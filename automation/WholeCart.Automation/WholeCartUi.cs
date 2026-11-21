using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WholeCart.Automation;

internal sealed record DeliverySlot(string Date, string StartTime, string EndTime);
internal sealed record CartLine(Product Product, string Unit, int Quantity, decimal Net, decimal TaxRate, int Pieces,
    int StockBefore, string ProductUrl);

internal sealed class WholeCartUi(IPage page)
{
    private static readonly Regex OrderNumber = new(@"^WC-\d+-\d+$");
    private static readonly Regex CasePrice = new(@"Case of\s+(\d+):\s*₹\s*([\d,.]+)");
    private static readonly Regex PiecePrice = new(@"Piece:\s*₹\s*([\d,.]+)");
    private static readonly Regex TaxRate = new(@"Tax\s+([\d.]+)%");
    private static readonly Regex Stock = new(@"In stock:\s*(-?\d+)\s+pieces");
    private static readonly Regex SlotTime = new(@"(\d{2}:\d{2})\s*[–-]\s*(\d{2}:\d{2})");
    private static readonly Regex Money = new(@"₹\s*([\d,.]+)");
    private static readonly Regex LogisticsExport = new("^Logistics export$", RegexOptions.IgnoreCase);
    private static readonly Regex Sellers = new("^Sellers$", RegexOptions.IgnoreCase);

    public async Task SignInAsync(Uri endpoint, string username, string password)
    {
        await page.GotoAsync(endpoint.AbsoluteUri);
        await Expect(Heading("Log in")).ToBeVisibleAsync();
        await page.GetByTestId("username").FillAsync(username);
        await page.GetByTestId("password").FillAsync(password);
        await page.GetByTestId("login").ClickAsync();
        await Expect(Control(new Regex(@"^Log out$", RegexOptions.IgnoreCase))).ToBeVisibleAsync();
    }

    public async Task OpenAsync(string link, string heading)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = link, Exact = true }).ClickAsync();
        await Expect(Heading(heading)).ToBeVisibleAsync();
    }

    public async Task CheckOperatorNavigationAsync(bool available)
    {
        foreach (var name in new[] { Sellers, LogisticsExport })
        {
            var control = Control(name);
            if (available)
                await Expect(control).ToBeVisibleAsync();
            else
                await Expect(control).ToHaveCountAsync(0);
        }
    }

    public async Task CheckOperatorSellerListAsync(params string[] sellers)
    {
        await Control(Sellers).ClickAsync();
        await Expect(Heading("Sellers")).ToBeVisibleAsync();
        foreach (var seller in sellers)
            await Expect(RowWithCell(seller)).ToBeVisibleAsync();
    }

    public async Task DownloadLogisticsExportAsync(string path)
    {
        var download = await page.RunAndWaitForDownloadAsync(() => Control(LogisticsExport).ClickAsync());
        Assert.That(Path.GetExtension(download.SuggestedFilename), Is.EqualTo(".csv").IgnoreCase);
        await download.SaveAsAsync(path);
    }

    public async Task RequireEmptyCartAsync()
    {
        await OpenAsync("Cart", "Cart");
        await Expect(Control(new Regex("^Proceed to checkout$")), "Expected an empty test account cart.")
            .ToHaveCountAsync(0);
        await Expect(page.Locator("main table tbody tr").Filter(new()
        {
            Has = page.Locator("input[type=number], select")
        }), "The cart must have no existing product lines.").ToHaveCountAsync(0);
    }

    public Task<CartLine> AddCasesAsync(Product product, decimal minimumNet) =>
        AddToCartAsync(product, "case", price => (int)Math.Ceiling(minimumNet / price));

    public Task<CartLine> AddPiecesAsync(Product product, int quantity) =>
        AddToCartAsync(product, "piece", _ => quantity);

    private async Task<CartLine> AddToCartAsync(Product product, string unit, Func<decimal, int> getQuantity)
    {
        await OpenAsync("Catalogue", "Catalogue");
        await page.GetByPlaceholder("Search name or SKU", new() { Exact = true }).FillAsync(product.CatalogueQuery);
        await Button("Search").ClickAsync();
        var result = RowWithCell(product.CatalogueQuery);
        await result.GetByRole(AriaRole.Link, new() { Name = product.Name, Exact = true }).ClickAsync();
        await Expect(Heading(product.Name)).ToBeVisibleAsync();
        await Expect(page.GetByText($"Sold by {product.Seller}")).ToBeVisibleAsync();

        var pricePattern = unit == "case" ? CasePrice : PiecePrice;
        var priceText = await page.GetByText(pricePattern).InnerTextAsync();
        var priceMatch = pricePattern.Match(priceText);
        var taxRate = TaxRate.Match(priceText);
        if (!taxRate.Success)
            taxRate = TaxRate.Match(await page.GetByText(TaxRate).InnerTextAsync());
        Assert.That(priceMatch.Success && taxRate.Success, Is.True, $"Expected a {unit} price and tax rate.");

        var piecesPerUnit = unit == "case" ? int.Parse(priceMatch.Groups[1].Value, CultureInfo.InvariantCulture) : 1;
        var price = ParseDecimal(priceMatch.Groups[^1].Value);
        Assert.That(price, Is.Positive, $"{product.Name} must have a positive {unit} price.");
        var quantity = getQuantity(price);
        var inStock = await ReadStockAsync();
        var productUrl = page.Url;
        Assert.That(quantity, Is.InRange(1, 99), $"The quantity for {product.Name} must fit one cart line.");
        var pieces = quantity * piecesPerUnit;
        Assert.That(inStock, Is.GreaterThanOrEqualTo(pieces), $"{product.Name} needs {pieces} pieces for this test.");

        var form = page.Locator("form").Filter(new() { Has = Button("Add to cart") });
        var label = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(unit);
        await form.GetByRole(AriaRole.Combobox).SelectOptionAsync(new SelectOptionValue { Label = label });
        await form.GetByRole(AriaRole.Spinbutton).FillAsync(quantity.ToString(CultureInfo.InvariantCulture));
        await Button("Add to cart").ClickAsync();
        await OpenAsync("Cart", "Cart");
        var line = new CartLine(product, unit, quantity, quantity * price, ParseDecimal(taxRate.Groups[1].Value),
            pieces, inStock, productUrl);
        await CheckCartLineAsync(line);
        return line;
    }

    public async Task CheckCartLineAsync(CartLine line)
    {
        var row = ProductUnitRow(line);
        await Expect(row).ToBeVisibleAsync();
        await Expect(row.Locator("input[type=number], select"))
            .ToHaveValueAsync(line.Quantity.ToString(CultureInfo.InvariantCulture));
        await Expect(row.GetByRole(AriaRole.Cell).Last).ToHaveTextAsync(MoneyText(line.Net));
    }

    public async Task<DeliverySlot> OpenCheckoutAsync(params string[] products)
    {
        await OpenCheckoutPageAsync(products);
        return await SelectDeliverySlotAsync();
    }

    public async Task CheckMinimumOrderRejectionAsync(Regex minimumMessage, params string[] products)
    {
        await OpenCheckoutPageAsync(products);
        var submit = Button("Place order");
        if (await submit.IsVisibleAsync() && await submit.IsEnabledAsync())
        {
            await SelectDeliverySlotAsync();
            if (await submit.IsEnabledAsync())
                await submit.ClickAsync();
        }

        var notice = page.Locator("main").GetByText(minimumMessage).Filter(new()
        {
            Visible = true
        });
        await Expect(notice.First, "CR-07 requires a visible per-seller minimum order message.")
            .ToBeVisibleAsync();
    }

    public async Task ExpectStockAsync(CartLine line, int expected)
    {
        await page.GotoAsync(line.ProductUrl);
        await Expect(page.GetByText(new Regex($@"In stock:\s*{expected}\s+pieces")),
            $"Expected {line.Product.Name} to have {expected} pieces in stock.").ToBeVisibleAsync();
    }

    private async Task<int> ReadStockAsync() =>
        int.Parse(Stock.Match(await page.GetByText(Stock).InnerTextAsync()).Groups[1].Value,
            CultureInfo.InvariantCulture);

    private async Task OpenCheckoutPageAsync(string[] products)
    {
        await OpenAsync("Cart", "Cart");
        foreach (var product in products)
            await Expect(RowWithCell(product).First).ToBeVisibleAsync();

        await Control(new Regex("^Proceed to checkout$")).ClickAsync();
        await Expect(Heading("Checkout")).ToBeVisibleAsync();
    }

    private async Task<DeliverySlot> SelectDeliverySlotAsync()
    {
        var dateField = page.Locator("main input[type=date]");
        var storeDate = await dateField.GetAttributeAsync("min") ?? await dateField.InputValueAsync();
        var firstDate = DateOnly.ParseExact(storeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        // Start tomorrow so the test does not depend on the time of the same-day cut-off.
        for (var offset = 1; offset <= 7; offset++)
        {
            var date = firstDate.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            await dateField.FillAsync(date);
            await Button("Show slots").ClickAsync();
            await Expect(dateField).ToHaveValueAsync(date);

            var radio = page.Locator("main input[type=radio]:enabled").First;
            if (await radio.CountAsync() == 0)
                continue;

            var label = await radio.EvaluateAsync<string>("input => input.labels[0]?.textContent?.trim() ?? ''");
            var time = SlotTime.Match(label);
            Assert.That(time.Success, Is.True, $"Could not read the delivery time from '{label}'.");
            await radio.CheckAsync();
            return new DeliverySlot(date, time.Groups[1].Value, time.Groups[2].Value);
        }

        throw new AssertionException("No available delivery slot in the next seven store dates.");
    }

    public Task<decimal> CheckSellerQuoteAsync(CartLine line, bool expectFreeDelivery = false) =>
        CheckSellerQuoteAsync([line], expectFreeDelivery);

    public async Task<decimal> CheckSellerQuoteAsync(CartLine[] lines, bool expectFreeDelivery = false)
    {
        var net = lines.Sum(line => line.Net);
        var tax = lines.GroupBy(line => line.TaxRate).Sum(group =>
            Math.Round(group.Sum(line => line.Net) * group.Key / 100m, 2, MidpointRounding.AwayFromZero));
        var cells = RowWithCell(lines[0].Product.Seller).GetByRole(AriaRole.Cell);
        await Expect(cells.Nth(1)).ToHaveTextAsync(MoneyText(net));
        await Expect(cells.Nth(2)).ToHaveTextAsync(MoneyText(tax));
        var fee = ReadMoney(await cells.Nth(3).InnerTextAsync());
        if (expectFreeDelivery)
            Assert.That(fee, Is.Zero, "BR-08 waives the delivery fee when the seller's net meets the threshold.");
        var total = net + tax + fee;
        await Expect(cells.Nth(4)).ToHaveTextAsync(MoneyText(total));
        return total;
    }

    public Task CheckGrandTotalAsync(decimal expected) =>
        Expect(RowWithCell("Grand total").GetByRole(AriaRole.Cell).Last).ToHaveTextAsync(MoneyText(expected));

    public async Task PlaceOrderAsync()
    {
        await Button("Place order").ClickAsync();
        await Expect(Heading("Order history")).ToBeVisibleAsync();
    }

    public async Task<HashSet<string>> ReadOrderNumbersAsync()
    {
        await OpenAsync("Orders", "Order history");
        return await VisibleOrderNumbersAsync();
    }

    public async Task<string[]> FindNewOrdersAsync(HashSet<string> previousOrders, int expectedCount)
    {
        var newOrders = (await VisibleOrderNumbersAsync()).Except(previousOrders).ToArray();
        TestContext.Progress.WriteLine($"New order numbers: {string.Join(", ", newOrders)}");
        Assert.That(newOrders, Has.Length.EqualTo(expectedCount), "Unexpected number of new seller sub-orders.");
        return newOrders;
    }

    public async Task<string> FindSellerOrderAsync(string[] orderNumbers, string seller, decimal total,
        DeliverySlot slot)
    {
        var matching = OrderRow(orderNumbers[0]);
        foreach (var number in orderNumbers.Skip(1))
            matching = matching.Or(OrderRow(number));

        matching = matching.Filter(new()
        {
            Has = page.GetByRole(AriaRole.Cell, new() { Name = seller, Exact = true })
        });
        await Expect(matching).ToHaveCountAsync(1);
        var order = (await matching.GetByRole(AriaRole.Cell).First.InnerTextAsync()).Trim();
        await ExpectStatusAsync(order, "placed");
        await CheckOrderSummaryAsync(order, slot, total);
        return order;
    }

    public async Task CheckOrderSummaryAsync(string order, DeliverySlot slot, decimal total)
    {
        var delivery = new Regex($@"^{Regex.Escape(slot.Date)}\s+{slot.StartTime}\s*[–-]\s*{slot.EndTime}$");
        await Expect(OrderRow(order).GetByRole(AriaRole.Cell, new() { NameRegex = delivery })).ToBeVisibleAsync();
        await Expect(OrderRow(order).GetByRole(AriaRole.Cell, new() { NameRegex = MoneyText(total) }))
            .ToBeVisibleAsync();
    }

    public Task ExpectStatusAsync(string order, string status) =>
        Expect(OrderRow(order).GetByRole(AriaRole.Cell, new()
        {
            NameRegex = new Regex($"^{StatusPattern(status)}$", RegexOptions.IgnoreCase)
        }), $"Expected order {order} to be {status}.").ToBeVisibleAsync();

    public async Task RefreshStatusAsync(string order, string status)
    {
        await page.ReloadAsync();
        await ExpectStatusAsync(order, status);
    }

    public async Task ChangeStatusAsync(string order, string action, string status)
    {
        var control = OrderRow(order).GetByRole(AriaRole.Button, new()
        {
            NameRegex = new Regex($"^(?:{action})$", RegexOptions.IgnoreCase)
        });
        await ClickWithConfirmationAsync(control);
        await ExpectStatusAsync(order, status);
    }

    public async Task CancelAsBuyerAsync(string order)
    {
        await CheckOrderDetailStatusAsync(order, "placed");
        await ClickWithConfirmationAsync(Button("Cancel order"));
        await OpenAsync("Orders", "Order history");
        await ExpectStatusAsync(order, "cancelled");
        await CheckOrderDetailStatusAsync(order, "cancelled");
    }

    private async Task ClickWithConfirmationAsync(ILocator control)
    {
        async void HandleDialog(object? sender, IDialog dialog)
        {
            if (dialog.Type == "confirm")
                await dialog.AcceptAsync();
            else
                await dialog.DismissAsync();
        }

        page.Dialog += HandleDialog;
        try
        {
            await control.ClickAsync();
        }
        finally
        {
            page.Dialog -= HandleDialog;
        }
    }

    public async Task CheckOrderDetailStatusAsync(string order, string status)
    {
        await Control(new Regex("^View$"), OrderRow(order)).ClickAsync();
        await ExpectOrderDetailStatusAsync(order, status);
    }

    public async Task CheckPlacedOrderLinesAsync(string order, params CartLine[] lines)
    {
        await CheckOrderDetailStatusAsync(order, "placed");
        foreach (var line in lines)
        {
            var cells = ProductUnitRow(line).GetByRole(AriaRole.Cell);
            await Expect(cells.Nth(2)).ToHaveTextAsync(line.Quantity.ToString(CultureInfo.InvariantCulture));
            await Expect(cells.Last).ToHaveTextAsync(MoneyText(line.Net));
        }
    }

    public async Task<DeliverySlot> RequestRedeliveryAsync(string order, DeliverySlot originalSlot)
    {
        await CheckOrderDetailStatusAsync(order, "delivery failed");
        var request = Button("Request redelivery");
        var newSlot = await SelectAlternativeDeliverySlotAsync(request, originalSlot);
        await ClickWithConfirmationAsync(request);
        await ExpectOrderDetailStatusAsync(order, "redelivery requested");
        return newSlot;
    }

    public async Task CheckSlotChangeBlockedAsync(string order, DeliverySlot originalSlot)
    {
        await CheckOrderDetailStatusAsync(order, "shipped");
        var change = Button("Change slot");
        if (!await change.IsVisibleAsync() || !await change.IsEnabledAsync())
            return;

        await SelectAlternativeDeliverySlotAsync(change, originalSlot);
        await ClickWithConfirmationAsync(change);
        var rejection = new Regex(
            @"slot can only be changed before .*ship|(?:cannot|can't) change .*slot.*ship|order has already shipped",
            RegexOptions.IgnoreCase);
        await Expect(page.GetByText(rejection), "BR-15 blocks slot changes after shipment.").ToBeVisibleAsync();
    }

    private async Task<DeliverySlot> SelectAlternativeDeliverySlotAsync(ILocator action, DeliverySlot originalSlot)
    {
        var form = page.Locator("form").Filter(new() { Has = action });
        var dateInput = form.Locator("input[type=date]");
        var slotSelect = form.GetByRole(AriaRole.Combobox);

        // Keep the future delivery date and request a different time window.
        await dateInput.FillAsync(originalSlot.Date);
        var options = await slotSelect.Locator("option:enabled").AllAsync();
        foreach (var option in options)
        {
            var label = (await option.InnerTextAsync()).Trim();
            var time = SlotTime.Match(label);
            if (!time.Success || time.Groups[1].Value == originalSlot.StartTime)
                continue;

            await slotSelect.SelectOptionAsync(new SelectOptionValue { Label = label });
            return new DeliverySlot(originalSlot.Date, time.Groups[1].Value, time.Groups[2].Value);
        }

        throw new AssertionException($"No alternative delivery window for {originalSlot.Date} was offered.");
    }

    private async Task ExpectOrderDetailStatusAsync(string order, string status)
    {
        await Expect(Heading($"Order {order}")).ToBeVisibleAsync();
        await Expect(page.GetByText(new Regex($@"Status:\s*{StatusPattern(status)}\b", RegexOptions.IgnoreCase)))
            .ToBeVisibleAsync();
    }

    public async Task CheckInvoiceAsync(string order, Product product, decimal total)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Invoices", Exact = true }).ClickAsync();
        var row = OrderRow(order);
        await Expect(row, $"Expected an invoice for delivered order {order}.").ToBeVisibleAsync();
        await row.GetByRole(AriaRole.Link, new() { NameRegex = new Regex(@"^(?:.*INV-\d+|View(?: invoice)?)$",
            RegexOptions.IgnoreCase) }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { NameRegex = new Regex(@"^Tax invoice\b") }))
            .ToBeVisibleAsync();
        await Expect(page.Locator("main")).ToContainTextAsync(order);
        await Expect(page.Locator("main")).ToContainTextAsync(product.Seller);
        await Expect(page.GetByRole(AriaRole.Cell, new()
        {
            NameRegex = new Regex($@"^{Regex.Escape(product.Name)}\s*\(case\)$", RegexOptions.IgnoreCase)
        })).ToBeVisibleAsync();
        await Expect(page.GetByText(new Regex($@"\bTotal\s+{MoneyPattern(total)}\s*$")))
            .ToBeVisibleAsync();
    }

    private async Task<HashSet<string>> VisibleOrderNumbersAsync()
    {
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Order history", Exact = true }))
            .ToBeVisibleAsync();
        var cells = await page.GetByRole(AriaRole.Cell, new() { NameRegex = OrderNumber }).AllTextContentsAsync();
        return cells.Select(text => text.Trim()).ToHashSet(StringComparer.Ordinal);
    }

    private ILocator Heading(string name) => page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true });

    private ILocator Button(string name) => page.GetByRole(AriaRole.Button, new() { Name = name, Exact = true });

    private ILocator Control(Regex name, ILocator? within = null)
    {
        var scope = within ?? page.Locator("body");
        return scope.GetByRole(AriaRole.Link, new() { NameRegex = name })
            .Or(scope.GetByRole(AriaRole.Button, new() { NameRegex = name }));
    }

    private ILocator RowWithCell(string text) => page.GetByRole(AriaRole.Row).Filter(new()
    {
        Has = page.GetByRole(AriaRole.Cell, new() { Name = text, Exact = true })
    });

    private ILocator OrderRow(string order) => RowWithCell(order);

    private ILocator ProductUnitRow(CartLine line) => RowWithCell(line.Product.Name).Filter(new()
    {
        Has = page.GetByRole(AriaRole.Cell, new() { Name = line.Unit, Exact = true })
    });

    private static string StatusPattern(string status) =>
        string.Join(@"[\s_-]+", status.Split(' ').Select(Regex.Escape));

    private static Regex MoneyText(decimal amount) => new($"^{MoneyPattern(amount)}$");

    private static string MoneyPattern(decimal amount)
    {
        var plain = Regex.Escape(amount.ToString("0.00", CultureInfo.InvariantCulture));
        var grouped = Regex.Escape(amount.ToString("N2", CultureInfo.InvariantCulture));
        var indian = Regex.Escape(amount.ToString("N2", CultureInfo.GetCultureInfo("en-IN")));
        return $@"₹\s*(?:{plain}|{grouped}|{indian})";
    }

    private static decimal ReadMoney(string text)
    {
        var match = Money.Match(text);
        Assert.That(match.Success, Is.True, $"Expected a rupee amount in '{text}'.");
        return ParseDecimal(match.Groups[1].Value);
    }

    private static decimal ParseDecimal(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);
}
