using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReestrParse.Application.Catalog;
using ReestrParse.Application.Details;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Wpf.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IEiasCatalogService _catalog;
    private readonly IEiasOrganizationDetailsService _details;
    private CancellationTokenSource? _cts;

    public ObservableCollection<RegionOption> Regions { get; } = [];
    public ObservableCollection<SphereOption> Spheres { get; } = [SphereOption.HeatSupply];
    public ObservableCollection<OrganizationRowViewModel> Organizations { get; } = [];
    public ObservableCollection<int> ParallelismOptions { get; } = [1, 2, 3, 4, 5, 6];

    public ICollectionView FilteredOrganizations { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    private RegionOption? selectedRegion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    private SphereOption? selectedSphere = SphereOption.HeatSupply;

    [ObservableProperty]
    private int selectedParallelism = 3;

    [ObservableProperty]
    private bool headlessDetails = true;

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
    [NotifyCanExecuteChangedFor(nameof(LoadContactsCommand))]
    private bool isBusy;

    public MainWindowViewModel(
        IEiasCatalogService catalog,
        IEiasOrganizationDetailsService details)
    {
        _catalog = catalog;
        _details = details;

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
        if (item is not OrganizationRowViewModel org)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return org.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Inn.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Kpp.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Phones.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Email.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ResponsiblePerson.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.OrganizationId.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanLoadRegions))]
    private async Task LoadRegionsAsync()
    {
        ResetCancellation();

        try
        {
            IsBusy = true;
            StatusText = "Загрузка";
            CurrentStage = "Открытие ЕИАС";
            ProgressMessage = "Получаем список регионов с основной страницы...";

            var regions = await _catalog.LoadRegionsAsync(_cts!.Token);

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
            SetCanceled();
        }
        catch (Exception ex)
        {
            SetError(ex);
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

        ResetCancellation();

        try
        {
            IsBusy = true;
            StatusText = "Сбор";
            Organizations.Clear();
            LoadContactsCommand.NotifyCanExecuteChanged();

            var progress = new Progress<CatalogProgress>(p =>
            {
                CurrentStage = p.Stage;
                ProgressMessage = p.Message;
            });

            var organizations = await _catalog.LoadOrganizationsAsync(
                SelectedRegion,
                SelectedSphere,
                progress,
                _cts!.Token);

            foreach (var organization in organizations)
                Organizations.Add(new OrganizationRowViewModel(organization));

            FilteredOrganizations.Refresh();
            LoadContactsCommand.NotifyCanExecuteChanged();

            var directLinks = Organizations.Count(x => x.HasDetailUrl);
            StatusText = "Готов";
            CurrentStage = "Каталог собран";
            var clickFallback = Organizations.Count - directLinks;
            ProgressMessage =
                $"Найдено организаций: {Organizations.Count}. " +
                $"Прямые карточки: {directLinks}; через клик worker'а: {clickFallback}.";
        }
        catch (OperationCanceledException)
        {
            SetCanceled();
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
            LoadContactsCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanLoadOrganizations()
        => !IsBusy && SelectedRegion is not null && SelectedSphere is not null;

    [RelayCommand(CanExecute = nameof(CanLoadContacts))]
    private async Task LoadContactsAsync()
    {
        if (Organizations.Count == 0)
            return;

        ResetCancellation();

        try
        {
            IsBusy = true;
            StatusText = "Контакты";
            CurrentStage = "Форма 4.1.1";

            foreach (var row in Organizations)
                row.MarkQueued();

            var rowsByKey = Organizations.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
            var references = Organizations.Select(x => x.Reference).ToArray();

            var progress = new Progress<DetailsProgress>(p =>
            {
                var key = string.Join("|", p.Organization.Inn, p.Organization.Kpp, p.Organization.Name);
                if (rowsByKey.TryGetValue(key, out var row))
                    row.Apply(p);

                CurrentStage = $"Контакты {p.Completed}/{p.Total}";
                ProgressMessage =
                    $"{p.Message}. Успешно: {p.Succeeded}, ошибок: {p.Failed}.";

                FilteredOrganizations.Refresh();
            });

            var results = await _details.LoadDetailsAsync(
                references,
                new DetailsCrawlOptions(SelectedParallelism, HeadlessDetails),
                progress,
                _cts!.Token);

            var succeeded = results.Count(x => x.IsSuccess);
            var failed = results.Count - succeeded;

            StatusText = failed == 0 ? "Готов" : "Готово с ошибками";
            CurrentStage = "Контакты собраны";
            ProgressMessage =
                $"Форма 4.1.1 обработана для {succeeded}/{results.Count} организаций. " +
                $"Ошибок: {failed}. Workers: {Math.Clamp(SelectedParallelism, 1, 6)}.";
        }
        catch (OperationCanceledException)
        {
            SetCanceled();
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanLoadContacts() => !IsBusy && Organizations.Count > 0;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    private void ResetCancellation()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = new CancellationTokenSource();
    }

    private void SetCanceled()
    {
        StatusText = "Отменено";
        CurrentStage = "Операция остановлена";
        ProgressMessage = "Операция отменена пользователем.";
    }

    private void SetError(Exception ex)
    {
        StatusText = "Ошибка";
        CurrentStage = "Ошибка";
        ProgressMessage = ex.Message;
    }
}
