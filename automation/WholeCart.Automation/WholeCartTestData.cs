using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WholeCart.Automation;

internal sealed record BuyerFixture(string Username, string BusinessName);
internal sealed record BuyerFixtures(BuyerFixture Buyer1, BuyerFixture Buyer2, BuyerFixture Buyer3);
internal sealed record Product(string CatalogueQuery, string Name, string Seller, string SellerAccount);
internal sealed record ProductFixtures(Product Coffee, Product Tea, Product ToorDal);

internal sealed record CheckoutFixture(decimal MinimumOrderNet, decimal FreeDeliveryNet, decimal BelowMinimumNetTarget,
    int MixedCartPieceQuantity)
{
    public Regex MinimumMessage()
    {
        var plain = MinimumOrderNet.ToString("0.##", CultureInfo.InvariantCulture);
        var grouped = MinimumOrderNet.ToString("#,0.##", CultureInfo.GetCultureInfo("en-IN"));
        return new Regex($@"\bminimum\b|at\s+least\s+₹?\s*(?:{Regex.Escape(plain)}|{Regex.Escape(grouped)})(?!\d)",
            RegexOptions.IgnoreCase);
    }
}

internal sealed record WholeCartTestData(
    string Password,
    BuyerFixtures Buyers,
    string OperatorAccount,
    ProductFixtures Products,
    CheckoutFixture Checkout)
{
    public static WholeCartTestData Current { get; } = Load();

    private static WholeCartTestData Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "wholecart.json");
        using var file = File.OpenRead(path);
        var data = JsonSerializer.Deserialize<WholeCartTestData>(file, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new InvalidOperationException($"Test data is empty in {path}.");

        if (string.IsNullOrWhiteSpace(data.Password))
            throw new InvalidOperationException($"The assignment test password is missing in {path}.");

        if (data.Checkout is null ||
            data.Checkout.BelowMinimumNetTarget <= 0 ||
            data.Checkout.BelowMinimumNetTarget >= data.Checkout.MinimumOrderNet ||
            data.Checkout.FreeDeliveryNet < data.Checkout.MinimumOrderNet ||
            data.Checkout.MixedCartPieceQuantity is < 1 or > 99)
        {
            throw new InvalidOperationException($"Checkout amounts are invalid in {path}.");
        }

        return data;
    }
}
