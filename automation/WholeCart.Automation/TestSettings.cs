namespace WholeCart.Automation;

internal sealed record TestSettings(Uri BaseUrl, string Password)
{
    public static TestSettings FromEnvironment()
    {
        var endpoint = Environment.GetEnvironmentVariable("WHOLECART_BASE_URL");
        if (string.IsNullOrWhiteSpace(endpoint))
            endpoint = "http://13.200.185.1/c/janaki-47b177/";

        if (!Uri.TryCreate(endpoint.TrimEnd('/') + "/", UriKind.Absolute, out var baseUrl) ||
            baseUrl.Scheme is not ("http" or "https"))
        {
            throw new InvalidOperationException("WHOLECART_BASE_URL must be an HTTP(S) application URL.");
        }

        var password = Environment.GetEnvironmentVariable("WHOLECART_PASSWORD");
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Set WHOLECART_PASSWORD to the assignment test password.");

        return new TestSettings(baseUrl, password);
    }
}
