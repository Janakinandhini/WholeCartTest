using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Playwright;
using static Microsoft.Playwright.Assertions;

namespace WholeCart.Automation;

internal sealed record Product(string Sku, string Name, string Seller, string SellerAccount);
internal sealed record DeliverySlot(string Date, string Time);
internal sealed record CartLine(Product Product, decimal Net, decimal Tax, int Pieces, int StockBefore, string ProductUrl);

internal sealed class WholeCartUi(IPage page)
{
    private static readonly Regex OrderNumber = new(@"^WC-\d+-\d+$");
    private static readonly Regex CasePrice = new(@"Case of\s+(\d+):\s*₹\s*([\d,.]+)");
    private static readonly Regex TaxRate = new(@"Tax\s+([\d.]+)%");
    private static readonly Regex Stock = new(@"In stock:\s*(-?\d+)\s+pieces");
    private static readonly Regex SlotTime = new(@"\d{2}:\d{2}\s*[–-]\s*\d{2}:\d{2}");
    private static readonly Regex Money = new(@"₹\s*([\d,.]+)");
    private static readonly Regex MinimumOrderMessage = new(
        @"minimum\b|below\s+(?:the\s+)?(?:order\s+)?minimum|at\s+least\s+₹?\s*2,?000",
        RegexOptions.IgnoreCase);

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

    public async Task<CartLine> AddCasesAsync(Product product, decimal minimumNet)
    {
        await OpenAsync("Catalogue", "Catalogue");
        await page.GetByPlaceholder("Search name or SKU", new() { Exact = true }).FillAsync(product.Sku);
        await Button("Search").ClickAsync();
        var result = RowWithCell(product.Sku);
        await result.GetByRole(AriaRole.Link, new() { Name = product.Name, Exact = true }).ClickAsync();
        await Expect(Heading(product.Name)).ToBeVisibleAsync();
        await Expect(page.GetByText($"Sold by {product.Seller}")).ToBeVisibleAsync();

        var priceText = await page.GetByText(CasePrice).InnerTextAsync();
        var casePrice = CasePrice.Match(priceText);
        var taxRate = TaxRate.Match(priceText);
        if (!taxRate.Success)
            taxRate = TaxRate.Match(await page.GetByText(TaxRate).InnerTextAsync());
        Assert.That(casePrice.Success && taxRate.Success, Is.True, "Expected a case price and tax rate.");

        var unitsPerCase = int.Parse(casePrice.Groups[1].Value, CultureInfo.InvariantCulture);
        var price = ParseDecimal(casePrice.Groups[2].Value);
        Assert.That(price, Is.Positive, $"{product.Sku} must have a positive case price.");
        var quantity = (int)Math.Ceiling(minimumNet / price);
        var inStock = await ReadStockAsync();
        var productUrl = page.Url;
        Assert.That(quantity, Is.InRange(1, 99), $"The quantity for {product.Sku} must fit one cart line.");
        Assert.That(inStock, Is.GreaterThanOrEqualTo(quantity * unitsPerCase),
            $"{product.Sku} needs {quantity * unitsPerCase} pieces for this test.");

        var form = page.Locator("form").Filter(new() { Has = Button("Add to cart") });
        await form.GetByRole(AriaRole.Combobox).SelectOptionAsync(new SelectOptionValue { Label = "Case" });
        await form.GetByRole(AriaRole.Spinbutton).FillAsync(quantity.ToString(CultureInfo.InvariantCulture));
        await Button("Add to cart").ClickAsync();
        await OpenAsync("Cart", "Cart");
        var cartRow = RowWithCell(product.Name);
        await Expect(cartRow.GetByRole(AriaRole.Cell, new() { Name = "case", Exact = true })).ToBeVisibleAsync();
        await Expect(cartRow.Locator("input[type=number], select"))
            .ToHaveValueAsync(quantity.ToString(CultureInfo.InvariantCulture));
        var net = quantity * price;
        await Expect(cartRow.GetByRole(AriaRole.Cell).Last).ToHaveTextAsync(MoneyText(net));
        var tax = Math.Round(net * ParseDecimal(taxRate.Groups[1].Value) / 100m, 2, MidpointRounding.AwayFromZero);
        return new CartLine(product, net, tax, quantity * unitsPerCase, inStock, productUrl);
    }

    public async Task<DeliverySlot> OpenCheckoutAsync(params string[] products)
    {
        await OpenCheckoutPageAsync(products);
        return await SelectDeliverySlotAsync();
    }

    public async Task CheckMinimumOrderRejectionAsync(params string[] products)
    {
        await OpenCheckoutPageAsync(products);
        var submit = Button("Place order");
        if (await submit.IsVisibleAsync() && await submit.IsEnabledAsync())
        {
            await SelectDeliverySlotAsync();
            if (await submit.IsEnabledAsync())
                await submit.ClickAsync();
        }

        var notice = page.Locator("main").GetByText(MinimumOrderMessage).Filter(new()
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
            $"Expected {line.Product.Sku} to have {expected} pieces in stock.").ToBeVisibleAsync();
    }

    private async Task<int> ReadStockAsync() =>
        int.Parse(Stock.Match(await page.GetByText(Stock).InnerTextAsync()).Groups[1].Value,
            CultureInfo.InvariantCulture);

    private async Task OpenCheckoutPageAsync(string[] products)
    {
        await OpenAsync("Cart", "Cart");
        foreach (var product in products)
            await Expect(RowWithCell(product)).ToBeVisibleAsync();

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
            return new DeliverySlot(date, time.Value);
        }

        throw new AssertionException("No available delivery slot in the next seven store dates.");
    }

    public async Task<decimal> CheckSellerQuoteAsync(CartLine line, bool expectFreeDelivery = false)
    {
        var cells = RowWithCell(line.Product.Seller).GetByRole(AriaRole.Cell);
        await Expect(cells.Nth(1)).ToHaveTextAsync(MoneyText(line.Net));
        await Expect(cells.Nth(2)).ToHaveTextAsync(MoneyText(line.Tax));
        var fee = ReadMoney(await cells.Nth(3).InnerTextAsync());
        if (expectFreeDelivery)
            Assert.That(fee, Is.Zero, "BR-08 waives the delivery fee at or above ₹3,000 net.");
        var total = line.Net + line.Tax + fee;
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
        var times = Regex.Matches(slot.Time, @"\d{2}:\d{2}").Select(match => match.Value).ToArray();
        var delivery = new Regex($@"^{Regex.Escape(slot.Date)}\s+{times[0]}\s*[–-]\s*{times[1]}$");
        await Expect(OrderRow(order).GetByRole(AriaRole.Cell, new() { NameRegex = delivery })).ToBeVisibleAsync();
        await Expect(OrderRow(order).GetByRole(AriaRole.Cell, new() { NameRegex = MoneyText(total) }))
            .ToBeVisibleAsync();
    }

    public Task ExpectStatusAsync(string order, string status) =>
        Expect(OrderRow(order).GetByRole(AriaRole.Cell, new()
        {
            NameRegex = new Regex($"^{Regex.Escape(status)}$", RegexOptions.IgnoreCase)
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
        await Expect(Heading($"Order {order}")).ToBeVisibleAsync();
        await Expect(page.GetByText(new Regex($@"Status:\s*{Regex.Escape(status)}\b", RegexOptions.IgnoreCase)))
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
