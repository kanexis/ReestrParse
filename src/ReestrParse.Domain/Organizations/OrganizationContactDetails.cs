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
    bool HasForm1 = false,
    bool HasForm101 = false,
    string DisclosureUpdatedAt = "",
    IReadOnlyList<string>? InfrastructureSystems = null,
    IReadOnlyList<string>? RegulatedActivities = null,
    IReadOnlyList<string>? ServiceRegions = null,
    IReadOnlyList<string>? MunicipalDistricts = null,
    IReadOnlyList<string>? Municipalities = null,
    IReadOnlyList<string>? Warnings = null,
    string ManagerEmail = "")
{
    public IReadOnlyList<string> Systems => InfrastructureSystems ?? [];
    public IReadOnlyList<string> Activities => RegulatedActivities ?? [];
    public IReadOnlyList<string> Regions => ServiceRegions ?? [];
    public IReadOnlyList<string> Districts => MunicipalDistricts ?? [];
    public IReadOnlyList<string> MunicipalitiesList => Municipalities ?? [];
    public IReadOnlyList<string> DataWarnings => Warnings ?? [];


    public string PreferredEmail =>
        FirstNonEmpty(ManagerEmail, Email, ResponsibleEmail);

    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;

    public bool HasOrganizationContacts =>
        Phones.Count > 0 ||
        !string.IsNullOrWhiteSpace(PreferredEmail) ||
        !string.IsNullOrWhiteSpace(Website);

    public bool HasPrimaryContactForm => HasForm411 || HasForm1;

    public bool IsPartial => !HasPrimaryContactForm && HasForm101;

    public string ParsedForms
    {
        get
        {
            var forms = new List<string>(3);
            if (HasForm411) forms.Add("4.1.1");
            if (HasForm1) forms.Add("1");
            if (HasForm101) forms.Add("1.0.1");
            return forms.Count == 0 ? "—" : string.Join(" + ", forms);
        }
    }
}
