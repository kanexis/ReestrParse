using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReestrParse.Application.Catalog;
using ReestrParse.Application.Details;
using ReestrParse.Application.Monitoring;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Wpf.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IEiasCatalogService _catalog;
    private readonly IEiasOrganizationDetailsService _details;
    private readonly IParserTelemetry _telemetry;
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
    private double overallProgressPercent;

    [ObservableProperty]
    private string overallProgressText = "Ожидание";

    [ObservableProperty]
    private int currentPipelineStep = 1;

    [ObservableProperty]
    private string regionStepStatus = "Ожидание";

    [ObservableProperty]
    private string catalogStepStatus = "Ожидание";

    [ObservableProperty]
    private string contactsStepStatus = "Ожидание";

    [ObservableProperty]
    private string exportStepStatus = "Следующий этап";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadRegionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadContactsCommand))]
    private bool isBusy;

    public MainWindowViewModel(
        IEiasCatalogService catalog,
        IEiasOrganizationDetailsService details,
        IParserTelemetry telemetry)
    {
        _catalog = catalog;
        _details = details;
        _telemetry = telemetry;
        _telemetry.Published += OnTelemetryPublished;

        FilteredOrganizations = CollectionViewSource.GetDefaultView(Organizations);
        FilteredOrganizations.Filter = FilterOrganization;
    }

    partial void OnSearchTextChanged(string value) => FilteredOrganizations.Refresh();

    private void OnTelemetryPublished(ParserTelemetryEvent evt)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(() => ApplyTelemetryToPipeline(evt));
    }

    private void ApplyTelemetryToPipeline(ParserTelemetryEvent evt)
    {
        switch (evt.Stage)
        {
            case ParserPipelineStage.Regions:
                CurrentPipelineStep = 1;
                RegionStepStatus = evt.Message;
                CurrentStage = "Регионы";
                break;

            case ParserPipelineStage.CatalogNavigation:
            case ParserPipelineStage.CatalogFilters:
            case ParserPipelineStage.CatalogPages:
                CurrentPipelineStep = 2;
                CatalogStepStatus = evt.Message;
                CurrentStage = StageCaption(evt.Stage);
                break;

            case ParserPipelineStage.CatalogCompleted:
                CurrentPipelineStep = 2;
                CatalogStepStatus = evt.Message;
                ContactsStepStatus = Organizations.Count > 0 ? "Готов к запуску" : ContactsStepStatus;
                CurrentStage = "Каталог готов";
                break;

            case ParserPipelineStage.DetailsQueue:
            case ParserPipelineStage.DetailsNavigation:
            case ParserPipelineStage.DetailsFormDiscovery:
            case ParserPipelineStage.DetailsTemplate:
            case ParserPipelineStage.DetailsForm411:
            case ParserPipelineStage.DetailsForm101:
            case ParserPipelineStage.DetailsDataQuality:
                CurrentPipelineStep = 3;
                ContactsStepStatus = evt.Message;
                CurrentStage = StageCaption(evt.Stage);
                break;

            case ParserPipelineStage.DetailsCompleted:
                CurrentPipelineStep = 3;
                ContactsStepStatus = evt.Message;
                ExportStepStatus = "Подготовка результата";
                CurrentStage = "Данные форм готовы";
                break;

            case ParserPipelineStage.Completed:
                CurrentPipelineStep = 4;
                ContactsStepStatus = evt.Message;
                ExportStepStatus = "Результат готов";
                CurrentStage = "Pipeline завершён";
                break;

            case ParserPipelineStage.Cancelled:
                CurrentStage = "Отменено";
                break;

            case ParserPipelineStage.Failed:
                CurrentStage = "Ошибка";
                break;
        }

        if (evt.TotalItems is > 0 && evt.ItemIndex.HasValue &&
            evt.Stage is ParserPipelineStage.DetailsQueue or
                         ParserPipelineStage.DetailsNavigation or
                         ParserPipelineStage.DetailsFormDiscovery or
                         ParserPipelineStage.DetailsTemplate or
                         ParserPipelineStage.DetailsForm411 or
                         ParserPipelineStage.DetailsForm101 or
                         ParserPipelineStage.DetailsDataQuality or
                         ParserPipelineStage.DetailsCompleted or
                         ParserPipelineStage.Completed)
        {
            OverallProgressPercent = Math.Clamp(evt.ItemIndex.Value * 100d / evt.TotalItems.Value, 0d, 100d);
            OverallProgressText = $"Контакты: {evt.ItemIndex.Value}/{evt.TotalItems.Value}";
        }
        else if (evt.TotalPages is > 0 && evt.Page.HasValue && evt.Stage == ParserPipelineStage.CatalogPages)
        {
            OverallProgressPercent = Math.Clamp(evt.Page.Value * 100d / evt.TotalPages.Value, 0d, 100d);
            OverallProgressText = $"Каталог: страница {evt.Page.Value}/{evt.TotalPages.Value}";
        }

        if (!string.IsNullOrWhiteSpace(evt.Message) && evt.Stage is not ParserPipelineStage.Regions)
            ProgressMessage = evt.Message;
    }

    private static string StageCaption(ParserPipelineStage stage) => stage switch
    {
        ParserPipelineStage.CatalogNavigation => "Навигация каталога",
        ParserPipelineStage.CatalogFilters => "Фильтры каталога",
        ParserPipelineStage.CatalogPages => "Чтение страниц каталога",
        ParserPipelineStage.DetailsQueue => "Очередь workers",
        ParserPipelineStage.DetailsNavigation => "Карточка организации",
        ParserPipelineStage.DetailsFormDiscovery => "Поиск опубликованных форм",
        ParserPipelineStage.DetailsTemplate => "TemplatePrinter",
        ParserPipelineStage.DetailsForm411 => "Форма 4.1.1",
        ParserPipelineStage.DetailsForm101 => "Форма 1.0.1",
        ParserPipelineStage.DetailsDataQuality => "Проверка данных",
        _ => stage.ToString()
    };

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
               org.ResponsibleEmail.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Manager.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ParsedForms.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.InfrastructureSystems.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.RegulatedActivities.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ServiceTerritory.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.OrganizationId.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanLoadRegions))]
    private async Task LoadRegionsAsync()
    {
        ResetCancellation();
        var sw = Stopwatch.StartNew();

        try
        {
            IsBusy = true;
            CurrentPipelineStep = 1;
            RegionStepStatus = "Загрузка регионов...";
            CatalogStepStatus = "Ожидание";
            ContactsStepStatus = "Ожидание";
            ExportStepStatus = "Ожидание данных";
            OverallProgressPercent = 0;
            OverallProgressText = "Загрузка регионов";
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

            sw.Stop();
            StatusText = "Готов";
            CurrentStage = "Регионы загружены";
            ProgressMessage = $"Доступно регионов: {Regions.Count}.";
            RegionStepStatus = $"Готово · {Regions.Count} регионов";
            OverallProgressPercent = 100;
            OverallProgressText = $"Регионы загружены за {sw.Elapsed.TotalSeconds:F1} с";
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
        var sw = Stopwatch.StartNew();

        try
        {
            IsBusy = true;
            CurrentPipelineStep = 2;
            RegionStepStatus = $"Готово · {SelectedRegion.Name}";
            CatalogStepStatus = "Подготовка...";
            ContactsStepStatus = "Ожидание каталога";
            ExportStepStatus = "Ожидание данных";
            OverallProgressPercent = 0;
            OverallProgressText = "Подготовка каталога";
            StatusText = "Сбор";
            Organizations.Clear();
            LoadContactsCommand.NotifyCanExecuteChanged();

            var progress = new Progress<CatalogProgress>(p =>
            {
                CurrentStage = p.Stage;
                ProgressMessage = p.Message;

                CatalogStepStatus = p.Page > 0
                    ? p.TotalPages > 0
                        ? $"Страница {p.Page}/{p.TotalPages} · {p.OrganizationsFound}"
                        : $"Страница {p.Page} · {p.OrganizationsFound}"
                    : p.Stage;

                if (p.TotalPages > 0 && p.Page > 0)
                {
                    OverallProgressPercent = Math.Clamp(p.Page * 100d / p.TotalPages, 0d, 100d);
                    OverallProgressText = $"Каталог: страница {p.Page}/{p.TotalPages}";
                }
                else
                {
                    OverallProgressText = p.Message;
                }
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

            sw.Stop();
            var directLinks = Organizations.Count(x => x.HasDetailUrl);
            var clickFallback = Organizations.Count - directLinks;

            StatusText = "Готов";
            CurrentStage = "Каталог собран";
            CatalogStepStatus = $"Готово · {Organizations.Count} организаций";
            ContactsStepStatus = "Готов к запуску";
            OverallProgressPercent = 100;
            OverallProgressText = $"Каталог собран за {sw.Elapsed.TotalSeconds:F1} с";
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
        var totalSw = Stopwatch.StartNew();

        try
        {
            IsBusy = true;
            CurrentPipelineStep = 3;
            CatalogStepStatus = $"Готово · {Organizations.Count} организаций";
            ContactsStepStatus = $"Запуск {Math.Clamp(SelectedParallelism, 1, 6)} workers...";
            OverallProgressPercent = 0;
            OverallProgressText = $"Контакты: 0/{Organizations.Count}";
            StatusText = "Контакты";
            CurrentStage = "Карточки и формы";

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
                ContactsStepStatus = $"Обработано {p.Completed}/{p.Total} · OK {p.Succeeded} · PART {p.Partial} · ERR {p.Failed}";
                OverallProgressPercent = p.Total > 0
                    ? Math.Clamp(p.Completed * 100d / p.Total, 0d, 100d)
                    : 0;
                OverallProgressText =
                    $"Данные: {p.Completed}/{p.Total} · worker {p.WorkerId}" +
                    (p.Duration.HasValue ? $" · {p.Duration.Value.TotalSeconds:F1} с" : string.Empty);

                ProgressMessage =
                    $"{p.Message}. Полностью: {p.Succeeded}, частично: {p.Partial}, ошибок: {p.Failed}.";

                FilteredOrganizations.Refresh();
            });

            var results = await _details.LoadDetailsAsync(
                references,
                new DetailsCrawlOptions(SelectedParallelism, HeadlessDetails),
                progress,
                _cts!.Token);

            totalSw.Stop();
            var succeeded = results.Count(x => x.IsFull);
            var partial = results.Count(x => x.IsPartial);
            var failed = results.Count(x => !x.IsSuccess);

            StatusText = failed == 0 ? "Готов" : "Готово с ошибками";
            CurrentStage = "Данные форм собраны";
            ContactsStepStatus = $"Готово · {succeeded} полн. · {partial} част. · {failed} ошибок";
            OverallProgressPercent = 100;
            OverallProgressText = $"Контакты обработаны за {totalSw.Elapsed:hh\\:mm\\:ss}";
            ProgressMessage =
                $"Организации обработаны: полностью {succeeded}, частично {partial}, ошибок {failed}. " +
                $"Workers: {Math.Clamp(SelectedParallelism, 1, 6)}.";

            _telemetry.Success(
                ParserPipelineStage.Completed,
                "Pipeline completed",
                ProgressMessage,
                totalSw.Elapsed,
                totalItems: results.Count,
                itemIndex: results.Count,
                succeeded: succeeded,
                failed: failed,
                partial: partial);
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
        OverallProgressText = "Операция отменена";
        if (CurrentPipelineStep == 2)
            CatalogStepStatus = "Отменено";
        if (CurrentPipelineStep == 3)
            ContactsStepStatus = "Отменено";

        _telemetry.Warning(
            ParserPipelineStage.Cancelled,
            "Operation cancelled",
            "Операция отменена пользователем.");
    }

    private void SetError(Exception ex)
    {
        StatusText = "Ошибка";
        CurrentStage = "Ошибка";
        ProgressMessage = ex.Message;
        OverallProgressText = "Ошибка";
        if (CurrentPipelineStep == 2)
            CatalogStepStatus = "Ошибка";
        if (CurrentPipelineStep == 3)
            ContactsStepStatus = "Ошибка";

        _telemetry.Error(
            ParserPipelineStage.Failed,
            "UI operation failed",
            ex.Message,
            ex);
    }
}
