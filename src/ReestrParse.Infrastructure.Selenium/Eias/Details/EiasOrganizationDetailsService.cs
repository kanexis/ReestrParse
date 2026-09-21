using System.Collections.Concurrent;
using System.Diagnostics;
using OpenQA.Selenium;
using OpenQA.Selenium.Support.UI;
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
            totalItems: organizations.Count,
            code: "DETAILS_QUEUE_STARTED");

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
                failed: Volatile.Read(ref counters.Failed),
                partial: Volatile.Read(ref counters.Partial),
                code: "DETAILS_CANCELLED");
            throw;
        }

        totalSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsCompleted,
            "Details crawl completed",
            $"Обработано {Volatile.Read(ref counters.Completed)}/{organizations.Count}; " +
            $"полностью {Volatile.Read(ref counters.Succeeded)}, " +
            $"частично {Volatile.Read(ref counters.Partial)}, " +
            $"ошибок {Volatile.Read(ref counters.Failed)}.",
            totalSw.Elapsed,
            itemIndex: Volatile.Read(ref counters.Completed),
            totalItems: organizations.Count,
            succeeded: Volatile.Read(ref counters.Succeeded),
            failed: Volatile.Read(ref counters.Failed),
            partial: Volatile.Read(ref counters.Partial),
            code: "DETAILS_COMPLETED");

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
            totalItems: total,
            code: "WORKER_STARTED");

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
                    inn: organization.Inn,
                    code: "ORGANIZATION_STARTED");

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
                            $"orgId/DetailUrl отсутствует. Worker #{workerId} ищет организацию в отдельной catalog-session.",
                            workerId: workerId,
                            page: organization.SourcePage,
                            itemIndex: itemPosition,
                            totalItems: total,
                            organizationName: organization.Name,
                            inn: organization.Inn,
                            code: "CATALOG_FALLBACK_STARTED",
                            dataSource: "catalog");

                        detailUrl = EiasOrganizationCatalogClickResolver.ResolveAndOpen(
                            browser,
                            organization,
                            telemetry,
                            workerId,
                            itemPosition,
                            total,
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
                        inn: organization.Inn,
                        code: usedFallback ? "CATALOG_FALLBACK_OK" : "DETAIL_URL_DIRECT",
                        dataSource: usedFallback ? "catalog click" : "catalog metadata");

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
                int currentSucceeded;
                int currentPartial;
                int currentFailed;

                if (result.IsFull)
                {
                    currentSucceeded = Interlocked.Increment(ref counters.Succeeded);
                    currentPartial = Volatile.Read(ref counters.Partial);
                    currentFailed = Volatile.Read(ref counters.Failed);
                }
                else if (result.IsPartial)
                {
                    currentSucceeded = Volatile.Read(ref counters.Succeeded);
                    currentPartial = Interlocked.Increment(ref counters.Partial);
                    currentFailed = Volatile.Read(ref counters.Failed);
                }
                else
                {
                    currentSucceeded = Volatile.Read(ref counters.Succeeded);
                    currentPartial = Volatile.Read(ref counters.Partial);
                    currentFailed = Interlocked.Increment(ref counters.Failed);
                }

                if (result.IsFull)
                {
                    telemetry.Success(
                        ParserPipelineStage.DetailsCompleted,
                        "Organization completed",
                        $"Данные получены полностью за {itemSw.Elapsed.TotalSeconds:F2} с. " +
                        $"Формы: {result.Details!.ParsedForms}.",
                        itemSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: currentCompleted,
                        totalItems: total,
                        succeeded: currentSucceeded,
                        failed: currentFailed,
                        partial: currentPartial,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "ORGANIZATION_FULL",
                        dataSource: result.Details.ParsedForms);
                }
                else if (result.IsPartial)
                {
                    telemetry.Warning(
                        ParserPipelineStage.DetailsCompleted,
                        "Organization completed",
                        $"Организация обработана частично за {itemSw.Elapsed.TotalSeconds:F2} с.: " +
                        "получена форма 1.0.1, но контактная форма 4.1.1/1 не найдена.",
                        itemSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: currentCompleted,
                        totalItems: total,
                        succeeded: currentSucceeded,
                        failed: currentFailed,
                        partial: currentPartial,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "ORGANIZATION_PARTIAL_FORM101",
                        dataSource: result.Details!.ParsedForms);
                }
                else
                {
                    telemetry.Error(
                        ParserPipelineStage.DetailsCompleted,
                        "Organization completed",
                        $"Организация завершена с ошибкой за {itemSw.Elapsed.TotalSeconds:F2} с.",
                        duration: itemSw.Elapsed,
                        workerId: workerId,
                        page: organization.SourcePage,
                        itemIndex: currentCompleted,
                        totalItems: total,
                        succeeded: currentSucceeded,
                        failed: currentFailed,
                        partial: currentPartial,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        error: result.Error,
                        code: ClassifyErrorCode(result.Error));
                }

                progress?.Report(new DetailsProgress(
                    currentCompleted,
                    total,
                    currentSucceeded,
                    currentFailed,
                    currentPartial,
                    organization,
                    result.Details,
                    result.Error,
                    workerId,
                    itemSw.Elapsed,
                    result.IsFull
                        ? "full"
                        : result.IsPartial
                            ? "partial"
                            : "failed"));
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
                failed: Volatile.Read(ref counters.Failed),
                partial: Volatile.Read(ref counters.Partial),
                code: "WORKER_STOPPED");
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

        WaitForPublishedForms(browser, cancellationToken);
        cardSw.Stop();

        telemetry.Success(
            ParserPipelineStage.DetailsNavigation,
            "Organization card loaded",
            "Карточка организации загружена; таблица опубликованных форм стабилизирована.",
            cardSw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn,
            code: "ORGANIZATION_CARD_READY",
            dataSource: "organization card");

        var detailPageUri = new Uri(browser.Driver.Url);
        var detailHtml = browser.Driver.PageSource;

        var discoverySw = Stopwatch.StartNew();
        var candidates = EiasOrganizationPageReader.ExtractTemplateCandidates(
            detailHtml,
            detailPageUri);
        discoverySw.Stop();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException(
                "В карточке организации не найдено ни одной опубликованной формы с TemplatePrinter URL.");
        }

        telemetry.Success(
            ParserPipelineStage.DetailsFormDiscovery,
            "Published forms discovered",
            $"Найдено опубликованных TemplatePrinter-ссылок: {candidates.Count}. " +
            $"4.1.1: {(candidates.Any(x => x.IsForm411) ? "да" : "нет")}; " +
            $"форма 1: {(candidates.Any(x => x.IsForm1) ? "да" : "нет")}; " +
            $"1.0.1: {(candidates.Any(x => x.IsForm101) ? "да" : "нет")}.",
            discoverySw.Elapsed,
            workerId,
            page: organization.SourcePage,
            itemIndex: itemIndex,
            totalItems: totalItems,
            organizationName: organization.Name,
            inn: organization.Inn,
            code: "PUBLISHED_FORMS_DISCOVERED",
            dataSource: "organization card");

        var enrichedSource = organization with
        {
            OrganizationId = !string.IsNullOrWhiteSpace(organization.OrganizationId)
                ? organization.OrganizationId
                : EiasOrganizationUrlBuilder.ExtractOrganizationId(detailUrl),
            DetailUrl = detailUrl
        };

        OrganizationContactDetails? bestDetails = null;
        var candidateErrors = new List<string>();
        var checkedCandidates = 0;

        // Обычно достаточно первого exact 4.1.1. Дополнительные кандидаты нужны как
        // fallback для карточек, где строка 4.1.1 отсутствует, но workbook другой формы
        // всё равно содержит лист 4.1.1/1.0.1.
        foreach (var candidate in candidates.Take(6))
        {
            cancellationToken.ThrowIfCancellationRequested();
            checkedCandidates++;

            var candidateSw = Stopwatch.StartNew();
            telemetry.Info(
                ParserPipelineStage.DetailsTemplate,
                "Template candidate started",
                $"Проверяем workbook: {candidate.Caption}.",
                workerId: workerId,
                page: organization.SourcePage,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                code: "TEMPLATE_CANDIDATE_STARTED",
                dataSource: candidate.Caption);

            try
            {
                browser.Driver.Navigate().GoToUrl(candidate.TemplateUrl);
                WaitForDocument(browser, cancellationToken);
                WaitForTemplateWorkbook(browser, cancellationToken);

                var workbookHtml = browser.Driver.PageSource;
                var parsed = EiasTemplateWorkbookParser.Parse(
                    workbookHtml,
                    enrichedSource,
                    detailUrl,
                    candidate.TemplateUrl);

                if (parsed.Details is null && EiasRuntimeSpreadsheetReader.IsSpreadReady(browser.Driver))
                {
                    var runtimeValues = WaitForRuntimeParameters(browser, cancellationToken);
                    parsed = EiasTemplateWorkbookParser.ParseRuntime(
                        runtimeValues,
                        candidate,
                        enrichedSource,
                        detailUrl,
                        candidate.TemplateUrl);

                    if (parsed.Details is not null)
                    {
                        telemetry.Success(
                            ParserPipelineStage.DetailsTemplate,
                            "Runtime Spread parsed",
                            $"Canvas/Spread workbook прочитан через JS-модель; параметров: {runtimeValues.Count}.",
                            workerId: workerId,
                            page: organization.SourcePage,
                            itemIndex: itemIndex,
                            totalItems: totalItems,
                            organizationName: organization.Name,
                            inn: organization.Inn,
                            code: candidate.IsForm1 ? "FORM_1_RUNTIME_SPREAD" : "RUNTIME_SPREAD_PARSED",
                            dataSource: candidate.Caption);
                    }
                }

                candidateSw.Stop();

                if (parsed.Details is null)
                {
                    var warning = $"{candidate.Caption}: workbook не содержит читаемой 4.1.1/1/1.0.1.";
                    candidateErrors.Add(warning);
                    telemetry.Warning(
                        ParserPipelineStage.DetailsTemplate,
                        "Template candidate skipped",
                        warning,
                        candidateSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: itemIndex,
                        totalItems: totalItems,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "TEMPLATE_NO_TARGET_SHEETS",
                        dataSource: candidate.Caption);
                    continue;
                }

                bestDetails = MergeDetails(bestDetails, parsed.Details);

                telemetry.Success(
                    ParserPipelineStage.DetailsTemplate,
                    "Template candidate parsed",
                    $"Workbook обработан: 4.1.1={(parsed.HasForm411 ? "да" : "нет")}, " +
                    $"1={(parsed.HasForm1 ? "да" : "нет")}, " +
                    $"1.0.1={(parsed.HasForm101 ? "да" : "нет")}; " +
                    $"предупреждений {parsed.Warnings.Count}.",
                    candidateSw.Elapsed,
                    workerId,
                    page: organization.SourcePage,
                    itemIndex: itemIndex,
                    totalItems: totalItems,
                    organizationName: organization.Name,
                    inn: organization.Inn,
                    code: parsed.HasForm411
                        ? "TEMPLATE_411_FOUND"
                        : parsed.HasForm1
                            ? "TEMPLATE_FORM1_FOUND"
                            : "TEMPLATE_101_ONLY",
                    dataSource: candidate.Caption);

                if (parsed.HasForm411)
                {
                    telemetry.Success(
                        ParserPipelineStage.DetailsForm411,
                        "4.1.1 parsed",
                        $"Контакты: телефонов организации {parsed.Details.Phones.Count}; " +
                        $"email {(string.IsNullOrWhiteSpace(parsed.Details.Email) ? "нет" : "есть")}.",
                        candidateSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: itemIndex,
                        totalItems: totalItems,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "FORM_411_PARSED",
                        dataSource: candidate.Caption);
                }

                if (parsed.HasForm1)
                {
                    telemetry.Success(
                        ParserPipelineStage.DetailsForm411,
                        "Form 1 parsed",
                        $"Форма 1: телефонов организации {parsed.Details.Phones.Count}; " +
                        $"email {(string.IsNullOrWhiteSpace(parsed.Details.Email) ? "нет" : "есть")}.",
                        candidateSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: itemIndex,
                        totalItems: totalItems,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "FORM_1_PARSED",
                        dataSource: candidate.Caption);
                }

                if (parsed.HasForm101)
                {
                    telemetry.Success(
                        ParserPipelineStage.DetailsForm101,
                        "1.0.1 parsed",
                        $"Доп. сведения: систем {parsed.Details.Systems.Count}, " +
                        $"видов деятельности {parsed.Details.Activities.Count}, " +
                        $"муниципалитетов {parsed.Details.MunicipalitiesList.Count}.",
                        candidateSw.Elapsed,
                        workerId,
                        page: organization.SourcePage,
                        itemIndex: itemIndex,
                        totalItems: totalItems,
                        organizationName: organization.Name,
                        inn: organization.Inn,
                        code: "FORM_101_PARSED",
                        dataSource: candidate.Caption);
                }

                if (bestDetails.HasPrimaryContactForm && bestDetails.HasForm101)
                    break;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                candidateSw.Stop();
                var error = $"{candidate.Caption}: {ShortError(ex.Message)}";
                candidateErrors.Add(error);

                telemetry.Warning(
                    ParserPipelineStage.DetailsTemplate,
                    "Template candidate failed",
                    "Не удалось обработать один из опубликованных workbook; пробуем следующий.",
                    candidateSw.Elapsed,
                    workerId,
                    page: organization.SourcePage,
                    itemIndex: itemIndex,
                    totalItems: totalItems,
                    organizationName: organization.Name,
                    inn: organization.Inn,
                    error: error,
                    code: "TEMPLATE_CANDIDATE_FAILED",
                    dataSource: candidate.Caption);
            }
        }

        if (bestDetails is null)
        {
            throw new InvalidOperationException(
                $"Не удалось получить данные из {checkedCandidates} опубликованных workbook. " +
                string.Join(" | ", candidateErrors.Take(3)));
        }

        if (bestDetails.DataWarnings.Count > 0)
        {
            telemetry.Warning(
                ParserPipelineStage.DetailsDataQuality,
                "Data quality warnings",
                string.Join(" | ", bestDetails.DataWarnings.Take(5)),
                workerId: workerId,
                page: organization.SourcePage,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                code: "DATA_QUALITY_WARNING",
                dataSource: bestDetails.ParsedForms);
        }

        if (!bestDetails.HasPrimaryContactForm && bestDetails.HasForm101)
        {
            telemetry.Warning(
                ParserPipelineStage.DetailsForm411,
                "4.1.1 unavailable",
                "Ни форма 4.1.1, ни современная форма 1 с контактами не найдены. " +
                "Организация сохранена как частичная по данным формы 1.0.1.",
                workerId: workerId,
                page: organization.SourcePage,
                itemIndex: itemIndex,
                totalItems: totalItems,
                organizationName: organization.Name,
                inn: organization.Inn,
                code: "FORM_411_UNAVAILABLE_FORM101_FALLBACK",
                dataSource: "1.0.1");
        }

        return new OrganizationDetailsResult(organization, bestDetails, null);
    }

    private static OrganizationContactDetails MergeDetails(
        OrganizationContactDetails? current,
        OrganizationContactDetails incoming)
    {
        if (current is null)
            return incoming;

        // Контактные поля предпочитаем из результата с основной контактной формой:
        // 4.1.1 имеет приоритет, затем современная форма 1.
        var incomingRank = incoming.HasForm411 ? 2 : incoming.HasForm1 ? 1 : 0;
        var currentRank = current.HasForm411 ? 2 : current.HasForm1 ? 1 : 0;
        var primary = incomingRank > currentRank ? incoming : current;
        var secondary = ReferenceEquals(primary, current) ? incoming : current;

        return primary with
        {
            OrganizationId = FirstNonEmpty(primary.OrganizationId, secondary.OrganizationId),
            Name = FirstNonEmpty(primary.Name, secondary.Name),
            Inn = FirstNonEmpty(primary.Inn, secondary.Inn),
            Kpp = FirstNonEmpty(primary.Kpp, secondary.Kpp),
            Phones = Union(primary.Phones, secondary.Phones),
            Email = FirstNonEmpty(primary.Email, secondary.Email),
            Website = FirstNonEmpty(primary.Website, secondary.Website),
            ResponsibleFullName = FirstNonEmpty(primary.ResponsibleFullName, secondary.ResponsibleFullName),
            ResponsiblePosition = FirstNonEmpty(primary.ResponsiblePosition, secondary.ResponsiblePosition),
            ResponsiblePhone = FirstNonEmpty(primary.ResponsiblePhone, secondary.ResponsiblePhone),
            ResponsibleEmail = FirstNonEmpty(primary.ResponsibleEmail, secondary.ResponsibleEmail),
            ManagerFullName = FirstNonEmpty(primary.ManagerFullName, secondary.ManagerFullName),
            PostalAddress = FirstNonEmpty(primary.PostalAddress, secondary.PostalAddress),
            LocationAddress = FirstNonEmpty(primary.LocationAddress, secondary.LocationAddress),
            HasForm411 = primary.HasForm411 || secondary.HasForm411,
            HasForm1 = primary.HasForm1 || secondary.HasForm1,
            HasForm101 = primary.HasForm101 || secondary.HasForm101,
            DisclosureUpdatedAt = FirstNonEmpty(primary.DisclosureUpdatedAt, secondary.DisclosureUpdatedAt),
            InfrastructureSystems = Union(primary.Systems, secondary.Systems),
            RegulatedActivities = Union(primary.Activities, secondary.Activities),
            ServiceRegions = Union(primary.Regions, secondary.Regions),
            MunicipalDistricts = Union(primary.Districts, secondary.Districts),
            Municipalities = Union(primary.MunicipalitiesList, secondary.MunicipalitiesList),
            Warnings = Union(primary.DataWarnings, secondary.DataWarnings)
        };
    }

    private static string FirstNonEmpty(string? first, string? second)
        => !string.IsNullOrWhiteSpace(first) ? first : second ?? string.Empty;

    private static IReadOnlyList<string> Union(
        IEnumerable<string> first,
        IEnumerable<string> second)
        => first.Concat(second)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

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

    private static void WaitForPublishedForms(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        var wait = new WebDriverWait(browser.Driver, TimeSpan.FromSeconds(15))
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
                    d.SwitchTo().DefaultContent();
                    var ready = string.Equals(
                        ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.ToString(),
                        "complete",
                        StringComparison.OrdinalIgnoreCase);

                    if (!ready)
                        return false;

                    return Convert.ToInt32(((IJavaScriptExecutor)d).ExecuteScript("""
                        return document.querySelectorAll(
                            "tr[id^='ASPxGridViewDet_DXDataRow'] a.get_template, " +
                            "tr[id^='ASPxGridViewDet_DXDataRow'] a[onclick*='openTemplateDialog']"
                        ).length;
                        """)) > 0;
                }
                catch (WebDriverException)
                {
                    return false;
                }
            });
        }
        catch (WebDriverTimeoutException)
        {
            // Карточка может легитимно не содержать опубликованных форм. Дальнейший
            // parser выдаст понятную доменную ошибку вместо Selenium timeout.
        }
    }

    private static IReadOnlyDictionary<string, List<string>> WaitForRuntimeParameters(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        var wait = new WebDriverWait(browser.Driver, TimeSpan.FromSeconds(8))
        {
            PollingInterval = TimeSpan.FromMilliseconds(500)
        };

        try
        {
            return wait.Until(d =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var values = EiasRuntimeSpreadsheetReader.TryReadParameters(d);
                return values.Count > 0 ? values : null;
            }) ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
        catch (WebDriverTimeoutException)
        {
            return new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void WaitForTemplateWorkbook(
        SeleniumWorkerBrowser browser,
        CancellationToken cancellationToken)
    {
        var wait = new WebDriverWait(browser.Driver, TimeSpan.FromSeconds(25))
        {
            PollingInterval = TimeSpan.FromMilliseconds(250)
        };

        wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                d.SwitchTo().DefaultContent();
                var source = d.PageSource;
                return source.Contains("id=\"sheets\"", StringComparison.OrdinalIgnoreCase) ||
                       source.Contains("id='sheets'", StringComparison.OrdinalIgnoreCase) ||
                       source.Contains("Форма 4.1.1", StringComparison.OrdinalIgnoreCase) ||
                       source.Contains("Форма 1.0.1", StringComparison.OrdinalIgnoreCase) ||
                       EiasRuntimeSpreadsheetReader.IsSpreadReady(d);
            }
            catch (WebDriverException)
            {
                return false;
            }
        });
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

    private static string ClassifyErrorCode(string? error)
    {
        if (string.IsNullOrWhiteSpace(error))
            return "DETAILS_UNKNOWN";

        if (error.Contains("Не найдена строка организации", StringComparison.OrdinalIgnoreCase))
            return "CATALOG_ROW_NOT_FOUND";

        if (error.Contains("опубликован", StringComparison.OrdinalIgnoreCase))
            return "NO_PUBLISHED_FORMS";

        if (error.Contains("TemplatePrinter", StringComparison.OrdinalIgnoreCase) ||
            error.Contains("workbook", StringComparison.OrdinalIgnoreCase))
            return "TEMPLATE_READ_FAILED";

        if (error.Contains("stale", StringComparison.OrdinalIgnoreCase))
            return "SELENIUM_STALE";

        return "DETAILS_FAILED";
    }

    private static string ShortError(string? message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "Неизвестная ошибка.";

        var firstLine = message.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        if (string.IsNullOrWhiteSpace(firstLine))
            return "Неизвестная ошибка.";

        return firstLine.Length <= 260 ? firstLine : firstLine[..260] + "…";
    }

    private sealed class DetailsCounters
    {
        public int Completed;
        public int Succeeded;
        public int Partial;
        public int Failed;
    }
}
