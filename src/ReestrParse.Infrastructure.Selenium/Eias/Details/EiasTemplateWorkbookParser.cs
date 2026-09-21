using AngleSharp.Html.Parser;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Разбирает TemplatePrinter двумя путями:
/// 1) старые workbook — обычные HTML-table;
/// 2) новые workbook — runtime Spread/Canvas (значений в PageSource нет).
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
                HasForm1: false,
                HasForm101: false,
                ["В HTML workbook не найдены листы 4.1.1 и 1.0.1; возможен Canvas/Spread renderer."]);
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
            details = CreateBaseDetails(
                source,
                detailUrl,
                templateUrl,
                ["Форма 4.1.1 в HTML workbook отсутствует; результат сформирован только по форме 1.0.1."]);
        }

        if (form101Table is not null)
            details = ApplyForm101(details, EiasForm101Parser.ParseTable(form101Table));

        var warnings = details.DataWarnings.ToList();
        if (form101Table is null)
            warnings.Add("Дополнительный лист 1.0.1 в HTML workbook отсутствует.");

        details = details with { Warnings = warnings };

        return new EiasWorkbookParseResult(
            details,
            details.HasForm411,
            details.HasForm1,
            details.HasForm101,
            details.DataWarnings);
    }

    public static EiasWorkbookParseResult ParseRuntime(
        IReadOnlyDictionary<string, List<string>> values,
        EiasTemplateCandidate candidate,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        if (values.Count == 0)
        {
            return new EiasWorkbookParseResult(
                null,
                false,
                false,
                false,
                ["Runtime Spread доступен, но параметры формы из workbook не извлечены."]);
        }

        // Явный номер опубликованной формы сильнее эвристики по кодам.
        // У современных форм 1 и 4.1.1 часть кодов пересекается, поэтому прежняя
        // логика могла увидеть в форме 1 коды 2.2/3.3/3.4 и ошибочно применить
        // схему 4.1.1. Эвристики используем только для кандидата без номера.
        bool is411;
        bool is1;
        bool is101;

        if (candidate.IsForm411)
        {
            is411 = true;
            is1 = false;
            is101 = false;
        }
        else if (candidate.IsForm1)
        {
            is411 = false;
            is1 = true;
            is101 = false;
        }
        else if (candidate.IsForm101)
        {
            is411 = false;
            is1 = false;
            is101 = true;
        }
        else
        {
            is411 = LooksLike411(values);
            is1 = !is411 && LooksLikeForm1(values);
            is101 = !is411 && !is1 && LooksLike101(values);
        }

        OrganizationContactDetails? details = null;

        if (is411)
        {
            details = EiasForm411Parser.ParseValues(values, source, detailUrl, templateUrl);
        }
        else if (is1)
        {
            details = EiasForm1Parser.ParseValues(values, source, detailUrl, templateUrl);
        }
        else if (is101)
        {
            details = ApplyForm101(
                CreateBaseDetails(
                    source,
                    detailUrl,
                    templateUrl,
                    ["Контактная форма 4.1.1/1 не найдена; данные получены только из 1.0.1."]),
                EiasForm101Parser.ParseValues(values));
        }

        if (details is null)
        {
            return new EiasWorkbookParseResult(
                null,
                false,
                false,
                false,
                ["Runtime Spread прочитан, но тип формы по candidate/кодам определить не удалось."]);
        }

        return new EiasWorkbookParseResult(
            details,
            details.HasForm411,
            details.HasForm1,
            details.HasForm101,
            details.DataWarnings);
    }

    private static OrganizationContactDetails ApplyForm101(
        OrganizationContactDetails details,
        EiasForm101Data form101)
        => details with
        {
            HasForm101 = true,
            DisclosureUpdatedAt = form101.DisclosureUpdatedAt,
            InfrastructureSystems = form101.InfrastructureSystems,
            RegulatedActivities = form101.RegulatedActivities,
            ServiceRegions = form101.ServiceRegions,
            MunicipalDistricts = form101.MunicipalDistricts,
            Municipalities = form101.Municipalities
        };

    private static OrganizationContactDetails CreateBaseDetails(
        OrganizationReference source,
        string detailUrl,
        string templateUrl,
        IReadOnlyList<string> warnings)
        => new(
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
            Warnings: warnings);

    private static bool LooksLike411(IReadOnlyDictionary<string, List<string>> values)
        => values.ContainsKey("2.2") &&
           (values.ContainsKey("7.1") || values.ContainsKey("3.3") || values.ContainsKey("3.4"));

    private static bool LooksLikeForm1(IReadOnlyDictionary<string, List<string>> values)
        => values.ContainsKey("1") &&
           (values.ContainsKey("9") || values.ContainsKey("10") || values.ContainsKey("11")) &&
           (values.ContainsKey("7") || values.ContainsKey("8"));

    private static bool LooksLike101(IReadOnlyDictionary<string, List<string>> values)
        => values.ContainsKey("2.1") && values.ContainsKey("3.1") &&
           (values.ContainsKey("4.1.1") || values.ContainsKey("4.1.1.1"));
}

internal sealed record EiasWorkbookParseResult(
    OrganizationContactDetails? Details,
    bool HasForm411,
    bool HasForm1,
    bool HasForm101,
    IReadOnlyList<string> Warnings);
