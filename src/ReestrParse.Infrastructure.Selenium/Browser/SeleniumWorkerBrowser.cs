using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Browser;

internal sealed class SeleniumWorkerBrowser : IDisposable
{
    private int _disposed;

    public SeleniumWorkerBrowser(bool headless)
    {
        Driver = SeleniumDriverFactory.Create(headless);
        Driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(60);
        Driver.Manage().Timeouts().AsynchronousJavaScript = TimeSpan.FromSeconds(30);

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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Quit закрывает Chrome + chromedriver. Dispose вызывается повторно безопасно:
        // и после WebDriver-ошибки, и в finally worker-а.
        try { Driver.Quit(); } catch { }
        try { Driver.Dispose(); } catch { }
    }
}
