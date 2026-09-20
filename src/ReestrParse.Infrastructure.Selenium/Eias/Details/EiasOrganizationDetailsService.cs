using System.Collections.Concurrent;
using OpenQA.Selenium;
using ReestrParse.Application.Details;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;
using ReestrParse.Infrastructure.Selenium.Browser;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

internal sealed class EiasOrganizationDetailsService : IEiasOrganizationDetailsService
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

        var queue = new ConcurrentQueue<(int Index, OrganizationReference Organization)>(
            organizations.Select((organization, index) => (index, organization)));

        var results = new OrganizationDetailsResult?[organizations.Count];
        var counters = new DetailsCounters();

        var workerCount = Math.Min(options.NormalizedParallelism, organizations.Count);
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

        await Task.WhenAll(workers);

        return results
            .Select((result, index) => result ?? new OrganizationDetailsResult(
                organizations[index],
                null,
                "Организация не была обработана."))
            .ToArray();
    }

    private static void RunWorker(
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

        try
        {
            while (queue.TryDequeue(out var item))
            {
                cancellationToken.ThrowIfCancellationRequested();

                OrganizationDetailsResult result;

                try
                {
                    browser ??= new SeleniumWorkerBrowser(options.Headless);

                    var detailUrl = ResolveDetailUrl(item.Organization);
                    var detailAlreadyOpen = false;

                    // Fast path: если catalog reader смог вытащить orgId/URL, открываем карточку напрямую.
                    // Fallback: отдельный worker открывает отфильтрованный каталог, переходит на
                    // SourcePage и кликает первую ячейку нужной организации, как старая рабочая версия.
                    if (string.IsNullOrWhiteSpace(detailUrl))
                    {
                        detailUrl = EiasOrganizationCatalogClickResolver.ResolveAndOpen(
                            browser,
                            item.Organization,
                            cancellationToken);
                        detailAlreadyOpen = true;
                    }

                    result = ProcessOrganization(
                        browser,
                        item.Organization,
                        detailUrl,
                        detailAlreadyOpen,
                        cancellationToken);
                }
                catch (WebDriverException ex)
                {
                    browser?.Dispose();
                    browser = null;
                    result = new OrganizationDetailsResult(
                        item.Organization,
                        null,
                        $"WebDriver worker #{workerId}: {ShortError(ex.Message)}");
                }
                catch (Exception ex)
                {
                    result = new OrganizationDetailsResult(
                        item.Organization,
                        null,
                        ShortError(ex.Message));
                }

                results[item.Index] = result;

                var currentCompleted = Interlocked.Increment(ref counters.Completed);
                var currentSucceeded = result.IsSuccess
                    ? Interlocked.Increment(ref counters.Succeeded)
                    : Volatile.Read(ref counters.Succeeded);
                var currentFailed = result.IsSuccess
                    ? Volatile.Read(ref counters.Failed)
                    : Interlocked.Increment(ref counters.Failed);

                progress?.Report(new DetailsProgress(
                    currentCompleted,
                    total,
                    currentSucceeded,
                    currentFailed,
                    item.Organization,
                    result.Details,
                    result.Error));
            }
        }
        finally
        {
            browser?.Dispose();
        }
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

    private static OrganizationDetailsResult ProcessOrganization(
        SeleniumWorkerBrowser browser,
        OrganizationReference organization,
        string detailUrl,
        bool detailAlreadyOpen,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!detailAlreadyOpen ||
            !string.Equals(browser.Driver.Url, detailUrl, StringComparison.OrdinalIgnoreCase))
        {
            browser.Driver.Navigate().GoToUrl(detailUrl);
            WaitForDocument(browser, cancellationToken);
        }

        var detailPageUri = new Uri(browser.Driver.Url);
        var detailHtml = browser.Driver.PageSource;
        var templateUrl = EiasOrganizationPageReader.ExtractForm411TemplateUrl(
            detailHtml,
            detailPageUri);

        cancellationToken.ThrowIfCancellationRequested();
        browser.Driver.Navigate().GoToUrl(templateUrl);
        WaitForDocument(browser, cancellationToken);

        browser.Wait.Until(d =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var source = d.PageSource;
            return source.Contains("Форма 4.1.1", StringComparison.OrdinalIgnoreCase) &&
                   source.Contains("Общая информация об организации", StringComparison.OrdinalIgnoreCase);
        });

        var enrichedSource = organization with
        {
            OrganizationId = !string.IsNullOrWhiteSpace(organization.OrganizationId)
                ? organization.OrganizationId
                : EiasOrganizationUrlBuilder.ExtractOrganizationId(detailUrl),
            DetailUrl = detailUrl
        };

        var details = EiasForm411Parser.Parse(
            browser.Driver.PageSource,
            enrichedSource,
            detailUrl,
            templateUrl);

        return new OrganizationDetailsResult(organization, details, null);
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
