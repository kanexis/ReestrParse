using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;

namespace ReestrParse.Infrastructure.Selenium.Eias;

/// <summary>
/// Навигация по актуальному DevExpress ASPxGridView2.
/// Основной путь использует реально видимые numeric-link pager и делает hop к ближайшей
/// странице в сторону цели. Client API GridView сохранён как fallback. Старые IWebElement
/// между callback-ами не сохраняются.
/// </summary>
internal static class EiasPagerNavigator
{
    private const string PagerId = "ASPxGridView2_DXPagerBottom";
    private static readonly TimeSpan DirectNavigationTimeout = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SequentialStepTimeout = TimeSpan.FromSeconds(7);

    public static void GoToPage(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        if (targetPage < 1)
            throw new ArgumentOutOfRangeException(nameof(targetPage));

        cancellationToken.ThrowIfCancellationRequested();
        SafeDefaultContent(driver);

        var initial = EiasLivePager.Read(driver);
        if (initial.CurrentPage == targetPage)
            return;

        if (targetPage > initial.TotalPages)
        {
            throw new InvalidOperationException(
                $"Запрошена страница {targetPage}, но актуальный pager показывает только " +
                $"{initial.TotalPages} страниц. {initial.Summary}");
        }

        Exception? lastException = null;

        // 1. Основной путь именно для того pager, который реально отдаёт ЕИАС:
        // кликаем не невидимую далёкую страницу, а ближайшую ВИДИМУЮ numeric-link
        // в сторону цели. После каждого DevExpress callback pager перечитывается.
        // Например, если с page 1 видны только 1..7, а нужна 8:
        // page 1 -> click 7 -> pager раскрыл 8 -> click 8.
        try
        {
            if (TryVisiblePagerHops(driver, targetPage, cancellationToken))
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

        // 2. Резерв: клиентский API DevExpress. Оставляем его на случай, если
        // numeric links временно не отрисованы/перестроены, но сам GridView жив.
        try
        {
            if (TryClientGotoPage(
                    driver,
                    targetPage,
                    DirectNavigationTimeout,
                    cancellationToken))
            {
                return;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (WebDriverException ex)
        {
            lastException = ex;
        }

        // 3. Последний fallback: последовательный client NextPage/PrevPage.
        try
        {
            if (TrySequentialClientNavigation(driver, targetPage, cancellationToken))
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

        var finalState = EiasLivePager.Read(driver);
        throw new WebDriverTimeoutException(
            $"Не удалось перейти на страницу {targetPage} DevExpress-таблицы. " +
            "Испробованы visible pager hops -> client GotoPage -> последовательная client-навигация. " +
            $"Текущий pager: {finalState.Summary}",
            lastException);
    }

    /// <summary>
    /// Специальный путь для основного catalog crawler. Он всегда читает страницы строго
    /// по порядку, поэтому не пытается "прыгать" к номеру страницы. Вместо этого вызывает
    /// штатный DevExpress pager callback PBN (Следующая), затем проверяет фактический pager.
    /// Это изолирует основной сбор каталога от сложной worker-навигации.
    /// </summary>
    public static void GoToNextCatalogPage(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        if (targetPage < 2)
            throw new ArgumentOutOfRangeException(nameof(targetPage));

        cancellationToken.ThrowIfCancellationRequested();
        SafeDefaultContent(driver);

        var initial = EiasLivePager.Read(driver);
        if (initial.CurrentPage == targetPage)
            return;

        if (targetPage > initial.TotalPages)
        {
            throw new InvalidOperationException(
                $"Запрошена страница {targetPage}, но актуальный pager показывает только " +
                $"{initial.TotalPages} страниц. {initial.Summary}");
        }

        if (targetPage != initial.CurrentPage + 1)
        {
            throw new InvalidOperationException(
                $"Последовательный catalog navigator может перейти только на следующую страницу. " +
                $"Текущая: {initial.CurrentPage}, запрошена: {targetPage}. {initial.Summary}");
        }

        Exception? lastException = null;
        var timeout = TimeSpan.FromSeconds(12);

        for (var attempt = 1; attempt <= 3; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(5));
            SafeDefaultContent(driver);

            var before = EiasLivePager.Read(driver);
            if (before.CurrentPage == targetPage)
                return;

            if (before.CurrentPage != targetPage - 1)
            {
                throw new InvalidOperationException(
                    $"Перед переходом на страницу {targetPage} основной catalog crawler оказался " +
                    $"на странице {before.CurrentPage}. {before.Summary}");
            }

            try
            {
                ScrollPagerIntoView(driver, cancellationToken);

                var invoked = attempt switch
                {
                    1 => InvokePagerNextCallback(driver),
                    2 => ClickVisibleNextButton(driver),
                    _ => InvokeClientNextPage(driver)
                };

                if (invoked && WaitForPage(driver, targetPage, cancellationToken, timeout))
                    return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(250);
        }

        var finalState = EiasLivePager.Read(driver);
        throw new WebDriverTimeoutException(
            $"Не удалось последовательно перейти на страницу {targetPage} основного каталога. " +
            "Испробованы штатный DevExpress PBN -> видимая кнопка «Следующая» -> client NextPage. " +
            $"Текущий pager: {finalState.Summary}",
            lastException);
    }

    internal static int? SelectVisibleHop(
        int currentPage,
        int targetPage,
        IEnumerable<int> visiblePages)
    {
        if (currentPage < 1)
            throw new ArgumentOutOfRangeException(nameof(currentPage));
        if (targetPage < 1)
            throw new ArgumentOutOfRangeException(nameof(targetPage));
        if (currentPage == targetPage)
            return currentPage;

        var pages = visiblePages
            .Where(x => x >= 1)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();

        if (pages.Contains(targetPage))
            return targetPage;

        if (targetPage > currentPage)
        {
            // Максимальная реально видимая страница, которая продвигает нас к цели,
            // но не перескакивает через неё. Для 1 -> 8 и visible 1..7 это 7.
            var forward = pages
                .Where(x => x > currentPage && x < targetPage)
                .DefaultIfEmpty(-1)
                .Max();
            return forward >= 1 ? forward : null;
        }

        // Обратное движение: берём минимальную видимую страницу между целью и
        // текущей. Например 12 -> 3 при visible 6..12 даст hop на 6.
        var backward = pages
            .Where(x => x < currentPage && x > targetPage)
            .DefaultIfEmpty(int.MaxValue)
            .Min();
        return backward != int.MaxValue ? backward : null;
    }

    internal static IReadOnlyList<int> BuildSequentialPages(int currentPage, int targetPage)
    {
        if (currentPage < 1)
            throw new ArgumentOutOfRangeException(nameof(currentPage));
        if (targetPage < 1)
            throw new ArgumentOutOfRangeException(nameof(targetPage));
        if (currentPage == targetPage)
            return Array.Empty<int>();

        var step = targetPage > currentPage ? 1 : -1;
        var pages = new List<int>(Math.Abs(targetPage - currentPage));
        for (var page = currentPage + step; ; page += step)
        {
            pages.Add(page);
            if (page == targetPage)
                return pages;
        }
    }

    private static bool TryVisiblePagerHops(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        // Не позволяем повреждённому pager зациклить Worker. В норме требуется
        // 1-3 hop, лимит TotalPages + 2 оставляет большой запас.
        var initial = EiasLivePager.Read(driver);
        var maxHops = Math.Max(3, initial.TotalPages + 2);
        var visitedStates = new HashSet<string>(StringComparer.Ordinal);

        for (var hop = 1; hop <= maxHops; hop++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(5));
            SafeDefaultContent(driver);

            var state = EiasLivePager.Read(driver);
            if (state.CurrentPage == targetPage)
                return true;
            if (targetPage > state.TotalPages)
            {
                throw new InvalidOperationException(
                    $"Страница {targetPage} отсутствует в актуальном pager. {state.Summary}");
            }

            ScrollPagerIntoView(driver, cancellationToken);
            var visiblePages = ReadVisibleNumericPages(driver);
            var nextPage = SelectVisibleHop(state.CurrentPage, targetPage, visiblePages);
            if (nextPage is null || nextPage.Value == state.CurrentPage)
                return false;

            var stateKey = $"{state.CurrentPage}->{nextPage.Value}|{string.Join(",", visiblePages)}";
            if (!visitedStates.Add(stateKey))
                return false;

            if (!ClickVisibleNumericPage(driver, nextPage.Value, cancellationToken))
                return false;
        }

        return false;
    }

    private static IReadOnlyList<int> ReadVisibleNumericPages(IWebDriver driver)
    {
        SafeDefaultContent(driver);
        var pager = FindVisiblePager(driver);
        var pages = new List<int>();

        foreach (var element in pager.FindElements(By.CssSelector("a.dxp-num, b.dxp-num")))
        {
            try
            {
                if (!element.Displayed)
                    continue;

                var text = element.Text
                    .Replace("[", string.Empty, StringComparison.Ordinal)
                    .Replace("]", string.Empty, StringComparison.Ordinal)
                    .Trim();

                if (int.TryParse(text, out var page) && page >= 1)
                    pages.Add(page);
            }
            catch (StaleElementReferenceException)
            {
                // Pager мог перестроиться между callback-ами; следующий hop перечитает DOM.
            }
        }

        return pages
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
    }

    private static bool ClickVisibleNumericPage(
        IWebDriver driver,
        int pageNumber,
        CancellationToken cancellationToken)
    {
        // Каждый strategy заново находит pager и ссылку. IWebElement через callback
        // намеренно не сохраняется.
        for (var strategy = 0; strategy < 3; strategy++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(3));
            SafeDefaultContent(driver);

            IWebElement? pageLink = null;
            try
            {
                var pager = FindVisiblePager(driver);
                pageLink = pager
                    .FindElements(By.CssSelector("a.dxp-num"))
                    .FirstOrDefault(x =>
                    {
                        try
                        {
                            return x.Displayed &&
                                   string.Equals(
                                       x.Text.Trim(),
                                       pageNumber.ToString(),
                                       StringComparison.Ordinal);
                        }
                        catch (StaleElementReferenceException)
                        {
                            return false;
                        }
                    });

                if (pageLink is null)
                    return false;

                switch (strategy)
                {
                    case 0:
                        new Actions(driver)
                            .ScrollToElement(pageLink)
                            .MoveToElement(pageLink)
                            .Pause(TimeSpan.FromMilliseconds(80))
                            .Click(pageLink)
                            .Perform();
                        break;
                    case 1:
                        pageLink.Click();
                        break;
                    default:
                        ((IJavaScriptExecutor)driver).ExecuteScript(
                            "arguments[0].click();",
                            pageLink);
                        break;
                }

                // Считаем click успешным только после фактического изменения pager.
                // Если Actions формально выполнился, но DevExpress callback не стартовал,
                // пробуем native/JS click на заново найденной ссылке.
                if (WaitForPage(driver, pageNumber, cancellationToken, SequentialStepTimeout))
                    return true;
            }
            catch (StaleElementReferenceException)
            {
            }
            catch (ElementClickInterceptedException)
            {
            }
            catch (ElementNotInteractableException)
            {
            }
            catch (MoveTargetOutOfBoundsException)
            {
            }

            Thread.Sleep(150);
        }

        return false;
    }

    private static bool InvokePagerNextCallback(IWebDriver driver)
    {
        try
        {
            return Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
                try {
                    if (!window.ASPx || typeof window.ASPx.GVPagerOnClick !== 'function')
                        return false;

                    if (window.ASPxGridView2 &&
                        typeof window.ASPxGridView2.InCallback === 'function' &&
                        window.ASPxGridView2.InCallback()) {
                        return false;
                    }

                    // Ровно тот callback, который прописан сайтом в onclick кнопки
                    // «Следующая»: ASPx.GVPagerOnClick('ASPxGridView2','PBN').
                    window.ASPx.GVPagerOnClick('ASPxGridView2', 'PBN');
                    return true;
                } catch (_) {
                    return false;
                }
                """));
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static bool ClickVisibleNextButton(IWebDriver driver)
    {
        try
        {
            SafeDefaultContent(driver);
            var pager = FindVisiblePager(driver);
            var nextButton = pager
                .FindElements(By.CssSelector("a.dxp-button"))
                .FirstOrDefault(x =>
                {
                    try
                    {
                        if (!x.Displayed)
                            return false;

                        return x.FindElements(By.CssSelector("img.dxWeb_pNext, img[alt='Следующая']")).Count > 0;
                    }
                    catch (StaleElementReferenceException)
                    {
                        return false;
                    }
                });

            if (nextButton is null)
                return false;

            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].click();", nextButton);
            return true;
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static bool InvokeClientNextPage(IWebDriver driver)
    {
        try
        {
            return Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
                const findGrid = () => {
                    if (window.ASPxGridView2) return window.ASPxGridView2;
                    try {
                        const collection = window.ASPxClientControl?.GetControlCollection?.();
                        return collection?.GetByName?.('ASPxGridView2') || null;
                    } catch (_) {
                        return null;
                    }
                };

                const grid = findGrid();
                if (!grid || typeof grid.NextPage !== 'function')
                    return false;
                if (typeof grid.InCallback === 'function' && grid.InCallback())
                    return false;

                grid.NextPage();
                return true;
                """));
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static bool TryClientGotoPage(
        IWebDriver driver,
        int targetPage,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(5));

        var invoked = InvokeClientGotoPage(driver, targetPage);
        if (!invoked)
            return false;

        return WaitForPage(driver, targetPage, cancellationToken, timeout);
    }

    private static bool TrySequentialClientNavigation(
        IWebDriver driver,
        int targetPage,
        CancellationToken cancellationToken)
    {
        WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(5));

        var state = EiasLivePager.Read(driver);
        if (state.CurrentPage == targetPage)
            return true;

        if (targetPage > state.TotalPages)
        {
            throw new InvalidOperationException(
                $"Страница {targetPage} отсутствует в актуальном pager. {state.Summary}");
        }

        foreach (var nextPage in BuildSequentialPages(state.CurrentPage, targetPage))
        {
            cancellationToken.ThrowIfCancellationRequested();
            WaitForCallbackIdle(driver, cancellationToken, TimeSpan.FromSeconds(5));

            if (!InvokeClientAdjacentPage(driver, nextPage))
                return false;

            if (!WaitForPage(driver, nextPage, cancellationToken, SequentialStepTimeout))
                return false;
        }

        return EiasLivePager.Read(driver).CurrentPage == targetPage;
    }

    private static bool InvokeClientGotoPage(IWebDriver driver, int targetPage)
    {
        try
        {
            return Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
                const pageIndex = Number(arguments[0]) - 1;
                const findGrid = () => {
                    if (window.ASPxGridView2) return window.ASPxGridView2;
                    try {
                        const collection = window.ASPxClientControl?.GetControlCollection?.();
                        return collection?.GetByName?.('ASPxGridView2') || null;
                    } catch (_) {
                        return null;
                    }
                };

                const grid = findGrid();
                if (!grid || typeof grid.GotoPage !== 'function')
                    return false;

                if (typeof grid.InCallback === 'function' && grid.InCallback())
                    return false;

                grid.GotoPage(pageIndex);
                return true;
                """, targetPage));
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static bool InvokeClientAdjacentPage(IWebDriver driver, int targetPage)
    {
        try
        {
            return Convert.ToBoolean(((IJavaScriptExecutor)driver).ExecuteScript("""
                const targetPage = Number(arguments[0]);
                const findGrid = () => {
                    if (window.ASPxGridView2) return window.ASPxGridView2;
                    try {
                        const collection = window.ASPxClientControl?.GetControlCollection?.();
                        return collection?.GetByName?.('ASPxGridView2') || null;
                    } catch (_) {
                        return null;
                    }
                };

                const grid = findGrid();
                if (!grid) return false;
                if (typeof grid.InCallback === 'function' && grid.InCallback())
                    return false;

                let currentPage = null;
                try {
                    if (typeof grid.GetPageIndex === 'function')
                        currentPage = Number(grid.GetPageIndex()) + 1;
                } catch (_) {}

                if (Number.isFinite(currentPage)) {
                    if (targetPage === currentPage + 1 && typeof grid.NextPage === 'function') {
                        grid.NextPage();
                        return true;
                    }
                    if (targetPage === currentPage - 1 && typeof grid.PrevPage === 'function') {
                        grid.PrevPage();
                        return true;
                    }
                }

                if (typeof grid.GotoPage === 'function') {
                    grid.GotoPage(targetPage - 1);
                    return true;
                }

                return false;
                """, targetPage));
        }
        catch (WebDriverException)
        {
            return false;
        }
    }

    private static void WaitForCallbackIdle(
        IWebDriver driver,
        CancellationToken cancellationToken,
        TimeSpan timeout)
    {
        var wait = new WebDriverWait(driver, timeout)
        {
            PollingInterval = TimeSpan.FromMilliseconds(150)
        };

        try
        {
            wait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                SafeDefaultContent(d);

                try
                {
                    return !Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                        const visible = el => {
                            if (!el) return false;
                            const s = getComputedStyle(el);
                            return s.display !== 'none' &&
                                   s.visibility !== 'hidden' &&
                                   s.opacity !== '0' &&
                                   el.getClientRects().length > 0;
                        };

                        let callback = false;
                        try {
                            if (window.ASPxGridView2 &&
                                typeof window.ASPxGridView2.InCallback === 'function') {
                                callback = !!window.ASPxGridView2.InCallback();
                            }
                        } catch (_) {}

                        return callback ||
                               visible(document.getElementById('ASPxGridView2_LP')) ||
                               visible(document.getElementById('ASPxGridView2_LD'));
                        """));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Вызывающий navigation path сам решит, удалось ли продолжить.
        }
    }

    private static void ScrollPagerIntoView(
        IWebDriver driver,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 5; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                SafeDefaultContent(driver);
                var pager = FindVisiblePager(driver);

                ((IJavaScriptExecutor)driver).ExecuteScript("""
                    const pager = arguments[0];
                    if (!pager) return;
                    pager.scrollIntoView({ block: 'center', inline: 'nearest' });
                    """, pager);

                Thread.Sleep(100);
                pager = FindVisiblePager(driver);
                new Actions(driver)
                    .ScrollToElement(pager)
                    .MoveToElement(pager)
                    .Perform();
                return;
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

            Thread.Sleep(150);
        }

        throw new WebDriverException(
            "Не удалось прокрутить страницу до актуальной нижней панели пагинации #ASPxGridView2_DXPagerBottom.",
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
            PollingInterval = TimeSpan.FromMilliseconds(200)
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

                const row = table.querySelector("tr[id^='ASPxGridView2_DXDataRow']");
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
