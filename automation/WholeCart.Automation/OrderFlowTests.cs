using System.Globalization;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Playwright;
using NUnit.Framework.Interfaces;

namespace WholeCart.Automation;

[TestFixture]
[NonParallelizable]
public class OrderFlowTests
{
    private const decimal MinimumOrderNet = 2_000m;
    private const decimal FreeDeliveryNet = 3_000m;
    private static readonly Product Coffee = new("SKU-0381", "Chef's Cold Coffee", "FreshFarm Foods", "seller2");
    private static readonly Product Tea = new("SKU-0392", "Daily Iced Tea", "Coastal Beverages", "seller3");

    private static readonly string RunId = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
    private readonly List<(string Actor, IBrowserContext Context, IPage Page)> _sessions = [];
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Uri _baseUrl = null!;
    private string _password = null!;
    private string _evidenceDirectory = null!;
    private bool _recordVideo;

    [SetUp]
    public async Task StartBrowser()
    {
        var endpoint = Environment.GetEnvironmentVariable("WHOLECART_BASE_URL");
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "http://13.200.185.1/c/janaki-47b177/";

        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUrl) ||
            baseUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("WHOLECART_BASE_URL must be an HTTP(S) application URL.");
        }

        _baseUrl = baseUrl;
        _password = Environment.GetEnvironmentVariable("WHOLECART_PASSWORD")
            ?? throw new InvalidOperationException("Set WHOLECART_PASSWORD to the assignment test password.");
        if (string.IsNullOrWhiteSpace(_password))
            throw new InvalidOperationException("WHOLECART_PASSWORD must contain the assignment test password.");

        var evidenceRoot = Environment.GetEnvironmentVariable("WHOLECART_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(evidenceRoot))
            evidenceRoot = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "evidence");
        _evidenceDirectory = Path.GetFullPath(Path.Combine(evidenceRoot, RunId, TestContext.CurrentContext.Test.Name));
        _recordVideo = Environment.GetEnvironmentVariable("WHOLECART_VIDEO") == "1";
        Directory.CreateDirectory(_evidenceDirectory);
        TestContext.Progress.WriteLine($"Browser evidence: {_evidenceDirectory}");

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new()
        {
            Headless = Environment.GetEnvironmentVariable("WHOLECART_HEADED") != "1"
        });
    }

    [TearDown]
    public async Task CloseBrowser()
    {
        try
        {
            foreach (var (actor, context, page) in _sessions)
            {
                if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
                    await CaptureAsync(actor, "failure");

                var video = page.Video;
                // Playwright finishes writing video when its browser context closes.
                await context.CloseAsync();
                if (video is not null)
                {
                    var source = await video.PathAsync();
                    var path = Path.Combine(_evidenceDirectory, $"{actor}.webm");
                    File.Move(source, path);
                    TestContext.AddTestAttachment(path, $"{actor} browser recording");
                }
            }
        }
        finally
        {
            _sessions.Clear();
            try
            {
                if (_browser is not null)
                    await _browser.CloseAsync();
            }
            finally
            {
                _browser = null;
                _playwright?.Dispose();
                _playwright = null;
            }
        }
    }

    [Test]
    [Category("Smoke")]
    public async Task TestBuyerLoginAndNavigation()
    {
        var buyer = await SignInAsync("buyer1");
        await buyer.OpenAsync("Catalogue", "Catalogue");
        await CaptureAsync("buyer1", "catalogue");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task MultiSellerCheckoutCreatesTwoOrdersThatCanBeDeliveredAndInvoiced()
    {
        var buyer = await SignInAsync("buyer1");
        var freshFarm = await SignInAsync(Coffee.SellerAccount);
        var coastal = await SignInAsync(Tea.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var coffeeLine = await buyer.AddCasesAsync(Coffee, FreeDeliveryNet);
        var teaLine = await buyer.AddCasesAsync(Tea, FreeDeliveryNet);
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name, Tea.Name);
        var coffeeTotal = await buyer.CheckSellerQuoteAsync(coffeeLine, expectFreeDelivery: true);
        var teaTotal = await buyer.CheckSellerQuoteAsync(teaLine, expectFreeDelivery: true);
        await buyer.CheckGrandTotalAsync(coffeeTotal + teaTotal);
        await CaptureAsync("buyer1", "checkout");

        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 2);
        var coffeeOrder = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, coffeeTotal, checkout);
        var teaOrder = await buyer.FindSellerOrderAsync(orderNumbers, Tea.Seller, teaTotal, checkout);
        Assert.That(coffeeOrder[..coffeeOrder.LastIndexOf('-')],
            Is.EqualTo(teaOrder[..teaOrder.LastIndexOf('-')]),
            "The two sub-orders should share the checkout number.");
        await CaptureAsync("buyer1", "sub-orders-placed");
        await buyer.RequireEmptyCartAsync();
        await buyer.OpenAsync("Orders", "Order history");

        await FulfilAsync(freshFarm, buyer, coffeeOrder, checkout, coffeeTotal);
        await buyer.ExpectStatusAsync(teaOrder, "placed");
        await FulfilAsync(coastal, buyer, teaOrder, checkout, teaTotal);
        await CaptureAsync("buyer1", "sub-orders-delivered");
        await CaptureAsync(Coffee.SellerAccount, "seller-order-delivered");
        await CaptureAsync(Tea.SellerAccount, "seller-order-delivered");

        await buyer.CheckInvoiceAsync(coffeeOrder, Coffee, coffeeTotal);
        await CaptureAsync("buyer1", "freshfarm-invoice");
        await buyer.CheckInvoiceAsync(teaOrder, Tea, teaTotal);
        await CaptureAsync("buyer1", "coastal-invoice");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Cancellation")]
    public async Task SellerCancellationOfAcceptedOrderAppearsInBuyerHistory()
    {
        var buyer = await SignInAsync("buyer2");
        var seller = await SignInAsync(Coffee.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, MinimumOrderNet);
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);

        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, total, checkout);
        await seller.OpenAsync("Orders", "Orders");
        await seller.ExpectStatusAsync(order, "placed");
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await buyer.RefreshStatusAsync(order, "accepted");
        await seller.ChangeStatusAsync(order, "Cancel", "cancelled");
        await CaptureAsync(Coffee.SellerAccount, "seller-order-cancelled");
        await buyer.RefreshStatusAsync(order, "cancelled");
        await buyer.CheckOrderDetailStatusAsync(order, "cancelled");
        await CaptureAsync("buyer2", "buyer-order-cancelled");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("CheckoutValidation")]
    public async Task CheckoutRejectsOneSellerBelowMinimumEvenWhenCartTotalQualifies()
    {
        var buyer = await SignInAsync("buyer3");
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var belowMinimum = await buyer.AddCasesAsync(Coffee, 1_000m);
        var qualifying = await buyer.AddCasesAsync(Tea, MinimumOrderNet);
        Assert.That(belowMinimum.Net, Is.LessThan(MinimumOrderNet), "FreshFarm must be below the CR-07 minimum.");
        Assert.That(qualifying.Net, Is.GreaterThanOrEqualTo(MinimumOrderNet));
        Assert.That(belowMinimum.Net + qualifying.Net, Is.GreaterThan(MinimumOrderNet));

        await buyer.CheckMinimumOrderRejectionAsync(Coffee.Name, Tea.Name);
        await CaptureAsync("buyer3", "per-seller-minimum-rejection");
        var currentOrders = await buyer.ReadOrderNumbersAsync();
        Assert.That(currentOrders, Is.EquivalentTo(previousOrders),
            "A rejected checkout must create no seller sub-orders.");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Cancellation")]
    public async Task BuyerCancelsNewOrderAndStockReturnsToItsOriginalPieceCount()
    {
        var buyer = await SignInAsync("buyer2");
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, MinimumOrderNet);
        Assert.That(line.StockBefore, Is.GreaterThan(line.Pieces),
            "This fixture needs enough stock for the product to stay visible after checkout.");
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);
        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, total, checkout);

        await buyer.ExpectStockAsync(line, line.StockBefore - line.Pieces);
        await CaptureAsync("buyer2", "stock-after-checkout");
        await buyer.OpenAsync("Orders", "Order history");
        await buyer.CancelAsBuyerAsync(order);
        await CaptureAsync("buyer2", "buyer-cancellation-confirmed");
        await buyer.ExpectStockAsync(line, line.StockBefore);
        await CaptureAsync("buyer2", "stock-after-cancellation");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Operator")]
    public async Task AcceptedOrderAppearsInOperatorLogisticsExport()
    {
        var buyer = await SignInAsync("buyer2");
        var seller = await SignInAsync(Coffee.SellerAccount);
        var dispatcher = await SignInAsync("operator");
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, MinimumOrderNet);
        var slot = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);
        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, total, slot);

        await seller.OpenAsync("Orders", "Orders");
        await seller.CheckOrderSummaryAsync(order, slot, total);
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await buyer.RefreshStatusAsync(order, "accepted");
        await CaptureAsync(Coffee.SellerAccount, "order-accepted-for-dispatch");
        await CaptureAsync("buyer2", "accepted-order-delivery-slot");

        await CaptureAsync("operator", "logistics-export-control");
        var csvPath = Path.Combine(_evidenceDirectory, "logistics.csv");
        await dispatcher.DownloadLogisticsExportAsync(csvPath);
        TestContext.AddTestAttachment(csvPath, "Operator logistics export");
        CheckLogisticsExport(csvPath, order, slot, line.Pieces);
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Operator")]
    public async Task OperatorNavigationIsVisibleOnlyToTheOperator()
    {
        var dispatcher = await SignInAsync("operator");
        await dispatcher.CheckOperatorNavigationAsync(available: true);
        await CaptureAsync("operator", "operator-navigation");
        await dispatcher.CheckOperatorSellerListAsync("WholeCart Direct", Coffee.Seller, Tea.Seller);
        await CaptureAsync("operator", "marketplace-sellers");

        var buyer = await SignInAsync("buyer1");
        await buyer.CheckOperatorNavigationAsync(available: false);
        await CaptureAsync("buyer1", "buyer-navigation");

        var seller = await SignInAsync(Coffee.SellerAccount);
        await seller.CheckOperatorNavigationAsync(available: false);
        await CaptureAsync(Coffee.SellerAccount, "seller-navigation");
    }

    private static void CheckLogisticsExport(string path, string order, DeliverySlot slot, int pieces)
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
        var csv = utf8.GetString(File.ReadAllBytes(path)).TrimStart('\uFEFF');
        using var parser = new TextFieldParser(new StringReader(csv))
        {
            HasFieldsEnclosedInQuotes = true,
            TrimWhiteSpace = false
        };
        parser.SetDelimiters(",");
        string[] columns =
            ["order_number", "buyer_name", "delivery_address", "delivery_date", "slot_start", "slot_end", "pieces"];
        Assert.That(parser.ReadFields(), Is.EqualTo(columns), "IF-02 defines the logistics CSV column order.");

        var matches = new List<string[]>();
        while (parser.ReadFields() is { } row)
        {
            if (row.Length > 0 && row[0] == order)
                matches.Add(row);
        }

        Assert.That(matches, Has.Count.EqualTo(1), $"Expected one logistics row for accepted order {order}.");
        var exported = matches[0];
        Assert.That(exported, Has.Length.EqualTo(columns.Length));
        Assert.Multiple(() =>
        {
            Assert.That(exported[1], Is.EqualTo("Café Dosa Corner"), "Buyer name");
            Assert.That(exported[2].Trim(), Is.Not.Empty, "Delivery address");
            Assert.That(exported[3], Is.EqualTo(slot.Date), "Delivery date");
            Assert.That(exported[4], Is.EqualTo(slot.StartTime), "Slot start (DEF-14)");
            Assert.That(exported[5], Is.EqualTo(slot.EndTime), "Slot end (DEF-14)");
            Assert.That(exported[6], Is.EqualTo(pieces.ToString(CultureInfo.InvariantCulture)), "Piece count");
        });
    }

    private static async Task FulfilAsync(WholeCartUi seller, WholeCartUi buyer, string order,
        DeliverySlot slot, decimal total)
    {
        await seller.OpenAsync("Orders", "Orders");
        await seller.ExpectStatusAsync(order, "placed");
        await seller.CheckOrderSummaryAsync(order, slot, total);
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await buyer.RefreshStatusAsync(order, "accepted");
        await seller.ChangeStatusAsync(order, "Ship", "shipped");
        await buyer.RefreshStatusAsync(order, "shipped");
        await seller.ChangeStatusAsync(order, "(?:Mark (?:as )?)?Deliver(?:ed)?", "delivered");
        await buyer.RefreshStatusAsync(order, "delivered");
    }

    private async Task<WholeCartUi> SignInAsync(string username)
    {
        var context = await _browser!.NewContextAsync(new()
        {
            RecordVideoDir = _recordVideo ? _evidenceDirectory : null
        });
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(10_000);
        _sessions.Add((username, context, page));

        var app = new WholeCartUi(page);
        await app.SignInAsync(_baseUrl, username, _password);
        return app;
    }

    private async Task CaptureAsync(string actor, string checkpoint)
    {
        var page = _sessions.Single(session => session.Actor == actor).Page;
        if (page.IsClosed)
            return;

        try
        {
            var path = Path.Combine(_evidenceDirectory, $"{actor}-{checkpoint}.png");
            await page.ScreenshotAsync(new()
            {
                Path = path,
                FullPage = true,
                Timeout = 5_000,
                Mask = [page.GetByTestId("password")]
            });
            TestContext.AddTestAttachment(path, $"{actor} — {checkpoint}");
        }
        catch (PlaywrightException error)
        {
            TestContext.Progress.WriteLine($"Screenshot unavailable for {actor} at {checkpoint}: {error.Message}");
        }
    }
}
