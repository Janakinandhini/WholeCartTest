using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WholeCart.Automation.Api;

internal sealed record ApiProduct(int Id, string Sku, decimal CasePrice, int CaseSize, int Stock);
internal sealed record ApiDeliverySlot(string Date, string Start);

internal sealed record ApiReply(HttpStatusCode Status, string Method, string Route, string Body)
{
    public void RequireStatus(params HttpStatusCode[] expected) =>
        Assert.That(expected, Does.Contain(Status), $"{Method} {Route} returned HTTP {(int)Status}.");

    public JsonElement Json()
    {
        if (string.IsNullOrWhiteSpace(Body))
            throw new AssertionException($"{Method} {Route} returned an empty response where JSON was expected.");

        using var document = JsonDocument.Parse(Body);
        return document.RootElement.Clone();
    }
}

internal sealed class WholeCartApi(Uri baseUrl) : IDisposable
{
    private readonly HttpClient _client = new(new HttpClientHandler { AllowAutoRedirect = false })
    {
        BaseAddress = baseUrl,
        Timeout = TimeSpan.FromSeconds(10)
    };

    public async Task SignInAsync(string buyer, string password)
    {
        var login = await SendAsync(HttpMethod.Post, "api/login", new { username = buyer, password });
        login.RequireStatus(HttpStatusCode.OK);
        if (!string.IsNullOrWhiteSpace(login.Body))
        {
            var token = Text(login.Json(), "access_token", "token");
            if (!string.IsNullOrWhiteSpace(token))
                _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        var identity = await GetAsync("api/me");
        var username = Text(Unwrap(identity, "user", "actor"), "username");
        if (username is not null)
            Assert.That(username, Is.EqualTo(buyer), "The API session should belong to the buyer who signed in.");
    }

    public async Task<ApiProduct> GetProductAsync(string sku)
    {
        var search = await GetAsync($"api/products?q={Uri.EscapeDataString(sku)}");
        var matches = Rows(search, "products", "items").Where(item => Text(item, "sku") == sku).ToArray();
        Assert.That(matches, Has.Length.EqualTo(1), $"Expected one catalogue product for {sku}.");
        var id = ProductId(matches[0]);
        var product = Unwrap(await GetAsync($"api/products/{id}"), "product");
        return new ApiProduct(id, sku, DecimalValue(product, "case_price"), IntegerValue(product, "case_size"),
            IntegerValue(product, "stock"));
    }

    public async Task<JsonElement[]> GetCartAsync() =>
        Rows(Unwrap(await GetAsync("api/cart"), "cart"), "items", "lines");

    public async Task<HashSet<int>> GetOrdersAsync() =>
        Rows(await GetAsync("api/orders"), "orders", "items")
            .Select(order => IntegerValue(order, "id", "order_id"))
            .ToHashSet();

    public async Task<JsonElement> GetOrderAsync(int orderId) =>
        Unwrap(await GetAsync($"api/orders/{orderId}"), "order");

    public async Task<ApiDeliverySlot> FindDeliverySlotAsync()
    {
        // WholeCart uses local time in India. Override it when the test store clock is fixed.
        var storeDate = Environment.GetEnvironmentVariable("WHOLECART_STORE_DATE");
        var today = string.IsNullOrWhiteSpace(storeDate)
            ? DateOnly.FromDateTime(DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromMinutes(330)).DateTime)
            : DateOnly.ParseExact(storeDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        for (var offset = 1; offset <= 7; offset++)
        {
            var date = today.AddDays(offset).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var slots = Rows(await GetAsync($"api/slots?date={date}"), "slots");
            foreach (var slot in slots.Where(IsAvailable))
            {
                var start = Text(slot, "slot_start", "start");
                if (!string.IsNullOrWhiteSpace(start))
                    return new ApiDeliverySlot(date, start);
            }
        }

        throw new AssertionException("The slots API returned no available future slot. Check the store date and capacity.");
    }

    public Task<ApiReply> SubmitCheckoutAsync(ApiDeliverySlot slot) =>
        SendAsync(HttpMethod.Post, "api/orders", new { slot_date = slot.Date, slot_start = slot.Start });

    public async Task CancelOrderAsync(int orderId)
    {
        var response = await SendAsync(HttpMethod.Post, $"api/orders/{orderId}/cancel");
        response.RequireStatus(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    public async Task AddCasesAsync(int productId, int quantity)
    {
        var response = await SendAsync(HttpMethod.Post, "api/cart/items",
            new { product_id = productId, unit = "case", qty = quantity });
        response.RequireStatus(HttpStatusCode.OK, HttpStatusCode.Created);
    }

    public async Task RemoveTestCartLinesAsync(ISet<int> addedProductIds)
    {
        foreach (var item in await GetCartAsync())
        {
            var id = ProductId(item);
            if (!addedProductIds.Contains(id) || Text(item, "unit") != "case")
                continue;

            await DeleteCartLineAsync(id, "case");
        }
    }

    public async Task ClearCartAsync()
    {
        foreach (var item in await GetCartAsync())
        {
            var unit = Text(item, "unit") ?? throw new AssertionException("The cart line has no unit.");
            await DeleteCartLineAsync(ProductId(item), unit);
        }
    }

    private async Task DeleteCartLineAsync(int productId, string unit)
    {
        var response = await SendAsync(HttpMethod.Delete,
            $"api/cart/items?product_id={productId}&unit={Uri.EscapeDataString(unit)}");
        response.RequireStatus(HttpStatusCode.OK, HttpStatusCode.NoContent);
    }

    public static int ProductId(JsonElement item) =>
        Text(item, "product_id") is { } productId
            ? int.Parse(productId, CultureInfo.InvariantCulture)
            : IntegerValue(Unwrap(item, "product"), "id");

    public static decimal DecimalValue(JsonElement item, params string[] names)
    {
        if (decimal.TryParse(Text(item, names), NumberStyles.Number, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new AssertionException($"Expected a numeric API field named {string.Join(" or ", names)}.");
    }

    public static string? Text(JsonElement item, params string[] names)
    {
        var value = Field(item, names);
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private async Task<JsonElement> GetAsync(string route)
    {
        var reply = await SendAsync(HttpMethod.Get, route);
        reply.RequireStatus(HttpStatusCode.OK);
        return reply.Json();
    }

    private async Task<ApiReply> SendAsync(HttpMethod method, string route, object? body = null)
    {
        using var request = new HttpRequestMessage(method, route);
        if (body is not null)
            request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        using var response = await _client.SendAsync(request);
        return new ApiReply(response.StatusCode, method.Method, route, await response.Content.ReadAsStringAsync());
    }

    private static bool IsAvailable(JsonElement slot)
    {
        var availability = Field(slot, "available", "enabled", "is_available", "ok");
        if (availability.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return availability.GetBoolean();
        return string.IsNullOrWhiteSpace(Text(slot, "reason", "disabled_reason"));
    }

    private static int IntegerValue(JsonElement item, params string[] names)
    {
        if (int.TryParse(Text(item, names), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            return value;
        throw new AssertionException($"Expected an integer API field named {string.Join(" or ", names)}.");
    }

    private static JsonElement[] Rows(JsonElement root, params string[] names)
    {
        var rows = root.ValueKind == JsonValueKind.Array ? root : Field(root, names);
        if (rows.ValueKind != JsonValueKind.Array)
            throw new AssertionException($"Expected an API array or the array field {string.Join(" or ", names)}.");
        return rows.EnumerateArray().ToArray();
    }

    private static JsonElement Unwrap(JsonElement root, params string[] names)
    {
        var value = Field(root, names);
        return value.ValueKind is JsonValueKind.Object or JsonValueKind.Array ? value : root;
    }

    private static JsonElement Field(JsonElement item, params string[] names)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var name in names)
            {
                if (item.TryGetProperty(name, out var value))
                    return value;
            }
        }
        return default;
    }

    public void Dispose() => _client.Dispose();
}
