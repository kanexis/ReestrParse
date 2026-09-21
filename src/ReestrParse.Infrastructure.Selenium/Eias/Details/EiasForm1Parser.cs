using System.Text.RegularExpressions;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Современная форма №1 «Общая информация об организации».
/// На текущем TemplatePrinter она отрисовывается Spread/Canvas, поэтому сюда
/// приходят уже извлечённые из runtime spreadsheet значения по кодам параметров.
/// </summary>
internal static partial class EiasForm1Parser
{
    public static OrganizationContactDetails ParseValues(
        IReadOnlyDictionary<string, List<string>> values,
        OrganizationReference source,
        string detailUrl,
        string templateUrl)
    {
        var warnings = new List<string>();

        var phones = EiasFormTableReader.AllByCodeOrLabel(
                values,
                "9",
                "контактные телефоны регулируемой организации",
                "контактный телефон регулируемой организации",
                "контактные телефоны")
            .Select(EiasFormTableReader.Normalize)
            .Where(LooksLikePhone)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var emailRaw = EiasFormTableReader.First(values, "11");
        var email = ExtractEmail(emailRaw);
        if (string.IsNullOrWhiteSpace(email))
        {
            emailRaw = EiasFormTableReader.FirstByLabel(
                values,
                "адрес электронной почты",
                "электронная почта",
                "e-mail");
            email = ExtractEmail(emailRaw);
        }
        if (EiasFormTableReader.IsMeaningful(emailRaw) && string.IsNullOrWhiteSpace(email))
            warnings.Add($"Код 11 формы 1 содержит значение, не похожее на email: «{emailRaw}».");

        if (phones.Length == 0)
            warnings.Add("В форме 1 не найден контактный телефон (код 9).");

        return new OrganizationContactDetails(
            OrganizationId: source.OrganizationId,
            Name: FirstOrFallback(
                EiasFormTableReader.FirstByCodeOrLabel(
                    values,
                    "1",
                    "наименование регулируемой организации",
                    "наименование организации"),
                source.Name),
            Inn: source.Inn,
            Kpp: source.Kpp,
            Phones: phones,
            Email: email,
            Website: NormalizeWebsite(EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "10",
                "официальный сайт",
                "адрес сайта",
                "сайт")),
            ResponsibleFullName: string.Empty,
            ResponsiblePosition: string.Empty,
            ResponsiblePhone: string.Empty,
            ResponsibleEmail: string.Empty,
            ManagerFullName: JoinName(
                EiasFormTableReader.FirstByCodeOrLabel(values, "6.1", "фамилия руководителя"),
                EiasFormTableReader.FirstByCodeOrLabel(values, "6.2", "имя руководителя"),
                EiasFormTableReader.FirstByCodeOrLabel(values, "6.3", "отчество руководителя")),
            PostalAddress: EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "7",
                "почтовый адрес"),
            LocationAddress: EiasFormTableReader.FirstByCodeOrLabel(
                values,
                "8",
                "место нахождения",
                "местонахождение",
                "юридический адрес"),
            DetailUrl: detailUrl,
            TemplateUrl: templateUrl,
            HasForm1: true,
            Warnings: warnings);
    }

    private static bool LooksLikePhone(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return false;

        var digits = value!.Count(char.IsDigit);
        return digits >= 6 && digits <= 20;
    }

    private static string NormalizeWebsite(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return string.Empty;

        var normalized = EiasFormTableReader.Normalize(value);
        if (normalized.Contains(' ') || !normalized.Contains('.'))
            return string.Empty;

        return normalized;
    }

    private static string FirstOrFallback(string? value, string fallback)
        => EiasFormTableReader.IsMeaningful(value)
            ? EiasFormTableReader.Normalize(value)
            : fallback;

    private static string JoinName(params string[] parts)
        => string.Join(" ", parts.Where(EiasFormTableReader.IsMeaningful));

    private static string ExtractEmail(string? value)
    {
        if (!EiasFormTableReader.IsMeaningful(value))
            return string.Empty;

        var match = EmailRegex().Match(EiasFormTableReader.Normalize(value));
        return match.Success ? match.Groups["email"].Value : string.Empty;
    }

    [GeneratedRegex(@"(?<email>[A-Z0-9._%+\-]+@[A-Z0-9.\-]+\.[A-Z]{2,})", RegexOptions.IgnoreCase)]
    private static partial Regex EmailRegex();
}
