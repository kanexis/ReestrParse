using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Browser;

internal sealed class SeleniumBrowserSession : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IWebDriver? _driver;

    private IWebDriver Driver => _driver ??= SeleniumDriverFactory.Create(headless: false);

    public async Task<T> RunAsync<T>(
        Func<IWebDriver, WebDriverWait, T> action,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);

        try
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var wait = new WebDriverWait(Driver, TimeSpan.FromSeconds(25))
                {
                    PollingInterval = TimeSpan.FromMilliseconds(250)
                };
                wait.IgnoreExceptionTypes(
                    typeof(NoSuchElementException),
                    typeof(StaleElementReferenceException),
                    typeof(NoSuchFrameException));

                return action(Driver, wait);
            }, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        try { _driver?.Quit(); } catch { }
        _driver?.Dispose();
        _gate.Dispose();
    }
}
