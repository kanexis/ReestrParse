using OpenQA.Selenium;
using OpenQA.Selenium.Interactions;
using OpenQA.Selenium.Support.UI;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Fallback для тех строк каталога, где DevExpress не отдаёт orgId/прямой DetailUrl.
/// Использует проверенный алгоритм старой версии ReestrParse: находит организацию по
/// Название + ИНН + КПП и кликает первую ячейку строки.
///
/// Важно: resolver работает внутри отдельного Selenium worker, а не в основной
/// browser-session каталога WPF. Поэтому переход в карточку не сбрасывает пагинацию
/// основного списка организаций.
/// </summary>
internal static class EiasOrganizationCatalogClickResolver
{
    private const string OrganizationPageMarker = "/Discl/PublicDisclosureInfoOrg.aspx";

    public static string ResolveAndOpen(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        CancellationToken cancellationToken)
    {
        var catalogUrl = EiasCatalogUrlBuilder.Build(organization)
            ?? throw new InvalidOperationException(
                "Недостаточно данных для fallback-перехода через каталог: отсутствуют region/sphere/form.");

        cancellationToken.ThrowIfCancellationRequested();
        browser.Driver.Navigate().GoToUrl(catalogUrl);
        WaitForCatalog(browser, cancellationToken);

        var targetPage = Math.Max(1, organization.SourcePage);
        if (targetPage > 1)
        {
            EiasPagerNavigator.GoToPage(
                browser.Driver,
                targetPage,
                cancellationToken);
        }

        WaitForCatalog(browser, cancellationToken);

        var beforeUrl = browser.Driver.Url;
        var clicked = ClickOrganizationCell(
            browser,
            organization,
            cancellationToken);

        if (!clicked)
        {
            throw new NoSuchElementException(
                $"На странице {targetPage} не найдена строка организации " +
                $"«{organization.Name}» (ИНН {organization.Inn}, КПП {organization.Kpp}).");
        }

        var navigationWait = new WebDriverWait(browser.Driver, TimeSpan.FromSeconds(45))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        try
        {
            navigationWait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    var url = d.Url ?? string.Empty;
                    return url.Contains(OrganizationPageMarker, StringComparison.OrdinalIgnoreCase) ||
                           (!string.IsNullOrWhiteSpace(url) &&
                            !string.Equals(url, beforeUrl, StringComparison.OrdinalIgnoreCase));
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException ex)
        {
            throw new WebDriverTimeoutException(
                $"Строка организации «{organization.Name}» была нажата на странице {targetPage}, " +
                "но браузер не перешёл в карточку организации.", ex);
        }

        WaitForDocument(browser, cancellationToken);

        var resolvedUrl = browser.Driver.Url ?? string.Empty;
        if (!resolvedUrl.Contains(OrganizationPageMarker, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"После клика по «{organization.Name}» открыт неожиданный URL: {resolvedUrl}");
        }

        return resolvedUrl;
    }

    private static void WaitForCatalog(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                d.SwitchTo().DefaultContent();

                var ready = string.Equals(
                    ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString(),
                    "complete",
                    StringComparison.OrdinalIgnoreCase);

                if (!ready)
                    return false;

                return Convert.ToBoolean(((IJavaScriptExecutor)d).ExecuteScript("""
                    const visible = el => {
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
                    const table = tables.find(visible) || tables[tables.length - 1];
                    if (!table) return false;

                    return table.querySelectorAll(
                        "tr[id^='ASPxGridView2_DXDataRow']"
                    ).length > 0;
                    """));
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
    }

    private static bool ClickOrganizationCell(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        for (var attempt = 1; attempt <= 6; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                browser.Driver.SwitchTo().DefaultContent();

                var rowId = ((IJavaScriptExecutor)browser.Driver).ExecuteScript("""
                    const expectedName = arguments[0];
                    const expectedInn = arguments[1];
                    const expectedKpp = arguments[2];

                    const normalize = value => (value || '')
                        .replace(/\u00a0/g, ' ')
                        .replace(/\s+/g, ' ')
                        .trim();

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

                    const rows = Array.from(table.querySelectorAll(
                        "tr[id^='ASPxGridView2_DXDataRow']"
                    ));

                    const exact = rows.find(row => {
                        const cells = Array.from(row.querySelectorAll(':scope > td.dxgv'));
                        if (cells.length < 3) return false;

                        return normalize(cells[0].textContent) === normalize(expectedName) &&
                               normalize(cells[1].textContent) === normalize(expectedInn) &&
                               normalize(cells[2].textContent) === normalize(expectedKpp);
                    });

                    if (exact) return exact.id || '';

                    // Fallback для редких случаев, когда сайт немного меняет отображаемое имя:
                    // ИНН + КПП достаточно уникальны внутри выбранного каталога.
                    const byTaxIds = rows.find(row => {
                        const cells = Array.from(row.querySelectorAll(':scope > td.dxgv'));
                        if (cells.length < 3) return false;

                        return normalize(cells[1].textContent) === normalize(expectedInn) &&
                               normalize(cells[2].textContent) === normalize(expectedKpp);
                    });

                    return byTaxIds?.id || '';
                    """,
                    organization.Name,
                    organization.Inn,
                    organization.Kpp)?.ToString();

                if (string.IsNullOrWhiteSpace(rowId))
                    return false;

                // Как и в старой рабочей WinForms-версии: кликаем именно первую ячейку.
                // Element заново получается прямо перед кликом и нигде не кэшируется.
                var row = browser.Driver.FindElement(By.Id(rowId));
                var cell = row.FindElements(By.CssSelector(":scope > td.dxgv")).FirstOrDefault()
                    ?? row.FindElements(By.TagName("td")).FirstOrDefault();

                if (cell is null)
                    return false;

                new Actions(browser.Driver)
                    .ScrollToElement(cell)
                    .MoveToElement(cell)
                    .Pause(TimeSpan.FromMilliseconds(100))
                    .Click(cell)
                    .Perform();

                return true;
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
            catch (WebDriverException ex)
            {
                lastException = ex;
            }

            Thread.Sleep(150);
            WaitForCatalog(browser, cancellationToken);
        }

        if (lastException is not null)
        {
            throw new WebDriverException(
                $"Не удалось кликнуть строку организации «{organization.Name}» в каталоге.",
                lastException);
        }

        return false;
    }

    private static void WaitForDocument(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                d.SwitchTo().DefaultContent();
                return string.Equals(
                    ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString(),
                    "complete",
                    StringComparison.OrdinalIgnoreCase);
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
    }
}
