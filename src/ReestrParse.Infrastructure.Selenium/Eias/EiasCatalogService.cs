using OpenQA.Selenium;
using ReestrParse.Application.Catalog;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal sealed class EiasCatalogService(SeleniumBrowserSession browser) : IEiasCatalogService
{
    public Task<IReadOnlyList<RegionOption>> LoadRegionsAsync(CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<RegionOption>>((driver, wait) =>
        {
            driver.Navigate().GoToUrl(EiasUrls.Map);
            EiasPageWaiter.WaitForDocumentReady(driver, wait);
            return EiasRegionSelector.ReadRegions(driver, wait);
        }, cancellationToken);

    public Task<IReadOnlyList<OrganizationReference>> LoadOrganizationsAsync(
        RegionOption region,
        SphereOption sphere,
        IProgress<CatalogProgress>? progress,
        CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<OrganizationReference>>((driver, wait) =>
        {
            driver.Navigate().GoToUrl(EiasUrls.Map);
            EiasPageWaiter.WaitForDocumentReady(driver, wait);

            progress?.Report(new("Выбор региона", 0, 0, region.Name));
            EiasRegionSelector.SelectRegion(driver, wait, region);

            // Не храним IWebElement кнопки. На динамическом ЕИАС действие выполняется
            // атомарно через свежий DOM, иначе старый элемент легко становится stale.
            var goClicked = wait.Until(d => EiasPageWaiter.ExecuteBool(d, """
                const button = document.getElementById('go-btn');
                if (!button) return false;
                button.click();
                return true;
                """));

            if (!goClicked)
                throw new NoSuchElementException("Кнопка выбора региона #go-btn не найдена.");

            EiasPageWaiter.WaitForDocumentReady(driver, wait);
            EiasPageWaiter.WaitForSearchPageReady(driver, wait);

            progress?.Report(new("Фильтры", 0, 0, "Выбираем «Теплоснабжение»..."));
            EiasFilterSelector.SelectHeatSupply(driver, wait);
            EiasSearchExecutor.WaitUntilReady(driver, wait);

            progress?.Report(new("Фильтры", 0, 0, "Выбираем «Общая информация об организации»..."));
            EiasFilterSelector.SelectGeneralOrganizationInfo(driver, wait);

            // НАЙТИ намеренно отдельный этап после полной готовности страницы.
            progress?.Report(new("Поиск", 0, 0, "Фильтры выбраны. Ждём готовность страницы..."));
            EiasSearchExecutor.WaitUntilReady(driver, wait);

            progress?.Report(new("Поиск", 0, 0, "Нажимаем обязательную кнопку «НАЙТИ»..."));
            EiasSearchExecutor.ClickSearch(driver, wait);

            progress?.Report(new("Поиск", 0, 0, "Ждём первую страницу таблицы организаций..."));
            EiasSearchExecutor.WaitForFirstPage(driver, cancellationToken);

            progress?.Report(new("Каталог", 1, 0, "Таблица получена. Читаем все страницы..."));
            return EiasOrganizationGridReader.ReadAllPages(
                driver,
                region,
                sphere,
                progress,
                cancellationToken);
        }, cancellationToken);
}
