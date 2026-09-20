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
    private string email = string.Empty;

    [ObservableProperty]
    private string website = string.Empty;

    [ObservableProperty]
    private string responsiblePerson = string.Empty;

    [ObservableProperty]
    private string responsiblePhone = string.Empty;

    [ObservableProperty]
    private string responsibleEmail = string.Empty;

    [ObservableProperty]
    private string manager = string.Empty;

    [ObservableProperty]
    private string contactStatus = "Не загружены";

    [ObservableProperty]
    private string contactError = string.Empty;

    public void MarkQueued()
    {
        ContactStatus = HasDetailUrl ? "В очереди — прямой URL" : "В очереди — клик по каталогу";
        ContactError = string.Empty;
    }

    public void Apply(DetailsProgress progress)
    {
        if (progress.Details is not null)
        {
            Phones = string.Join("; ", progress.Details.Phones);
            Email = progress.Details.Email;
            Website = progress.Details.Website;
            ResponsiblePerson = progress.Details.ResponsibleFullName;
            ResponsiblePhone = progress.Details.ResponsiblePhone;
            ResponsibleEmail = progress.Details.ResponsibleEmail;
            Manager = progress.Details.ManagerFullName;
            OrganizationId = progress.Details.OrganizationId;
            ContactStatus = "Готово";
            ContactError = string.Empty;
            return;
        }

        ContactStatus = "Ошибка";
        ContactError = progress.Error ?? "Не удалось получить контакты.";
    }
}
