using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;

namespace ReestrParse.Infrastructure.Selenium.Browser;

internal static class SeleniumDriverFactory
{
    public static IWebDriver Create(bool headless)
    {
        var options = new ChromeOptions();
        options.AddArgument("--disable-notifications");
        options.AddArgument("--disable-popup-blocking");
        options.AddArgument("--disable-background-networking");
        options.AddArgument("--disable-renderer-backgrounding");
        options.AddArgument("--disable-background-timer-throttling");

        if (headless)
        {
            options.AddArgument("--headless=new");
            options.AddArgument("--window-size=1440,1000");
            options.AddUserProfilePreference("profile.managed_default_content_settings.images", 2);
        }
        else
        {
            options.AddArgument("--start-maximized");
        }

        return new ChromeDriver(options);
    }
}
