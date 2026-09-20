using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Навигация по актуальному видимому pager DevExpress ASPxGridView2.
/// Каждый шаг заново находит pager и ссылку нужной страницы; старые IWebElement
/// между callback-ами не сохраняются.
/// </summary>
internal static class EiasPagerNavigator
{
    private const string PagerId = "ASPxGridView2_DXPagerBottom";

    public static void GoToPage(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        if (targetPage < 1)
            throw new ArgumentOutOfRangeException(nameof(targetPage));

        cancellationToken.ThrowIfCancellationRequested();
        SafeDefaultContent(driver);

        var state = EiasLivePager.Read(driver);

        if (state.CurrentPage == targetPage)
            return;

        if (targetPage > state.TotalPages)
        {
            throw new InvalidOperationException(
                $"Запрошена страница {targetPage}, но актуальный видимый pager показывает только " +
                $"{state.TotalPages} страниц. {state.Summary}");
        }

        var previousSignature = ReadRowsSignature(driver);

        ScrollPagerIntoView(driver, cancellationToken);
        ClickPageLink(driver, targetPage, cancellationToken);
        WaitForPage(driver, targetPage, previousSignature, cancellationToken);
    }

    private static void ScrollPagerIntoView(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SafeDefaultContent(driver);

                var pager = FindVisiblePager(driver);

                ((IJavaScriptExecutor)driver).ExecuteScript("""
                    const pager = arguments[0];
                    if (!pager) return;

                    let node = pager.parentElement;
                    while (node && node !== document.body && node !== document.documentElement) {
                        const style = getComputedStyle(node);
                        const overflowY = style.overflowY;
                        const scrollable = (overflowY === 'auto' || overflowY === 'scroll') &&
                                           node.scrollHeight > node.clientHeight;

                        if (scrollable) {
                            const nodeRect = node.getBoundingClientRect();
                            const pagerRect = pager.getBoundingClientRect();
                            node.scrollTop += pagerRect.top - nodeRect.top -
                                              (node.clientHeight / 2) +
                                              (pagerRect.height / 2);
                        }

                        node = node.parentElement;
                    }

                    pager.scrollIntoView({ block: 'center', inline: 'nearest' });
                    """, pager);

                Thread.Sleep(150);

                // Selenium делает физическую прокрутку уже к свежему pager.
                pager = FindVisiblePager(driver);
                new Actions(driver)
                    .ScrollToElement(pager)
                    .MoveToElement(pager)
                    .Perform();

                var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(5))
                {
                    PollingInterval = TimeSpan.FromMilliseconds(100)
                };

                if (wait.Until(d =>
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        SafeDefaultContent(d);

                        try
                        {
                            var freshPager = FindVisiblePager(d);
                            return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                                const pager = arguments[0];
                                const r = pager.getBoundingClientRect();
                                return r.bottom > 0 &&
                                       r.top < window.innerHeight &&
                                       r.right > 0 &&
                                       r.left < window.innerWidth;
                                """, freshPager));
                        }
                        catch (WebDriverException)
                        {
                            return false;
                        }
                    }))
                {
                    return;
                }
            }
            catch (StaleElementReferenceException ex)
            {
                lastException = ex;
            }
            catch (NoSuchElementException ex)
            {
                lastException = ex;
            }
            catch (MoveTargetOutOfBoundsException ex)
            {
                lastException = ex;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(200);
        }

        throw new WebDriverException(
            "Не удалось прокрутить страницу до актуальной нижней панели пагинации " +
            "#ASPxGridView2_DXPagerBottom.",
            lastException);
    }

    private static void ClickPageLink(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 8; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SafeDefaultContent(driver);

                var state = EiasLivePager.Read(driver);
                if (targetPage > state.TotalPages)
                {
                    throw new InvalidOperationException(
                        $"Страница {targetPage} отсутствует в актуальном pager. " +
                        $"Сейчас страниц: {state.TotalPages}. {state.Summary}");
                }

                var pager = FindVisiblePager(driver);
                var pageLink = pager
                    .FindElements(By.CssSelector("a.dxp-num"))
                    .FirstOrDefault(x =>
                        string.Equals(x.Text.Trim(), targetPage.ToString(), StringComparison.Ordinal));

                if (pageLink is null)
                {
                    throw new NoSuchElementException(
                        $"В актуальном pager не найдена ссылка страницы {targetPage}.");
                }

                new Actions(driver)
                    .ScrollToElement(pageLink)
                    .MoveToElement(pageLink)
                    .Pause(TimeSpan.FromMilliseconds(120))
                    .Click(pageLink)
                    .Perform();

                return;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (StaleElementReferenceException ex)
            {
                lastException = ex;
            }
            catch (ElementClickInterceptedException ex)
            {
                lastException = ex;
            }
            catch (ElementNotInteractableException ex)
            {
                lastException = ex;
            }
            catch (MoveTargetOutOfBoundsException ex)
            {
                lastException = ex;
            }
            catch (NoSuchElementException ex)
            {
                lastException = ex;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            ScrollPagerIntoView(driver, cancellationToken);
            Thread.Sleep(200);
        }

        throw new WebDriverException(
            $"Не удалось кликнуть штатную ссылку страницы {targetPage} в актуальном pager.",
            lastException);
    }

    private static void WaitForPage(
        IWebDriver driver,
        int targetPage,
        string previousSignature,
        CancellationToken cancellationToken)
    {
        var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(60))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        try
        {
            wait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    SafeDefaultContent(d);

                    var state = EiasLivePager.Read(d);
                    if (state.CurrentPage != targetPage)
                        return false;

                    var loading = Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                        const visible = el => {
                            if (!el) return false;
                            const s = getComputedStyle(el);
                            return s.display !== 'none' &&
                                   s.visibility !== 'hidden' &&
                                   s.opacity !== '0' &&
                                   el.getClientRects().length > 0;
                        };

                        const lp = document.getElementById('ASPxGridView2_LP');
                        const ld = document.getElementById('ASPxGridView2_LD');
                        return visible(lp) || visible(ld);
                        """));

                    if (loading)
                        return false;

                    var signature = ReadRowsSignature(d);
                    return !string.IsNullOrWhiteSpace(signature) &&
                           (string.IsNullOrWhiteSpace(previousSignature) ||
                            !string.Equals(signature, previousSignature, StringComparison.Ordinal));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            var state = EiasLivePager.Read(driver);
            throw new WebDriverTimeoutException(
                $"Ссылка страницы {targetPage} была нажата, но актуальная DevExpress-таблица " +
                $"не перешла на неё за 60 секунд. Текущий pager: {state.Summary}", ex);
        }
    }

    private static IWebElement FindVisiblePager(IWebDriver driver)
    {
        var pagers = driver.FindElements(By.CssSelector($"[id='{PagerId}']"));

        // При callback DevExpress может оставить старый скрытый pager. Берём последний
        // реально отображаемый экземпляр, а не первый элемент с этим id.
        var pager = pagers.LastOrDefault(x =>
        {
            try
            {
                return x.Displayed;
            }
            catch (StaleElementReferenceException)
            {
                return false;
            }
        });

        return pager ?? throw new NoSuchElementException(
            "Актуальный отображаемый pager ASPxGridView2_DXPagerBottom не найден.");
    }

    private static string ReadRowsSignature(IWebDriver driver)
    {
        try
        {
            SafeDefaultContent(driver);

            return ((IJavaScriptExecutor)driver).ExecuteScript("""
                const rendered = el => {
                    if (!el) return false;
                    const s = getComputedStyle(el);
                    return s.display !== 'none' &&
                           s.visibility !== 'hidden' &&
                           s.opacity !== '0' &&
                           el.getClientRects().length > 0;
                };

                const tables = Array.from(document.querySelectorAll(
                    "[id='ASPxGridView2_DXMainTable']"
                ));

                const table = tables.find(rendered) || tables[tables.length - 1];
                if (!table) return '';

                const row = table.querySelector(
                    "tr[id^='ASPxGridView2_DXDataRow']"
                );
                if (!row) return '';

                return Array.from(row.querySelectorAll('td.dxgv'))
                    .slice(0, 3)
                    .map(x => (x.textContent || '').replace(/\s+/g, ' ').trim())
                    .join('|');
                """)?.ToString() ?? string.Empty;
        }
        catch (WebDriverException)
        {
            return string.Empty;
        }
    }

    private static void SafeDefaultContent(IWebDriver driver)
    {
        try
        {
            driver.SwitchTo().DefaultContent();
        }
        catch (NoSuchFrameException)
        {
        }
        catch (StaleElementReferenceException)
        {
        }
    }
}
