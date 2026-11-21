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

    private readonly List<(string Actor, IPage Page)> _sessions = [];
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private Uri _baseUrl = null!;
    private string _password = null!;

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
            if (TestContext.CurrentContext.Result.Outcome.Status == TestStatus.Failed)
                await AttachFailureScreenshotsAsync();

            if (_browser is not null)
                await _browser.CloseAsync();
        }
        finally
        {
            _sessions.Clear();
            _browser = null;
            _playwright?.Dispose();
            _playwright = null;
        }
    }

    [Test]
    [Category("Smoke")]
    public async Task TestBuyerLoginAndNavigation()
    {
        var buyer = await SignInAsync("buyer1");
        await buyer.OpenAsync("Catalogue", "Catalogue");
    }

    [Test]
    [Category("OrderFlow")]
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

        await buyer.PlaceOrderAsync();
        var orderNumbers = await buyer.FindNewOrdersAsync(previousOrders, 2);
        var coffeeOrder = await buyer.FindSellerOrderAsync(orderNumbers, Coffee.Seller, coffeeTotal, checkout);
        var teaOrder = await buyer.FindSellerOrderAsync(orderNumbers, Tea.Seller, teaTotal, checkout);
        Assert.That(coffeeOrder[..coffeeOrder.LastIndexOf('-')],
            Is.EqualTo(teaOrder[..teaOrder.LastIndexOf('-')]),
            "The two sub-orders should share the checkout number.");
        await buyer.RequireEmptyCartAsync();
        await buyer.OpenAsync("Orders", "Order history");

        await FulfilAsync(freshFarm, buyer, coffeeOrder, checkout, coffeeTotal);
        await buyer.ExpectStatusAsync(teaOrder, "placed");
        await FulfilAsync(coastal, buyer, teaOrder, checkout, teaTotal);

        await buyer.CheckInvoiceAsync(coffeeOrder, Coffee, coffeeTotal);
        await buyer.CheckInvoiceAsync(teaOrder, Tea, teaTotal);
    }

    [Test]
    [Category("OrderFlow")]
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
        await buyer.RefreshStatusAsync(order, "cancelled");
        await buyer.CheckOrderDetailStatusAsync(order, "cancelled");
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
        var context = await _browser!.NewContextAsync();
        var page = await context.NewPageAsync();
        page.SetDefaultTimeout(10_000);
        _sessions.Add((username, page));

        var app = new WholeCartUi(page);
        await app.SignInAsync(_baseUrl, username, _password);
        return app;
    }

    private async Task AttachFailureScreenshotsAsync()
    {
        foreach (var (actor, page) in _sessions)
        {
            if (page.IsClosed)
                continue;

            try
            {
                var path = Path.Combine(TestContext.CurrentContext.WorkDirectory,
                    $"{TestContext.CurrentContext.Test.Name}-{actor}.png");
                await page.ScreenshotAsync(new()
                {
                    Path = path,
                    FullPage = true,
                    Timeout = 5_000,
                    Mask = [page.GetByTestId("password")]
                });
                TestContext.AddTestAttachment(path, $"{actor}'s page at failure");
            }
            catch (PlaywrightException error)
            {
                TestContext.Progress.WriteLine($"Screenshot unavailable for {actor}: {error.Message}");
            }
        }
    }
}
