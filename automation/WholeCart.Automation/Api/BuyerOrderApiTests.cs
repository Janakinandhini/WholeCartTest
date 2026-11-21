using System.Globalization;
using System.Net;
using System.Text.Json;

namespace WholeCart.Automation.Api;

[TestFixture]
[NonParallelizable]
[Category("Api")]
public class BuyerOrderApiTests
{
    private static WholeCartTestData Data => WholeCartTestData.Current;

    private WholeCartApi? _api;
    private readonly HashSet<int> _addedProductIds = [];

    [TearDown]
    public async Task CleanTestCart()
    {
        if (_api is null)
            return;

        try
        {
            if (_addedProductIds.Count > 0)
                await _api.RemoveTestCartLinesAsync(_addedProductIds);
        }
        finally
        {
            _api.Dispose();
            _api = null;
            _addedProductIds.Clear();
        }
    }

    [Test]
    public async Task BuyerChecksOutAndCancelsItsOwnOrderViaApi()
    {
        var api = await SignInWithEmptyCartAsync(Data.Buyers.Buyer2.Username);
        var originalOrders = await api.GetOrdersAsync();
        var product = await api.GetProductAsync(Data.Products.Coffee.CatalogueQuery);
        var slot = await api.FindDeliverySlotAsync();
        await AddCasesAsync(api, product, Data.Checkout.MinimumOrderNet);

        var checkout = await api.SubmitCheckoutAsync(slot);
        await SaveCheckoutEvidenceAsync(checkout.Status, checkout.Body);
        checkout.RequireStatus(HttpStatusCode.OK, HttpStatusCode.Created);

        var currentOrders = await api.GetOrdersAsync();
        var newIds = currentOrders.Except(originalOrders).ToArray();
        Assert.That(newIds, Has.Length.EqualTo(1), "Single-seller checkout should create exactly one order.");
        var orderId = newIds[0];
        var order = await api.GetOrderAsync(orderId);
        Assert.Multiple(() =>
        {
            Assert.That(WholeCartApi.Text(order, "status"), Is.EqualTo("placed").IgnoreCase);
            Assert.That(WholeCartApi.Text(order, "slot_date", "delivery_date"), Is.EqualTo(slot.Date));
            Assert.That(WholeCartApi.Text(order, "slot_start"), Is.EqualTo(slot.Start));
        });
        Assert.That(await api.GetCartAsync(), Is.Empty, "A successful checkout should empty the cart.");

        await api.CancelOrderAsync(orderId);
        var cancelled = await api.GetOrderAsync(orderId);
        Assert.That(WholeCartApi.Text(cancelled, "status"), Is.EqualTo("cancelled").IgnoreCase);
    }

    [Test]
    [Category("CheckoutValidation")]
    public async Task CheckoutApiRejectsCartWhenOnlyOneSellerMissesTheMinimum()
    {
        var api = await SignInWithEmptyCartAsync(Data.Buyers.Buyer1.Username);
        var originalOrders = await api.GetOrdersAsync();
        var coffee = await api.GetProductAsync(Data.Products.Coffee.CatalogueQuery);
        var tea = await api.GetProductAsync(Data.Products.Tea.CatalogueQuery);
        var slot = await api.FindDeliverySlotAsync();

        var coffeeNet = await AddCasesAsync(api, coffee, Data.Checkout.BelowMinimumNetTarget);
        var teaNet = await AddCasesAsync(api, tea, Data.Checkout.MinimumOrderNet);
        Assert.Multiple(() =>
        {
            Assert.That(coffeeNet, Is.LessThan(Data.Checkout.MinimumOrderNet));
            Assert.That(teaNet, Is.GreaterThanOrEqualTo(Data.Checkout.MinimumOrderNet));
            Assert.That(coffeeNet + teaNet, Is.GreaterThan(Data.Checkout.MinimumOrderNet));
        });

        var checkout = await api.SubmitCheckoutAsync(slot);
        await SaveCheckoutEvidenceAsync(checkout.Status, checkout.Body);
        var currentOrders = await api.GetOrdersAsync();
        Assert.Multiple(() =>
        {
            checkout.RequireStatus(HttpStatusCode.BadRequest, HttpStatusCode.UnprocessableEntity);
            Assert.That(Data.Checkout.MinimumMessage().IsMatch(checkout.Body), Is.True,
                "CR-07 should be the reason checkout was rejected.");
            Assert.That(currentOrders, Is.EquivalentTo(originalOrders), "No sub-orders should have been created.");
        });
    }

    private async Task<WholeCartApi> SignInWithEmptyCartAsync(string buyer)
    {
        var settings = TestSettings.FromEnvironment();
        _api = new WholeCartApi(settings.BaseUrl);
        await _api.SignInAsync(buyer, settings.Password);
        Assert.That(await _api.GetCartAsync(), Is.Empty, $"Start with an empty cart for {buyer}.");
        return _api;
    }

    private async Task<decimal> AddCasesAsync(WholeCartApi api, ApiProduct product, decimal netTarget)
    {
        Assert.That(product.CasePrice, Is.Positive, $"{product.Sku} must have a positive case price.");
        var quantity = decimal.ToInt32(decimal.Ceiling(netTarget / product.CasePrice));
        Assert.That(quantity, Is.InRange(1, 99), $"{product.Sku} case quantity");
        Assert.That(product.Stock, Is.GreaterThanOrEqualTo(quantity * product.CaseSize),
            $"{product.Sku} needs enough stock for {quantity} cases.");

        _addedProductIds.Add(product.Id);
        await api.AddCasesAsync(product.Id, quantity);
        var cart = await api.GetCartAsync();
        var expectedNet = quantity * product.CasePrice;
        var line = cart.SingleOrDefault(item => WholeCartApi.ProductId(item) == product.Id &&
            WholeCartApi.Text(item, "unit") == "case");
        Assert.That(line.ValueKind, Is.EqualTo(JsonValueKind.Object), $"Expected a cart line for {product.Sku}.");
        Assert.That(WholeCartApi.Text(line, "qty"), Is.EqualTo(quantity.ToString(CultureInfo.InvariantCulture)));
        Assert.That(WholeCartApi.DecimalValue(line, "line_net"), Is.EqualTo(expectedNet));
        return expectedNet;
    }

    private static async Task SaveCheckoutEvidenceAsync(HttpStatusCode status, string body)
    {
        var root = Environment.GetEnvironmentVariable("WHOLECART_EVIDENCE_DIR");
        if (string.IsNullOrWhiteSpace(root))
            root = Path.Combine(TestContext.CurrentContext.WorkDirectory, "TestResults", "evidence");
        var test = TestContext.CurrentContext.Test.Name;
        var directory = Path.GetFullPath(Path.Combine(root, $"api-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}", test));
        Directory.CreateDirectory(directory);

        var path = Path.Combine(directory, "checkout-response.txt");
        await File.WriteAllTextAsync(path, $"POST api/orders\nHTTP {(int)status} {status}\n\n{body}\n");
        TestContext.AddTestAttachment(path, "Checkout API response");
    }
}
