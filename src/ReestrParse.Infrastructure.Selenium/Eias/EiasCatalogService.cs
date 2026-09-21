using System.Diagnostics;
using OpenQA.Selenium;
using ReestrParse.Application.Catalog;
using ReestrParse.Application.Monitoring;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias;

internal sealed class EiasCatalogService(
    SeleniumBrowserSession browser,
    IParserTelemetry telemetry) : IEiasCatalogService
{
    public Task<IReadOnlyList<RegionOption>> LoadRegionsAsync(CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<RegionOption>>((driver, wait) =>
        {
            var sw = Stopwatch.StartNew();
            telemetry.Info(
                ParserPipelineStage.Regions,
                "Regions load started",
                "Открываем Map.aspx и загружаем список регионов.");

            try
            {
                driver.Navigate().GoToUrl(EiasUrls.Map);
                EiasPageWaiter.WaitForDocumentReady(driver, wait);
                var regions = EiasRegionSelector.ReadRegions(driver, wait);

                sw.Stop();
                telemetry.Success(
                    ParserPipelineStage.Regions,
                    "Regions loaded",
                    $"Загружено регионов: {regions.Count}.",
                    sw.Elapsed);

                return regions;
            }
            catch (Exception ex)
            {
                sw.Stop();
                telemetry.Error(
                    ParserPipelineStage.Failed,
                    "Regions load failed",
                    "Не удалось загрузить список регионов.",
                    ex,
                    sw.Elapsed);
                throw;
            }
        }, cancellationToken);

    public Task<IReadOnlyList<OrganizationReference>> LoadOrganizationsAsync(
        RegionOption region,
        SphereOption sphere,
        IProgress<CatalogProgress>? progress,
        CancellationToken cancellationToken)
        => browser.RunAsync<IReadOnlyList<OrganizationReference>>((driver, wait) =>
        {
            var totalSw = Stopwatch.StartNew();

            telemetry.Info(
                ParserPipelineStage.CatalogNavigation,
                "Catalog started",
                $"Запуск каталога для региона «{region.Name}», сфера «{sphere.Name}».");

            try
            {
                var sw = Stopwatch.StartNew();
                driver.Navigate().GoToUrl(EiasUrls.Map);
                EiasPageWaiter.WaitForDocumentReady(driver, wait);

                progress?.Report(new("Выбор региона", 0, 0, region.Name));
                EiasRegionSelector.SelectRegion(driver, wait, region);

                telemetry.Success(
                    ParserPipelineStage.CatalogNavigation,
                    "Region selected",
                    $"Выбран регион «{region.Name}».",
                    sw.Elapsed);

                sw.Restart();

                // Не храним IWebElement кнопки. Действие выполняется атомарно через свежий DOM.
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

                telemetry.Success(
                    ParserPipelineStage.CatalogNavigation,
                    "Search page loaded",
                    "Страница фильтров ЕИАС загружена.",
                    sw.Elapsed);

                sw.Restart();
                progress?.Report(new("Фильтры", 0, 0, "Выбираем «Теплоснабжение»..."));
                EiasFilterSelector.SelectHeatSupply(driver, wait);
                EiasSearchExecutor.WaitUntilReady(driver, wait);

                telemetry.Info(
                    ParserPipelineStage.CatalogFilters,
                    "Sphere selected",
                    "Выбран фильтр «Теплоснабжение».");

                progress?.Report(new("Фильтры", 0, 0, "Выбираем «Общая информация об организации»..."));
                EiasFilterSelector.SelectGeneralOrganizationInfo(driver, wait);

                telemetry.Success(
                    ParserPipelineStage.CatalogFilters,
                    "Filters selected",
                    "Выбраны «Теплоснабжение» и «Общая информация об организации».",
                    sw.Elapsed);

                sw.Restart();
                progress?.Report(new("Поиск", 0, 0, "Фильтры выбраны. Ждём готовность страницы..."));
                EiasSearchExecutor.WaitUntilReady(driver, wait);

                var beforeSearch = EiasSearchExecutor.CaptureState(driver);

                progress?.Report(new("Поиск", 0, 0, "Нажимаем обязательную кнопку «НАЙТИ»..."));
                EiasSearchExecutor.ClickSearch(driver, wait);

                progress?.Report(new("Поиск", 0, 0, "Ждём фактическое обновление и стабилизацию таблицы..."));
                var filteredState = EiasSearchExecutor.WaitForFirstPage(driver, beforeSearch, cancellationToken);

                telemetry.Success(
                    ParserPipelineStage.CatalogNavigation,
                    "Search completed",
                    $"Нажата «НАЙТИ»; отфильтрованный grid стабилизирован. " +
                    $"До поиска: {beforeSearch.Summary}; после: {filteredState.Summary}.",
                    sw.Elapsed);

                progress?.Report(new("Каталог", 1, 0, "Таблица получена. Читаем все страницы..."));

                var organizations = EiasOrganizationGridReader.ReadAllPages(
                    driver,
                    region,
                    sphere,
                    progress,
                    telemetry,
                    cancellationToken);

                totalSw.Stop();
                telemetry.Success(
                    ParserPipelineStage.CatalogCompleted,
                    "Catalog completed",
                    $"Каталог собран: {organizations.Count} организаций.",
                    totalSw.Elapsed,
                    itemIndex: organizations.Count,
                    totalItems: organizations.Count);

                return organizations;
            }
            catch (OperationCanceledException)
            {
                totalSw.Stop();
                telemetry.Warning(
                    ParserPipelineStage.Cancelled,
                    "Catalog cancelled",
                    "Сбор каталога отменён.",
                    totalSw.Elapsed);
                throw;
            }
            catch (Exception ex)
            {
                totalSw.Stop();
                telemetry.Error(
                    ParserPipelineStage.Failed,
                    "Catalog failed",
                    "Ошибка при сборе каталога организаций.",
                    ex,
                    totalSw.Elapsed);
                throw;
            }
        }, cancellationToken);
}
