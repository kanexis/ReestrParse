using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReestrParse.Application.Catalog;
using ReestrParse.Domain.Catalog;
using ReestrParse.Domain.Organizations;

namespace ReestrParse.Wpf.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IEiasCatalogService _catalog;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RegionOption> Regions { get; } = [];
    public ObservableCollection<SphereOption> Spheres { get; } = [SphereOption.HeatSupply];
    public ObservableCollection<OrganizationReference> Organizations { get; } = [];

    public ICollectionView FilteredOrganizations { get; }

    [ObservableProperty]
    private RegionOption? selectedRegion;

    [ObservableProperty]
    private SphereOption? selectedSphere = SphereOption.HeatSupply;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string statusText = "Готов";

    [ObservableProperty]
    private string currentStage = "Ожидание";

    [ObservableProperty]
    private string progressMessage = "Выберите регион и загрузите организации.";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadRegionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    private bool isBusy;

    public MainWindowViewModel(IEiasCatalogService catalog)
    {
        _catalog = catalog;

        FilteredOrganizations = CollectionViewSource.GetDefaultView(Organizations);
        FilteredOrganizations.Filter = FilterOrganization;
    }

    partial void OnSearchTextChanged(string value) => FilteredOrganizations.Refresh();

    public async Task InitializeAsync()
    {
        await LoadRegionsAsync();
    }

    private bool FilterOrganization(object item)
    {
        if (item is not OrganizationReference org)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return org.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ExternalId.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanLoadRegions))]
    private async Task LoadRegionsAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            StatusText = "Загрузка";
            CurrentStage = "Открытие ЕИАС";
            ProgressMessage = "Получаем список регионов с основной страницы...";

            var regions = await _catalog.LoadRegionsAsync(_cts.Token);

            Regions.Clear();
            foreach (var region in regions)
                Regions.Add(region);

            SelectedRegion ??= Regions.FirstOrDefault(x =>
                x.Name.Contains("Алтай", StringComparison.OrdinalIgnoreCase))
                ?? Regions.FirstOrDefault();

            StatusText = "Готов";
            CurrentStage = "Регионы загружены";
            ProgressMessage = $"Доступно регионов: {Regions.Count}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
            ProgressMessage = "Операция отменена.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка";
            ProgressMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadRegions() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanLoadOrganizations))]
    private async Task LoadOrganizationsAsync()
    {
        if (SelectedRegion is null || SelectedSphere is null)
            return;

        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            StatusText = "Сбор";
            Organizations.Clear();

            var progress = new Progress<CatalogProgress>(p =>
            {
                CurrentStage = p.Stage;
                ProgressMessage = p.Message;
            });

            var organizations = await _catalog.LoadOrganizationsAsync(
                SelectedRegion,
                SelectedSphere,
                progress,
                _cts.Token);

            foreach (var organization in organizations)
                Organizations.Add(organization);

            FilteredOrganizations.Refresh();

            StatusText = "Готов";
            CurrentStage = "Каталог собран";
            ProgressMessage = $"Найдено организаций: {Organizations.Count}.";
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
            ProgressMessage = "Операция отменена.";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка";
            ProgressMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadOrganizations()
        => !IsBusy && SelectedRegion is not null && SelectedSphere is not null;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();
}
