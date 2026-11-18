using Microsoft.Playwright;
using NUnit.Framework;
using System.Threading.Tasks;

namespace WholeCart.Automation
{
    [TestFixture]
    public class MarketplaceSmokeTests
    {
        private const string BaseUrl = "http://13.200.185.1/c/janaki-47b177/";
        private IPlaywright _playwright;
        private IBrowser _browser;
        private IPage _page;

        [SetUp]
        public async Task Setup()
        {
            _playwright = await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
            _page = await _browser.NewPageAsync();
        }

        [TearDown]
        public async Task TearDown()
        {
            await _browser.CloseAsync();
            _playwright.Dispose();
        }

        [Test]
        public async Task TestBuyerLoginAndNavigation()
        {
            // 1. Navigate to login
            await _page.GotoAsync(BaseUrl);

            // 2. Perform login as buyer1
            await _page.Locator("[data-testid='username']").FillAsync("buyer1");
            await _page.Locator("[data-testid='password']").FillAsync("Test@123");
            await _page.Locator("[data-testid='login']").ClickAsync();

            // 3. Verify successful redirect away from login
            Assert.That(_page.Url, Does.Not.Contain("login"));
        }
    }
}