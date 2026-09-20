using AngleSharp.Dom;

namespace ReestrParse.Infrastructure.Selenium.Eias.Details;

/// <summary>
/// Форма 1.0.1 «Основные параметры раскрываемой информации».
/// Она не заменяет 4.1.1 для контактов, но даёт полезный контекст: систему,
/// вид регулируемой деятельности и территорию оказания услуги.
/// </summary>
internal static class EiasForm101Parser
{
    public static IElement? FindTable(IDocument document)
        => EiasFormTableReader.FindTable(
            document,
            "Форма 1.0.1",
            "Основные параметры раскрываемой информации");

    public static EiasForm101Data ParseTable(IElement table)
    {
        var values = EiasFormTableReader.ReadParameters(table);

        return new EiasForm101Data(
            DisclosureUpdatedAt: EiasFormTableReader.First(values, "1"),
            InfrastructureSystems: EiasFormTableReader.All(values, "2.1"),
            RegulatedActivities: EiasFormTableReader.All(values, "3.1"),
            ServiceRegions: EiasFormTableReader.All(values, "4.1.1"),
            MunicipalDistricts: EiasFormTableReader.All(values, "4.1.1.1"),
            Municipalities: EiasFormTableReader.All(values, "4.1.1.1.1"));
    }
}

internal sealed record EiasForm101Data(
    string DisclosureUpdatedAt,
    IReadOnlyList<string> InfrastructureSystems,
    IReadOnlyList<string> RegulatedActivities,
    IReadOnlyList<string> ServiceRegions,
    IReadOnlyList<string> MunicipalDistricts,
    IReadOnlyList<string> Municipalities);
