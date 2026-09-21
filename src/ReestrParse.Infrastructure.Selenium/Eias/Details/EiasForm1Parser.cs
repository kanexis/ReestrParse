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

        var phones = ValuesForCodeOrChildren(values, "9")
            .Select(EiasFormTableReader.Normalize)
            .Where(EiasFormTableReader.IsMeaningful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var emailRaw = EiasFormTableReader.First(values, "11");
        var email = ExtractEmail(emailRaw);
        if (EiasFormTableReader.IsMeaningful(emailRaw) && string.IsNullOrWhiteSpace(email))
            warnings.Add($"Код 11 формы 1 содержит значение, не похожее на email: «{emailRaw}».");

        if (phones.Length == 0)
            warnings.Add("В форме 1 не найден контактный телефон (код 9).");

        return new OrganizationContactDetails(
            OrganizationId: source.OrganizationId,
            Name: EiasFormTableReader.First(values, "1", source.Name),
            Inn: source.Inn,
            Kpp: source.Kpp,
            Phones: phones,
            Email: email,
            Website: EiasFormTableReader.First(values, "10"),
            ResponsibleFullName: string.Empty,
            ResponsiblePosition: string.Empty,
            ResponsiblePhone: string.Empty,
            ResponsibleEmail: string.Empty,
            ManagerFullName: JoinName(
                EiasFormTableReader.First(values, "6.1"),
                EiasFormTableReader.First(values, "6.2"),
                EiasFormTableReader.First(values, "6.3")),
            PostalAddress: EiasFormTableReader.First(values, "7"),
            LocationAddress: EiasFormTableReader.First(values, "8"),
            DetailUrl: detailUrl,
            TemplateUrl: templateUrl,
            HasForm1: true,
            Warnings: warnings);
    }

    private static IEnumerable<string> ValuesForCodeOrChildren(
        IReadOnlyDictionary<string, List<string>> values,
        string code)
        => values
            .Where(x => string.Equals(x.Key, code, StringComparison.OrdinalIgnoreCase) ||
                        x.Key.StartsWith(code + ".", StringComparison.OrdinalIgnoreCase))
            .SelectMany(x => x.Value);

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
