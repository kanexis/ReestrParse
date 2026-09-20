using AngleSharp.Html.Parser;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Разбирает один HTML workbook TemplatePrinter сразу по нескольким листам.
/// 4.1.1 является основным источником контактов; 1.0.1 — дополнительным источником
/// сведений о регулируемой деятельности и территории.
/// </summary>
internal static class EiasTemplateWorkbookParser
{
    public static EiasWorkbookParseResult Parse(
        string html,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("TemplatePrinter вернул пустой HTML.");

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);

        var form411Table = EiasForm411Parser.FindTable(document);
        var form101Table = EiasForm101Parser.FindTable(document);

        if (form411Table is null && form101Table is null)
        {
            return new EiasWorkbookParseResult(
                null,
                HasForm411: false,
                HasForm101: false,
                ["В workbook не найдены листы 4.1.1 и 1.0.1."]);
        }

        OrganizationContactDetails details;

        if (form411Table is not null)
        {
            details = EiasForm411Parser.ParseTable(
                form411Table,
                source,
                detailUrl,
                templateUrl);
        }
        else
        {
            details = new OrganizationContactDetails(
                OrganizationId: source.OrganizationId,
                Name: source.Name,
                Inn: source.Inn,
                Kpp: source.Kpp,
                Phones: [],
                Email: string.Empty,
                Website: string.Empty,
                ResponsibleFullName: string.Empty,
                ResponsiblePosition: string.Empty,
                ResponsiblePhone: string.Empty,
                ResponsibleEmail: string.Empty,
                ManagerFullName: string.Empty,
                PostalAddress: string.Empty,
                LocationAddress: string.Empty,
                DetailUrl: detailUrl,
                TemplateUrl: templateUrl,
                HasForm411: false,
                Warnings: ["Форма 4.1.1 в workbook отсутствует; результат сформирован только по форме 1.0.1."]);
        }

        if (form101Table is not null)
        {
            var form101 = EiasForm101Parser.ParseTable(form101Table);
            details = details with
            {
                HasForm101 = true,
                DisclosureUpdatedAt = form101.DisclosureUpdatedAt,
                InfrastructureSystems = form101.InfrastructureSystems,
                RegulatedActivities = form101.RegulatedActivities,
                ServiceRegions = form101.ServiceRegions,
                MunicipalDistricts = form101.MunicipalDistricts,
                Municipalities = form101.Municipalities
            };
        }

        var warnings = details.DataWarnings.ToList();
        if (form101Table is null)
            warnings.Add("Дополнительный лист 1.0.1 в workbook отсутствует.");

        details = details with { Warnings = warnings };

        return new EiasWorkbookParseResult(
            details,
            details.HasForm411,
            details.HasForm101,
            details.DataWarnings);
    }
}

internal sealed record EiasWorkbookParseResult(
    OrganizationContactDetails? Details,
    bool HasForm411,
    bool HasForm101,
    IReadOnlyList<string> Warnings);
