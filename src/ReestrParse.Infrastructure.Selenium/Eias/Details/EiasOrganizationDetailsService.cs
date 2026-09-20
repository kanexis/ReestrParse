using System.Collections.Concurrent;
using System.Diagnostics;
using OpenQA.Selenium;
using ReestrParse.Application.Details;
using ReestrParse.Application.Monitoring;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

internal sealed class EiasOrganizationDetailsService(IParserTelemetry telemetry)
    : IEiasOrganizationDetailsService
{
    public async Task<IReadOnlyList<OrganizationDetailsResult>> LoadDetailsAsync(
        IReadOnlyList<OrganizationReference> organizations,
        DetailsCrawlOptions options,
        IProgress<DetailsProgress>? progress,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);
        ArgumentNullException.ThrowIfNull(options);

        if (organizations.Count == 0)
            return [];

        var totalSw = Stopwatch.StartNew();
        var workerCount = Math.Min(options.NormalizedParallelism, organizations.Count);

        telemetry.Info(
            ParserPipelineStage.DetailsQueue,
            "Details crawl started",
            $"В очередь поставлено {organizations.Count} организаций; workers: {workerCount}; headless: {options.Headless}.",
            totalItems: organizations.Count);

        var queue = new ConcurrentQueue<(int Index, OrganizationReference Organization)>(
            organizations.Select((organization, index) => (index, organization)));

        var results = new OrganizationDetailsResult?[organizations.Count];
        var counters = new DetailsCounters();

        var workers = Enumerable.Range(1, workerCount)
            .Select(workerId => Task.Run(() => RunWorker(
                workerId,
                queue,
                results,
                organizations.Count,
                options,
                progress,
                counters,
                cancellationToken), cancellationToken))
            .ToArray();

        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
            totalSw.Stop();
            telemetry.Warning(
                ParserPipelineStage.Cancelled,
                "Details crawl cancelled",
                $"Сбор контактов отменён: обработано {Volatile.Read(ref counters.Completed)}/{organizations.Count}.",
                totalSw.Elapsed,
                itemIndex: Volatile.Read(ref counters.Completed),
                totalItems: organizations.Count,
                succeeded: Volatile.Read(ref counters.Succeeded),
                failed: Volatile.Read(ref counters.Failed));
            throw;
        }

        totalSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsCompleted,
            "Details crawl completed",
            $"Контакты обработаны: {Volatile.Read(ref counters.Completed)}/{organizations.Count}; " +
            $"успешно {Volatile.Read(ref counters.Succeeded)}, ошибок {Volatile.Read(ref counters.Failed)}.",
            totalSw.Elapsed,
            itemIndex: Volatile.Read(ref counters.Completed),
            totalItems: organizations.Count,
            succeeded: Volatile.Read(ref counters.Succeeded),
            failed: Volatile.Read(ref counters.Failed));

        return results
            .Select((result, index) => result ?? new OrganizationDetailsResult(
                organizations[index],
                null,
                "Организация не была обработана."))
            .ToArray();
    }

    private void RunWorker(
        int workerId,
        ConcurrentQueue<(int Index, OrganizationReference Organization)> queue,
        OrganizationDetailsResult?[] results,
        int total,
        DetailsCrawlOptions options,
        IProgress<DetailsProgress>? progress,
        DetailsCounters counters,
        CancellationToken cancellationToken)
    {
        SeleniumWorkerBrowser? browser = null;
        var workerSw = Stopwatch.StartNew();

        telemetry.Info(
            ParserPipelineStage.DetailsQueue,
            "Worker started",
            $"Worker #{workerId} запущен.",
            workerId: workerId,
            totalItems: total);

        try
        {
            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();

                var organization = item.Organization;
                var itemPosition = item.Index + 1;
                var itemSw = Stopwatch.StartNew();
                OrganizationDetailsResult result;

                telemetry.Info(
                    ParserPipelineStage.DetailsNavigation,
                    "Organization started",
                    $"Worker #{workerId} начал организацию {itemPosition}/{total} со страницы каталога {organization.SourcePage}.",
                    workerId: workerId,
                    page: organization.SourcePage,
                    itemIndex: itemPosition,
                    totalItems: total,
                    organizationName: organization.Name,
                    inn: organization.Inn);

                try
                {
                    browser ??= new SeleniumWorkerBrowser(options.Headless);

                    var resolveSw = Stopwatch.StartNew();
                    var detailUrl = ResolveDetailUrl(organization);
                    var detailAlreadyOpen = false;
                    var usedFallback = string.IsNullOrWhiteSpace(detailUrl);

                    if (usedFallback)
                    {
                        telemetry.Info(
                            ParserPipelineStage.DetailsNavigation,
                            "Catalog click fallback started",
                            $"orgId/DetailUrl отсутствует. Worker #{workerId} открывает страницу каталога {organization.SourcePage} и кликает строку организации.",
                            workerId: workerId,
                            page: organization.SourcePage,
                            itemIndex: itemPosition,
                            totalItems: total,
                            organizationName: organization.Name,
                            inn: organization.Inn);

                        detailUrl = EiasOrganizationCatalogClickResolver.ResolveAndOpen(
                            browser,
                            organization,
                            cancellationToken);
                        detailAlreadyOpen = true;
                    }

                    resolveSw.Stop();
                    telemetry.Success(
                        ParserPipelineStage.DetailsNavigation,
                        usedFallback ? "Catalog click fallback completed" : "Direct detail URL resolved",
                        usedFallback
                            ? $"Карточка организации открыта через fallback: {detailUrl}"
                            : "Используется прямой URL карточки организации.",
                        resolveSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: itemPosition,
                        totalItems: total,
                        organizationName: organization.Name,
                        inn: organization.Inn);

                    result = ProcessOrganization(
                        browser,
                        organization,
                        detailUrl!,
                        detailAlreadyOpen,
                        workerId,
                        itemPosition,
                        total,
                        cancellationToken);
                }
                catch (WebDriverException ex)
                {
                    browser?.Dispose();
                    browser = null;
                    result = new OrganizationDetailsResult(
                        organization,
                        null,
                        $"WebDriver worker #{workerId}: {ShortError(ex.Message)}");
                }
                catch (Exception ex)
                {
                    result = new OrganizationDetailsResult(
                        organization,
                        null,
                        ShortError(ex.Message));
                }

                itemSw.Stop();
                results[item.Index] = result;

                var currentCompleted = Interlocked.Increment(ref counters.Completed);
                var currentSucceeded = result.IsSuccess
                    ? Interlocked.Increment(ref counters.Succeeded)
                    : Volatile.Read(ref counters.Succeeded);
                var currentFailed = result.IsSuccess
                    ? Volatile.Read(ref counters.Failed)
                    : Interlocked.Increment(ref counters.Failed);

                if (result.IsSuccess)
                {
                    telemetry.Success(
                        ParserPipelineStage.DetailsForm411,
                        "Organization completed",
                        $"Контакты получены за {itemSw.Elapsed.TotalSeconds:F2} с.",
                        itemSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: currentCompleted,
                        totalItems: total,
                        succeeded: currentSucceeded,
                        failed: currentFailed,
                        organizationName: organization.Name,
                        inn: organization.Inn);
                }
                else
                {
                    telemetry.Error(
                        ParserPipelineStage.DetailsForm411,
                        "Organization completed",
                        $"Организация завершена с ошибкой за {itemSw.Elapsed.TotalSeconds:F2} с.",
                        duration: itemSw.Elapsed,
                        workerId: workerId,
                        page: organization.SourcePage,
                        itemIndex: currentCompleted,
                        totalItems: total,
                        succeeded: currentSucceeded,
                        failed: currentFailed,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        error: result.Error);
                }

                progress?.Report(new DetailsProgress(
                    currentCompleted,
                    total,
                    currentSucceeded,
                    currentFailed,
                    organization,
                    result.Details,
                    result.Error,
                    workerId,
                    itemSw.Elapsed,
                    result.IsSuccess ? "4.1.1 parsed" : "failed"));
            }
        }
        finally
        {
            browser?.Dispose();
            workerSw.Stop();

            telemetry.Info(
                ParserPipelineStage.DetailsQueue,
                "Worker stopped",
                $"Worker #{workerId} остановлен. Время жизни: {workerSw.Elapsed:hh\\:mm\\:ss}.",
                workerSw.Elapsed,
                workerId: workerId,
                itemIndex: Volatile.Read(ref counters.Completed),
                totalItems: total,
                succeeded: Volatile.Read(ref counters.Succeeded),
                failed: Volatile.Read(ref counters.Failed));
        }
    }

    private OrganizationDetailsResult ProcessOrganization(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        string detailUrl,
        bool detailAlreadyOpen,
        int workerId,
        int itemIndex,
        int totalItems,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cardSw = Stopwatch.StartNew();
        if (!detailAlreadyOpen ||
            !string.Equals(browser.Driver.Url, detailUrl, StringComparison.OrdinalIgnoreCase))
        {
            browser.Driver.Navigate().GoToUrl(detailUrl);
            WaitForDocument(browser, cancellationToken);
        }
        cardSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsNavigation,
            "Organization card loaded",
            "Карточка организации загружена.",
            cardSw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn);

        var detailPageUri = new Uri(browser.Driver.Url);
        var detailHtml = browser.Driver.PageSource;

        var linkSw = Stopwatch.StartNew();
        var templateUrl = EiasOrganizationPageReader.ExtractForm411TemplateUrl(
            detailHtml,
            detailPageUri);
        linkSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsForm411,
            "4.1.1 link extracted",
            "Найдена строка формы 4.1.1 и извлечён прямой TemplatePrinter URL.",
            linkSw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn);

        cancellationToken.ThrowIfCancellationRequested();

        var templateSw = Stopwatch.StartNew();
        browser.Driver.Navigate().GoToUrl(templateUrl);
        WaitForDocument(browser, cancellationToken);

        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = d.PageSource;
            return source.Contains("Форма 4.1.1", StringComparison.OrdinalIgnoreCase) &&
                   source.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase);
        });
        templateSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsForm411,
            "TemplatePrinter loaded",
            "TemplatePrinter загружен, лист 4.1.1 присутствует в HTML.",
            templateSw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn);

        var enrichedSource = organization with
        {
            OrganizationId = !string.IsNullOrWhiteSpace(organization.OrganizationId)
                ? organization.OrganizationId
                : EiasOrganizationUrlBuilder.ExtractOrganizationId(detailUrl),
            DetailUrl = detailUrl
        };

        var parseSw = Stopwatch.StartNew();
        var details = EiasForm411Parser.Parse(
            browser.Driver.PageSource,
            enrichedSource,
            detailUrl,
            templateUrl);
        parseSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsForm411,
            "4.1.1 parsed",
            $"Форма распарсена: телефонов организации {details.Phones.Count}; email: " +
            $"{(string.IsNullOrWhiteSpace(details.Email) ? "нет" : "есть")}.",
            parseSw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn);

        return new OrganizationDetailsResult(organization, details, null);
    }

    private static string? ResolveDetailUrl(OrganizationReference organization)
    {
        if (!string.IsNullOrWhiteSpace(organization.DetailUrl))
            return organization.DetailUrl;

        if (string.IsNullOrWhiteSpace(organization.OrganizationId) ||
            string.IsNullOrWhiteSpace(organization.RegionId) ||
            string.IsNullOrWhiteSpace(organization.SphereId) ||
            string.IsNullOrWhiteSpace(organization.FormValue))
        {
            return null;
        }

        return EiasOrganizationUrlBuilder.Build(
            new RegionOption(organization.RegionId, organization.RegionName),
            new SphereOption(organization.SphereId, organization.SphereName),
            organization.FormValue,
            organization.OrganizationId);
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

    private static string ShortError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Неизвестная ошибка.";

        var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return "Неизвестная ошибка.";

        return firstLine.Length <= 220 ? firstLine : firstLine[..220] + "…";
    }

    private sealed class DetailsCounters
    {
        public int Completed;
        public int Succeeded;
        public int Failed;
    }
}
