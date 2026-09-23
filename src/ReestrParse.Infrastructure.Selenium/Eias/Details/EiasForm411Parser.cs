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
        return ParseValues(values, source, detailUrl, templateUrl);
    }

    public static OrganizationContactDetails ParseValues(
        IReadOnlyDictionary<string, List<string>> values,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        var warnings = new List<string>();

        var name = EiasFormTableReader.First(values, "2.1", source.Name);
        var inn = EiasFormTableReader.First(values, "2.2", source.Inn);
        var kpp = EiasFormTableReader.First(values, "2.3", source.Kpp);

        var phones = EiasFormTableReader.AllByCodeOrLabel(
                values,
                "7.1",
                "контактные телефоны регулируемой организации",
                "контактный телефон регулируемой организации")
            .Select(EiasFormTableReader.Normalize)
            .Where(LooksLikePhone)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (phones.Length == 0)
            warnings.Add("В форме 4.1.1 отсутствует код 7.1 с контактным телефоном организации.");

        var emailRaw = EiasFormTableReader.First(values, "9");
        var email = ExtractEmail(emailRaw);
        if (string.IsNullOrWhiteSpace(email))
        {
            emailRaw = EiasFormTableReader.FirstByLabel(
                values,
                "адрес электронной почты регулируемой организации",
                "адрес электронной почты организации");
            email = ExtractEmail(emailRaw);
        }
        if (EiasFormTableReader.IsMeaningful(emailRaw) && string.IsNullOrWhiteSpace(email))
            warnings.Add($"Код 9 содержит значение, не похожее на email: «{emailRaw}».");

        var responsibleEmailRaw = EiasFormTableReader.First(values, "3.4");
        var responsibleEmail = ExtractEmail(responsibleEmailRaw);
        if (string.IsNullOrWhiteSpace(responsibleEmail))
        {
            responsibleEmailRaw = EiasFormTableReader.FirstByLabel(
                values,
                "адрес электронной почты лица",
                "электронная почта ответственного");
            responsibleEmail = ExtractEmail(responsibleEmailRaw);
        }
        if (EiasFormTableReader.IsMeaningful(responsibleEmailRaw) && string.IsNullOrWhiteSpace(responsibleEmail))
            warnings.Add($"Код 3.4 содержит значение, не похожее на email: «{responsibleEmailRaw}».");

        // У разных редакций формы email руководителя может не иметь стабильного кода.
        // Поэтому берём его только по смысловой подписи и никогда не подменяем соседним полем.
        var managerEmail = ExtractEmail(EiasFormTableReader.FirstByLabel(
            values,
            "адрес электронной почты руководителя",
            "электронная почта руководителя",
            "e-mail руководителя"));

        return new OrganizationContactDetails(
            OrganizationId: source.OrganizationId,
            Name: name,
            Inn: inn,
            Kpp: kpp,
            Phones: phones,
            Email: email,
            Website: NormalizeWebsite(EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "8",
                "официальный сайт",
                "адрес сайта",
                "сайт")),
            ResponsibleFullName: JoinName(
                EiasFormTableReader.First(values, "3.1.1"),
                EiasFormTableReader.First(values, "3.1.2"),
                EiasFormTableReader.First(values, "3.1.3")),
            ResponsiblePosition: EiasFormTableReader.First(values, "3.2"),
            ResponsiblePhone: NormalizePhone(EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "3.3",
                "контактный телефон лица",
                "телефон ответственного")),
            ResponsibleEmail: responsibleEmail,
            ManagerFullName: JoinName(
                EiasFormTableReader.First(values, "4.1"),
                EiasFormTableReader.First(values, "4.2"),
                EiasFormTableReader.First(values, "4.3")),
            PostalAddress: EiasFormTableReader.FirstByCodeOrLabel(values, "5", "почтовый адрес"),
            LocationAddress: EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "6",
                "место нахождения",
                "местонахождение",
                "юридический адрес"),
            DetailUrl: detailUrl,
            TemplateUrl: templateUrl,
            HasForm411: true,
            Warnings: warnings,
            ManagerEmail: managerEmail);
    }

    private static bool LooksLikePhone(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return false;

        var digits = value!.Count(char.IsDigit);
        return digits >= 6 && digits <= 20;
    }

    private static string NormalizePhone(string? value)
        => LooksLikePhone(value)
            ? EiasFormTableReader.Normalize(value)
            : string.Empty;

    private static string NormalizeWebsite(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return string.Empty;

        var normalized = EiasFormTableReader.Normalize(value);
        if (normalized.Contains(' ') || !normalized.Contains('.'))
            return string.Empty;

        return normalized;
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
