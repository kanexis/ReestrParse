using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Browser;

internal sealed class SeleniumWorkerBrowser : IDisposable
{
    public SeleniumWorkerBrowser(bool headless)
    {
        Driver = SeleniumDriverFactory.Create(headless);
        Wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(35))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        Wait.IgnoreExceptionTypes(
            typeof(NoSuchElementException),
            typeof(StaleElementReferenceException),
            typeof(NoSuchFrameException));
    }

    public IWebDriver Driver { get; }
    public WebDriverWait Wait { get; }

    public void Dispose()
    {
        try { Driver.Quit(); } catch { }
        Driver.Dispose();
    }
}
