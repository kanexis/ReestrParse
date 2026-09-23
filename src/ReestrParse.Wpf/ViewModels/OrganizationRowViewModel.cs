using CommunityToolkit.Mvvm.ComponentModel;
using ReestrParse.Application.Details;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Wpf.ViewModels;

public partial class OrganizationRowViewModel : ObservableObject
{
    public OrganizationRowViewModel(OrganizationReference reference)
    {
        Reference = reference;
        OrganizationId = reference.OrganizationId;
    }

    public OrganizationReference Reference { get; }

    public string Key => string.Join("|", Reference.Inn, Reference.Kpp, Reference.Name);
    public string Name => Reference.Name;
    public string Inn => Reference.Inn;
    public string Kpp => Reference.Kpp;
    public string RegionName => Reference.RegionName;
    public string SphereName => Reference.SphereName;
    public int SourcePage => Reference.SourcePage;

    [ObservableProperty]
    private string organizationId = string.Empty;

    public bool HasDetailUrl =>
        !string.IsNullOrWhiteSpace(Reference.DetailUrl) ||
        (!string.IsNullOrWhiteSpace(Reference.OrganizationId) &&
         !string.IsNullOrWhiteSpace(Reference.RegionId) &&
         !string.IsNullOrWhiteSpace(Reference.SphereId) &&
         !string.IsNullOrWhiteSpace(Reference.FormValue));

    [ObservableProperty]
    private string phones = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreferredEmail))]
    [NotifyPropertyChangedFor(nameof(EmailSource))]
    private string email = string.Empty;

    [ObservableProperty]
    private string website = string.Empty;

    [ObservableProperty]
    private string responsiblePerson = string.Empty;

    [ObservableProperty]
    private string responsiblePosition = string.Empty;

    [ObservableProperty]
    private string responsiblePhone = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreferredEmail))]
    [NotifyPropertyChangedFor(nameof(EmailSource))]
    private string responsibleEmail = string.Empty;

    [ObservableProperty]
    private string manager = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PreferredEmail))]
    [NotifyPropertyChangedFor(nameof(EmailSource))]
    private string managerEmail = string.Empty;

    public string PreferredEmail => FirstNonEmpty(ManagerEmail, Email, ResponsibleEmail);

    public string EmailSource => !string.IsNullOrWhiteSpace(ManagerEmail)
        ? "Руководитель"
        : !string.IsNullOrWhiteSpace(Email)
            ? "Организация"
            : !string.IsNullOrWhiteSpace(ResponsibleEmail)
                ? "Ответственное лицо"
                : "Не найден";

    [ObservableProperty]
    private string postalAddress = string.Empty;

    [ObservableProperty]
    private string locationAddress = string.Empty;

    [ObservableProperty]
    private string parsedForms = "—";

    [ObservableProperty]
    private string disclosureUpdatedAt = string.Empty;

    [ObservableProperty]
    private string infrastructureSystems = string.Empty;

    [ObservableProperty]
    private string regulatedActivities = string.Empty;

    [ObservableProperty]
    private string serviceTerritory = string.Empty;

    [ObservableProperty]
    private string dataWarnings = string.Empty;

    [ObservableProperty]
    private string contactStatus = "Не загружены";

    [ObservableProperty]
    private string contactError = string.Empty;

    [ObservableProperty]
    private bool reportSucceeded;

    [ObservableProperty]
    private bool includeInReport = true;

    public void MarkQueued()
    {
        ContactStatus = HasDetailUrl ? "В очереди — прямой URL" : "В очереди — fallback";
        ContactError = string.Empty;
        DataWarnings = string.Empty;
        ReportSucceeded = false;
    }

    public void Apply(DetailsProgress progress)
    {
        if (progress.Details is not null)
        {
            var details = progress.Details;

            Phones = string.Join("; ", details.Phones);
            Email = details.Email;
            Website = details.Website;
            ResponsiblePerson = details.ResponsibleFullName;
            ResponsiblePosition = details.ResponsiblePosition;
            ResponsiblePhone = details.ResponsiblePhone;
            ResponsibleEmail = details.ResponsibleEmail;
            Manager = details.ManagerFullName;
            ManagerEmail = details.ManagerEmail;
            PostalAddress = details.PostalAddress;
            LocationAddress = details.LocationAddress;
            OrganizationId = details.OrganizationId;
            ParsedForms = details.ParsedForms;
            DisclosureUpdatedAt = details.DisclosureUpdatedAt;
            InfrastructureSystems = string.Join("; ", details.Systems);
            RegulatedActivities = string.Join("; ", details.Activities);
            ServiceTerritory = string.Join("; ",
                details.Regions
                    .Concat(details.Districts)
                    .Concat(details.MunicipalitiesList)
                    .Distinct(StringComparer.OrdinalIgnoreCase));
            DataWarnings = string.Join(" | ", details.DataWarnings);

            ContactStatus = details.IsPartial
                ? "Частично"
                : details.DataWarnings.Count > 0
                    ? "Готово · предупреждения"
                    : "Готово";

            ContactError = string.Empty;
            ReportSucceeded = true;
            return;
        }

        ContactStatus = "Ошибка";
        ContactError = progress.Error ?? "Не удалось получить данные организации.";
        ReportSucceeded = false;
    }


    private static string FirstNonEmpty(params string?[] values)
        => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
}
