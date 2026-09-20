namespace ReestrParse.Domain.Organizations;

/// <summary>
/// Нормализованный результат чтения опубликованных форм организации.
/// Контакты берутся прежде всего из формы 4.1.1, а форма 1.0.1 используется
/// как дополнительный источник сведений о системе/виде деятельности/территории.
/// </summary>
public sealed record OrganizationContactDetails(
    string OrganizationId,
    string Name,
    string Inn,
    string Kpp,
    IReadOnlyList<string> Phones,
    string Email,
    string Website,
    string ResponsibleFullName,
    string ResponsiblePosition,
    string ResponsiblePhone,
    string ResponsibleEmail,
    string ManagerFullName,
    string PostalAddress,
    string LocationAddress,
    string DetailUrl,
    string TemplateUrl,
    bool HasForm411 = false,
    bool HasForm101 = false,
    string DisclosureUpdatedAt = "",
    IReadOnlyList<string>? InfrastructureSystems = null,
    IReadOnlyList<string>? RegulatedActivities = null,
    IReadOnlyList<string>? ServiceRegions = null,
    IReadOnlyList<string>? MunicipalDistricts = null,
    IReadOnlyList<string>? Municipalities = null,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> Systems => InfrastructureSystems ?? [];
    public IReadOnlyList<string> Activities => RegulatedActivities ?? [];
    public IReadOnlyList<string> Regions => ServiceRegions ?? [];
    public IReadOnlyList<string> Districts => MunicipalDistricts ?? [];
    public IReadOnlyList<string> MunicipalitiesList => Municipalities ?? [];
    public IReadOnlyList<string> DataWarnings => Warnings ?? [];

    public bool HasOrganizationContacts =>
        Phones.Count > 0 ||
        !string.IsNullOrWhiteSpace(Email) ||
        !string.IsNullOrWhiteSpace(Website);

    public bool IsPartial => !HasForm411 && HasForm101;

    public string ParsedForms => (HasForm411, HasForm101) switch
    {
        (true, true) => "4.1.1 + 1.0.1",
        (true, false) => "4.1.1",
        (false, true) => "1.0.1",
        _ => "—"
    };
}
