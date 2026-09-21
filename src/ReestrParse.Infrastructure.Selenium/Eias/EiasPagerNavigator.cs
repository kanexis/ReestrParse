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

        Exception? lastException = null;

        // DevExpress callback иногда «съедает» click: pager остаётся на page 1.
        // Вместо одного 60-секундного ожидания делаем три независимые попытки
        // разными способами клика.
        for (var attempt = 0; attempt < 3; attempt++)
        {
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

            try
            {
                ScrollPagerIntoView(driver, cancellationToken);
                ClickPageLink(driver, targetPage, attempt, cancellationToken);

                if (WaitForPage(driver, targetPage, cancellationToken, TimeSpan.FromSeconds(20)))
                    return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (InvalidOperationException)
            {
                throw;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(350);
        }

        var finalState = EiasLivePager.Read(driver);
        throw new WebDriverTimeoutException(
            $"Не удалось перейти на страницу {targetPage} DevExpress-таблицы после 3 попыток " +
            $"(Actions -> native Click -> JS/href). Текущий pager: {finalState.Summary}",
            lastException);
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
        int strategy,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 5; attempt++)
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

                switch (strategy)
                {
                    case 0:
                        new Actions(driver)
                            .ScrollToElement(pageLink)
                            .MoveToElement(pageLink)
                            .Pause(TimeSpan.FromMilliseconds(120))
                            .Click(pageLink)
                            .Perform();
                        break;

                    case 1:
                        pageLink.Click();
                        break;

                    default:
                        var href = pageLink.GetAttribute("href") ?? string.Empty;
                        if (href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase))
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript(
                                href["javascript:".Length..]);
                        }
                        else
                        {
                            ((IJavaScriptExecutor)driver).ExecuteScript(
                                "arguments[0].click();", pageLink);
                        }
                        break;
                }

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
            $"Не удалось кликнуть ссылку страницы {targetPage} (strategy={strategy}).",
            lastException);
    }

    private static bool WaitForPage(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        try
        {
            return wait.Until(d =>
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
                        let callback = false;
                        try {
                            if (window.ASPxGridView2 &&
                                typeof window.ASPxGridView2.InCallback === 'function') {
                                callback = !!window.ASPxGridView2.InCallback();
                            }
                        } catch (_) {}

                        return callback || visible(lp) || visible(ld);
                        """));

                    if (loading)
                        return false;

                    // Page-number берётся из актуального видимого pager. Вместе с
                    // завершённым callback и существующими строками этого достаточно.
                    // Сравнение первой строки с прошлой страницей давало false timeout.
                    return !string.IsNullOrWhiteSpace(ReadRowsSignature(d));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException)
        {
            return false;
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
