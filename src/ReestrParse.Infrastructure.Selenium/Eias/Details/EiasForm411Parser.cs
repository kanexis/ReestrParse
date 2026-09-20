using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Парсер формы 4.1.1. Контакты читаются по стабильным кодам параметров,
/// а не по физическим номерам строк Excel/HTML.
/// </summary>
internal static partial class EiasForm411Parser
{
    public static OrganizationContactDetails Parse(
        string html,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        if (string.IsNullOrWhiteSpace(html))
            throw new InvalidOperationException("TemplatePrinter вернул пустой HTML.");

        var parser = new HtmlParser();
        var document = parser.ParseDocument(html);
        var formTable = FindTable(document)
            ?? throw new InvalidOperationException("В TemplatePrinter не найден лист «Форма 4.1.1».");

        return ParseTable(formTable, source, detailUrl, templateUrl);
    }

    public static IElement? FindTable(IDocument document)
        => EiasFormTableReader.FindTable(
            document,
            "Форма 4.1.1",
            "Общая информация об организации");

    public static OrganizationContactDetails ParseTable(
        IElement formTable,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        var values = EiasFormTableReader.ReadParameters(formTable);
        var warnings = new List<string>();

        var name = EiasFormTableReader.First(values, "2.1", source.Name);
        var inn = EiasFormTableReader.First(values, "2.2", source.Inn);
        var kpp = EiasFormTableReader.First(values, "2.3", source.Kpp);

        var phones = EiasFormTableReader.All(values, "7.1")
            .Select(EiasFormTableReader.Normalize)
            .Where(EiasFormTableReader.IsMeaningful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (phones.Length == 0)
            warnings.Add("В форме 4.1.1 отсутствует код 7.1 с контактным телефоном организации.");

        var emailRaw = EiasFormTableReader.First(values, "9");
        var email = ExtractEmail(emailRaw);
        if (EiasFormTableReader.IsMeaningful(emailRaw) && string.IsNullOrWhiteSpace(email))
            warnings.Add($"Код 9 содержит значение, не похожее на email: «{emailRaw}».");

        var responsibleEmailRaw = EiasFormTableReader.First(values, "3.4");
        var responsibleEmail = ExtractEmail(responsibleEmailRaw);
        if (EiasFormTableReader.IsMeaningful(responsibleEmailRaw) && string.IsNullOrWhiteSpace(responsibleEmail))
            warnings.Add($"Код 3.4 содержит значение, не похожее на email: «{responsibleEmailRaw}».");

        return new OrganizationContactDetails(
            OrganizationId: source.OrganizationId,
            Name: name,
            Inn: inn,
            Kpp: kpp,
            Phones: phones,
            Email: email,
            Website: EiasFormTableReader.First(values, "8"),
            ResponsibleFullName: JoinName(
                EiasFormTableReader.First(values, "3.1.1"),
                EiasFormTableReader.First(values, "3.1.2"),
                EiasFormTableReader.First(values, "3.1.3")),
            ResponsiblePosition: EiasFormTableReader.First(values, "3.2"),
            ResponsiblePhone: EiasFormTableReader.First(values, "3.3"),
            ResponsibleEmail: responsibleEmail,
            ManagerFullName: JoinName(
                EiasFormTableReader.First(values, "4.1"),
                EiasFormTableReader.First(values, "4.2"),
                EiasFormTableReader.First(values, "4.3")),
            PostalAddress: EiasFormTableReader.First(values, "5"),
            LocationAddress: EiasFormTableReader.First(values, "6"),
            DetailUrl: detailUrl,
            TemplateUrl: templateUrl,
            HasForm411: true,
            Warnings: warnings);
    }

    private static string JoinName(params string[] parts)
        => string.Join(" ", parts.Where(EiasFormTableReader.IsMeaningful));

    private static string ExtractEmail(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return string.Empty;

        var normalized = EiasFormTableReader.Normalize(value);
        var match = EmailRegex().Match(normalized);
        return match.Success ? match.Groups["email"].Value : string.Empty;
    }

    [GeneratedRegex(@"(?<email>[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
