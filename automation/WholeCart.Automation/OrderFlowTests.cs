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
    private static WholeCartTestData Data => WholeCartTestData.Current;
    private static Product Coffee => Data.Products.Coffee;
    private static Product Tea => Data.Products.Tea;
    private static Product ToorDal => Data.Products.ToorDal;

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
        (_baseUrl, _password) = TestSettings.FromEnvironment();

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
        var buyer = await SignInAsync(Data.Buyers.Buyer1.Username);
        await buyer.OpenAsync("Catalogue", "Catalogue");
        await CaptureAsync(Data.Buyers.Buyer1.Username, "catalogue");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task MultiSellerCheckoutCreatesTwoOrdersThatCanBeDeliveredAndInvoiced()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer1.Username);
        var freshFarm = await SignInAsync(Coffee.SellerAccount);
        var coastal = await SignInAsync(Tea.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var coffeeLine = await buyer.AddCasesAsync(Coffee, Data.Checkout.FreeDeliveryNet);
        var teaLine = await buyer.AddCasesAsync(Tea, Data.Checkout.FreeDeliveryNet);
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name, Tea.Name);
        var coffeeTotal = await buyer.CheckSellerQuoteAsync(coffeeLine, expectFreeDelivery: true);
        var teaTotal = await buyer.CheckSellerQuoteAsync(teaLine, expectFreeDelivery: true);
        await buyer.CheckGrandTotalAsync(coffeeTotal + teaTotal);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "checkout");

        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 2);
        var coffeeOrder = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, coffeeTotal, checkout);
        var teaOrder = await buyer.FindSellerOrderAsync(orderNumbers, Tea.Seller, teaTotal, checkout);
        Assert.That(coffeeOrder[..coffeeOrder.LastIndexOf('-')],
            Is.EqualTo(teaOrder[..teaOrder.LastIndexOf('-')]),
            "The two sub-orders should share the checkout number.");
        await CaptureAsync(Data.Buyers.Buyer1.Username, "sub-orders-placed");
        await buyer.RequireEmptyCartAsync();
        await buyer.OpenAsync("Orders", "Order history");

        await FulfilAsync(freshFarm, buyer, coffeeOrder, checkout, coffeeTotal);
        await buyer.ExpectStatusAsync(teaOrder, "placed");
        await FulfilAsync(coastal, buyer, teaOrder, checkout, teaTotal);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "sub-orders-delivered");
        await CaptureAsync(Coffee.SellerAccount, "seller-order-delivered");
        await CaptureAsync(Tea.SellerAccount, "seller-order-delivered");

        await buyer.CheckInvoiceAsync(coffeeOrder, Coffee, coffeeTotal);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "freshfarm-invoice");
        await buyer.CheckInvoiceAsync(teaOrder, Tea, teaTotal);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "coastal-invoice");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task BuyerChecksOutWithPiecesAndCasesOfTheSameProduct()
    {
        var username = Data.Buyers.Buyer1.Username;
        var buyer = await SignInAsync(username);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var cases = await buyer.AddCasesAsync(Coffee, Data.Checkout.MinimumOrderNet);
        var pieceQuantity = Data.Checkout.MixedCartPieceQuantity;
        Assert.That(cases.StockBefore, Is.GreaterThan(cases.Pieces + pieceQuantity),
            "This fixture needs stock for both cart lines and enough to keep the product visible after checkout.");
        var pieces = await buyer.AddPiecesAsync(Coffee, pieceQuantity);
        await buyer.CheckCartLineAsync(cases);
        await CaptureAsync(username, "pieces-and-cases-in-cart");

        var slot = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync([cases, pieces]);
        await buyer.CheckGrandTotalAsync(total);
        await buyer.PlaceOrderAsync();
        var orders = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orders, Coffee.Seller, total, slot);
        await buyer.CheckPlacedOrderLinesAsync(order, cases, pieces);
        await CaptureAsync(username, "pieces-and-cases-in-order");

        await buyer.RequireEmptyCartAsync();
        await buyer.ExpectStockAsync(cases, cases.StockBefore - cases.Pieces - pieces.Pieces);
        await CaptureAsync(username, "stock-after-mixed-unit-checkout");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Cancellation")]
    public async Task SellerCancellationOfAcceptedOrderAppearsInBuyerHistory()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer2.Username);
        var seller = await SignInAsync(Coffee.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, Data.Checkout.MinimumOrderNet);
        Assert.That(line.StockBefore, Is.GreaterThan(line.Pieces),
            "This fixture needs enough stock for the product to stay visible after checkout.");
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);

        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, total, checkout);
        await seller.OpenAsync("Orders", "Orders");
        await seller.ExpectStatusAsync(order, "placed");
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await buyer.RefreshStatusAsync(order, "accepted");
        await buyer.ExpectStockAsync(line, line.StockBefore - line.Pieces);
        await CaptureAsync(Data.Buyers.Buyer2.Username, "stock-before-seller-cancellation");
        await buyer.OpenAsync("Orders", "Order history");

        await seller.ChangeStatusAsync(order, "Cancel", "cancelled");
        await CaptureAsync(Coffee.SellerAccount, "seller-order-cancelled");
        await buyer.RefreshStatusAsync(order, "cancelled");
        await buyer.CheckOrderDetailStatusAsync(order, "cancelled");
        await CaptureAsync(Data.Buyers.Buyer2.Username, "buyer-order-cancelled");
        await buyer.ExpectStockAsync(line, line.StockBefore);
        await CaptureAsync(Data.Buyers.Buyer2.Username, "stock-after-seller-cancellation");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("CheckoutValidation")]
    public async Task CheckoutRejectsOneSellerBelowMinimumEvenWhenCartTotalQualifies()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer3.Username);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var belowMinimum = await buyer.AddCasesAsync(Coffee, Data.Checkout.BelowMinimumNetTarget);
        var qualifying = await buyer.AddCasesAsync(Tea, Data.Checkout.MinimumOrderNet);
        Assert.That(belowMinimum.Net, Is.LessThan(Data.Checkout.MinimumOrderNet), "FreshFarm must be below the CR-07 minimum.");
        Assert.That(qualifying.Net, Is.GreaterThanOrEqualTo(Data.Checkout.MinimumOrderNet));
        Assert.That(belowMinimum.Net + qualifying.Net, Is.GreaterThan(Data.Checkout.MinimumOrderNet));

        await buyer.CheckMinimumOrderRejectionAsync(Data.Checkout.MinimumMessage(), Coffee.Name, Tea.Name);
        await CaptureAsync(Data.Buyers.Buyer3.Username, "per-seller-minimum-rejection");
        var currentOrders = await buyer.ReadOrderNumbersAsync();
        Assert.That(currentOrders, Is.EquivalentTo(previousOrders),
            "A rejected checkout must create no seller sub-orders.");
    }

    [Test]
    [Category("EndToEnd")]
    public async Task BuyerCannotChangeDeliverySlotAfterSellerShipsOrder()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer1.Username);
        var seller = await SignInAsync(Tea.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Tea, Data.Checkout.MinimumOrderNet);
        var originalSlot = await buyer.OpenCheckoutAsync(Tea.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);
        await buyer.PlaceOrderAsync();
        var orders = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orders, Tea.Seller, total, originalSlot);

        await seller.OpenAsync("Orders", "Orders");
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await seller.ChangeStatusAsync(order, "Ship", "shipped");
        await buyer.RefreshStatusAsync(order, "shipped");
        await buyer.CheckSlotChangeBlockedAsync(order, originalSlot);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "post-shipment-slot-change-blocked");

        await buyer.OpenAsync("Orders", "Order history");
        await buyer.ExpectStatusAsync(order, "shipped");
        await buyer.CheckOrderSummaryAsync(order, originalSlot, total);
        await seller.RefreshStatusAsync(order, "shipped");
        await seller.CheckOrderSummaryAsync(order, originalSlot, total);
        await CaptureAsync(Tea.SellerAccount, "original-shipped-slot");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Cancellation")]
    public async Task BuyerCancelsNewOrderAndStockReturnsToItsOriginalPieceCount()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer2.Username);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, Data.Checkout.MinimumOrderNet);
        Assert.That(line.StockBefore, Is.GreaterThan(line.Pieces),
            "This fixture needs enough stock for the product to stay visible after checkout.");
        var checkout = await buyer.OpenCheckoutAsync(Coffee.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);
        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, total, checkout);

        await buyer.ExpectStockAsync(line, line.StockBefore - line.Pieces);
        await CaptureAsync(Data.Buyers.Buyer2.Username, "stock-after-checkout");
        await buyer.OpenAsync("Orders", "Order history");
        await buyer.CancelAsBuyerAsync(order);
        await CaptureAsync(Data.Buyers.Buyer2.Username, "buyer-cancellation-confirmed");
        await buyer.ExpectStockAsync(line, line.StockBefore);
        await CaptureAsync(Data.Buyers.Buyer2.Username, "stock-after-cancellation");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Operator")]
    public async Task AcceptedOrderAppearsInOperatorLogisticsExport()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer2.Username);
        var seller = await SignInAsync(Coffee.SellerAccount);
        var dispatcher = await SignInAsync(Data.OperatorAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(Coffee, Data.Checkout.MinimumOrderNet);
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
        await CaptureAsync(Data.Buyers.Buyer2.Username, "accepted-order-delivery-slot");

        await CaptureAsync(Data.OperatorAccount, "logistics-export-control");
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
        var dispatcher = await SignInAsync(Data.OperatorAccount);
        await dispatcher.CheckOperatorNavigationAsync(available: true);
        await CaptureAsync(Data.OperatorAccount, "operator-navigation");
        await dispatcher.CheckOperatorSellerListAsync(ToorDal.Seller, Coffee.Seller, Tea.Seller);
        await CaptureAsync(Data.OperatorAccount, "marketplace-sellers");

        var buyer = await SignInAsync(Data.Buyers.Buyer1.Username);
        await buyer.CheckOperatorNavigationAsync(available: false);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "buyer-navigation");

        var seller = await SignInAsync(Coffee.SellerAccount);
        await seller.CheckOperatorNavigationAsync(available: false);
        await CaptureAsync(Coffee.SellerAccount, "seller-navigation");
    }

    [Test]
    [Category("EndToEnd")]
    [Category("Redelivery")]
    public async Task WholeCartDirectReshipsAndDeliversAfterBuyerRequestsRedelivery()
    {
        var buyer = await SignInAsync(Data.Buyers.Buyer1.Username);
        var seller = await SignInAsync(ToorDal.SellerAccount);
        var previousOrders = await buyer.ReadOrderNumbersAsync();
        await buyer.RequireEmptyCartAsync();

        var line = await buyer.AddCasesAsync(ToorDal, Data.Checkout.MinimumOrderNet);
        var originalSlot = await buyer.OpenCheckoutAsync(ToorDal.Name);
        var total = await buyer.CheckSellerQuoteAsync(line);
        await buyer.PlaceOrderAsync();
        var orders = await buyer.FindNewOrdersAsync(previousOrders, 1);
        var order = await buyer.FindSellerOrderAsync(orders, ToorDal.Seller, total, originalSlot);

        await seller.OpenAsync("Orders", "Orders");
        await seller.ExpectStatusAsync(order, "placed");
        await seller.CheckOrderSummaryAsync(order, originalSlot, total);
        await seller.ChangeStatusAsync(order, "Accept", "accepted");
        await seller.ChangeStatusAsync(order, "Ship", "shipped");
        await buyer.RefreshStatusAsync(order, "shipped");
        await seller.ChangeStatusAsync(order, "(?:Mark (?:as )?)?(?:Fail(?:ed)?(?: delivery)?|Delivery failed)",
            "delivery failed");
        await CaptureAsync(ToorDal.SellerAccount, "delivery-failed");
        await buyer.RefreshStatusAsync(order, "delivery failed");

        var newSlot = await buyer.RequestRedeliveryAsync(order, originalSlot);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "redelivery-requested");
        await buyer.OpenAsync("Orders", "Order history");
        await buyer.ExpectStatusAsync(order, "redelivery requested");
        await buyer.CheckOrderSummaryAsync(order, newSlot, total);
        await seller.RefreshStatusAsync(order, "redelivery requested");
        await seller.CheckOrderSummaryAsync(order, newSlot, total);
        await CaptureAsync(ToorDal.SellerAccount, "redelivery-slot-confirmed");

        await seller.ChangeStatusAsync(order, "(?:Re[- ]?)?Ship", "shipped");
        await buyer.RefreshStatusAsync(order, "shipped");
        await seller.ChangeStatusAsync(order, "(?:Mark (?:as )?)?Deliver(?:ed)?", "delivered");
        await buyer.RefreshStatusAsync(order, "delivered");
        await buyer.CheckOrderSummaryAsync(order, newSlot, total);
        await CaptureAsync(Data.Buyers.Buyer1.Username, "redelivery-completed");
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
            Assert.That(exported[1], Is.EqualTo(Data.Buyers.Buyer2.BusinessName), "Buyer name");
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
