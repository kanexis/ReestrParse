using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ReestrParse.Application.Catalog;
using ReestrParse.Application.Details;
using ReestrParse.Application.Monitoring;
using ReestrParse.Application.Reports;
using ReestrParse.Domain.Catalog;

namespace ReestrParse.Wpf.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private const string SeparateFilesMode = "Отдельный файл по региону";
    private const string GlobalWorkbookMode = "Один Excel — регионы по листам";

    private readonly IEiasCatalogService _catalog;
    private readonly IEiasOrganizationDetailsService _details;
    private readonly IParserTelemetry _telemetry;
    private readonly IRegistryReportWriter _reportWriter;

    private CancellationTokenSource? _operationCts;
    private CancellationTokenSource? _batchCts;
    private CancellationTokenSource? _currentRegionCts;
    private readonly object _pauseSync = new();
    private TaskCompletionSource<bool>? _resumeTcs;
    private bool _skipCurrentRegionRequested;

    public ObservableCollection<RegionOption> Regions { get; } = [];
    public ObservableCollection<SphereOption> Spheres { get; } = [SphereOption.HeatSupply];
    public ObservableCollection<OrganizationRowViewModel> Organizations { get; } = [];
    public ObservableCollection<int> ParallelismOptions { get; } = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
    public ObservableCollection<string> ReportModeOptions { get; } = [SeparateFilesMode, GlobalWorkbookMode];

    public ICollectionView FilteredOrganizations { get; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentRegionPipelineCommand))]
    private RegionOption? selectedRegion;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentRegionPipelineCommand))]
    private SphereOption? selectedSphere = SphereOption.HeatSupply;

    [ObservableProperty]
    private int selectedParallelism = 6;

    [ObservableProperty]
    private bool headlessDetails = true;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string selectedReportMode = SeparateFilesMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ExportCurrentRegionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentRegionPipelineCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAllRegionsCommand))]
    private string outputFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        "ReestrParse");

    [ObservableProperty]
    private bool includeWithoutEmail = true;

    [ObservableProperty]
    private bool includeFailedOrganizations;

    [ObservableProperty]
    private bool autoSaveAfterRegion = true;

    [ObservableProperty]
    private bool continueAfterRegionError = true;

    [ObservableProperty]
    private bool openReportAfterSave = true;

    [ObservableProperty]
    private string lastReportPath = string.Empty;

    [ObservableProperty]
    private string statusText = "Готов";

    [ObservableProperty]
    private string currentStage = "Ожидание";

    [ObservableProperty]
    private string progressMessage = "Выберите регион или запустите обработку всех регионов.";

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
    private string exportStepStatus = "Ожидание данных";

    [ObservableProperty]
    private bool isBatchRunning;

    [ObservableProperty]
    private bool isBatchPaused;

    [ObservableProperty]
    private int batchRegionIndex;

    [ObservableProperty]
    private int batchRegionTotal;

    [ObservableProperty]
    private string batchProgressText = "Пакетный режим не запущен";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadRegionsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadOrganizationsCommand))]
    [NotifyCanExecuteChangedFor(nameof(LoadContactsCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCurrentRegionCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunCurrentRegionPipelineCommand))]
    [NotifyCanExecuteChangedFor(nameof(RunAllRegionsCommand))]
    private bool isBusy;

    public MainWindowViewModel(
        IEiasCatalogService catalog,
        IEiasOrganizationDetailsService details,
        IParserTelemetry telemetry,
        IRegistryReportWriter reportWriter)
    {
        _catalog = catalog;
        _details = details;
        _telemetry = telemetry;
        _reportWriter = reportWriter;
        _telemetry.Published += OnTelemetryPublished;

        FilteredOrganizations = CollectionViewSource.GetDefaultView(Organizations);
        FilteredOrganizations.Filter = FilterOrganization;
    }

    partial void OnSearchTextChanged(string value) => FilteredOrganizations.Refresh();

    public string ReportModeHint => SelectedReportMode == GlobalWorkbookMode
        ? "Один файл Реестр_Теплоснабжение_дата.xlsx, каждый регион — отдельный лист."
        : "Каждый регион сохраняется отдельным файлом Реестр_Регион_Теплоснабжение_дата.xlsx.";

    partial void OnSelectedReportModeChanged(string value)
    {
        OnPropertyChanged(nameof(ReportModeHint));
    }

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
                ExportStepStatus = "Можно создавать Excel";
                CurrentStage = "Данные форм готовы";
                break;

            case ParserPipelineStage.Report:
                CurrentPipelineStep = 4;
                ExportStepStatus = evt.Message;
                CurrentStage = "Excel-отчёт";
                break;

            case ParserPipelineStage.Batch:
                CurrentStage = "Пакетная обработка";
                break;

            case ParserPipelineStage.Completed:
                CurrentPipelineStep = 4;
                ExportStepStatus = "Результат готов";
                CurrentStage = "Готово";
                break;

            case ParserPipelineStage.Cancelled:
                CurrentStage = "Остановлено";
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
            OverallProgressText = $"Организации: {evt.ItemIndex.Value}/{evt.TotalItems.Value}";
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
        ParserPipelineStage.CatalogPages => "Чтение каталога",
        ParserPipelineStage.DetailsQueue => "Очередь workers",
        ParserPipelineStage.DetailsNavigation => "Карточка организации",
        ParserPipelineStage.DetailsFormDiscovery => "Поиск форм",
        ParserPipelineStage.DetailsTemplate => "Чтение формы",
        ParserPipelineStage.DetailsForm411 => "Форма 4.1.1",
        ParserPipelineStage.DetailsForm101 => "Форма 1.0.1",
        ParserPipelineStage.DetailsDataQuality => "Проверка данных",
        ParserPipelineStage.Report => "Excel-отчёт",
        ParserPipelineStage.Batch => "Пакетная обработка",
        _ => stage.ToString()
    };

    public async Task InitializeAsync() => await LoadRegionsAsync();

    private bool FilterOrganization(object item)
    {
        if (item is not OrganizationRowViewModel org)
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return org.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.Inn.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.PreferredEmail.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.EmailSource.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ContactStatus.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               org.ContactError.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    [RelayCommand(CanExecute = nameof(CanLoadRegions))]
    private async Task LoadRegionsAsync()
    {
        ResetManualCancellation();
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
            ProgressMessage = "Получаем список регионов...";

            var regions = await _catalog.LoadRegionsAsync(_operationCts!.Token);

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
            RefreshCommandStates();
        }
    }

    private bool CanLoadRegions() => !IsBusy && !IsBatchRunning;

    [RelayCommand(CanExecute = nameof(CanLoadOrganizations))]
    private async Task LoadOrganizationsAsync()
    {
        if (SelectedRegion is null || SelectedSphere is null)
            return;

        ResetManualCancellation();
        try
        {
            IsBusy = true;
            await LoadOrganizationsCoreAsync(SelectedRegion, _operationCts!.Token);
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
            RefreshCommandStates();
        }
    }

    private bool CanLoadOrganizations()
        => !IsBusy && !IsBatchRunning && SelectedRegion is not null && SelectedSphere is not null;

    [RelayCommand(CanExecute = nameof(CanLoadContacts))]
    private async Task LoadContactsAsync()
    {
        if (Organizations.Count == 0)
            return;

        ResetManualCancellation();
        try
        {
            IsBusy = true;
            await LoadContactsCoreAsync(_operationCts!.Token);
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
            RefreshCommandStates();
        }
    }

    private bool CanLoadContacts() => !IsBusy && !IsBatchRunning && Organizations.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExportCurrentRegion))]
    private async Task ExportCurrentRegionAsync()
    {
        if (SelectedRegion is null || Organizations.Count == 0)
            return;

        ResetManualCancellation();
        try
        {
            IsBusy = true;
            var path = await ExportRegionAsync(SelectedRegion, _operationCts!.Token);
            FinishReport(path, "Excel текущего региона сохранён.");
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
            RefreshCommandStates();
        }
    }

    private bool CanExportCurrentRegion()
        => !IsBusy && !IsBatchRunning && SelectedRegion is not null &&
           Organizations.Any(x => x.ContactStatus != "Не загружены") &&
           !string.IsNullOrWhiteSpace(OutputFolder);

    [RelayCommand(CanExecute = nameof(CanRunCurrentRegionPipeline))]
    private async Task RunCurrentRegionPipelineAsync()
    {
        if (SelectedRegion is null || SelectedSphere is null)
            return;

        ResetManualCancellation();
        try
        {
            IsBusy = true;
            await LoadOrganizationsCoreAsync(SelectedRegion, _operationCts!.Token);
            await LoadContactsCoreAsync(_operationCts.Token);
            var path = await ExportRegionAsync(SelectedRegion, _operationCts.Token);
            FinishReport(path, "Регион полностью обработан и сохранён в Excel.");
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
            RefreshCommandStates();
        }
    }

    private bool CanRunCurrentRegionPipeline()
        => !IsBusy && !IsBatchRunning && SelectedRegion is not null && SelectedSphere is not null && !string.IsNullOrWhiteSpace(OutputFolder);

    [RelayCommand(CanExecute = nameof(CanRunAllRegions))]
    private async Task RunAllRegionsAsync()
    {
        if (Regions.Count == 0 || SelectedSphere is null)
            return;

        ResetBatchCancellation();
        var reportDate = DateOnly.FromDateTime(DateTime.Now);
        var completedSheets = new List<RegistryReportSheet>();
        string? globalPath = null;
        var succeededRegions = 0;
        var failedRegions = 0;
        var skippedRegions = 0;

        try
        {
            IsBusy = true;
            IsBatchRunning = true;
            IsBatchPaused = false;
            BatchRegionIndex = 0;
            BatchRegionTotal = Regions.Count;
            StatusText = "Все регионы";
            BatchProgressText = $"Подготовка очереди: {Regions.Count} регионов";

            _telemetry.Info(
                ParserPipelineStage.Batch,
                "Batch started",
                $"Запущена последовательная обработка {Regions.Count} регионов. Workers внутри региона: {SelectedParallelism}.",
                totalItems: Regions.Count,
                itemIndex: 0,
                code: "BATCH_STARTED");

            for (var i = 0; i < Regions.Count; i++)
            {
                _batchCts!.Token.ThrowIfCancellationRequested();
                await WaitIfBatchPausedAsync(_batchCts.Token);

                var region = Regions[i];
                SelectedRegion = region;
                BatchRegionIndex = i + 1;
                BatchProgressText = $"Регион {BatchRegionIndex}/{BatchRegionTotal}: {region.Name}";
                _skipCurrentRegionRequested = false;

                _currentRegionCts?.Dispose();
                _currentRegionCts = CancellationTokenSource.CreateLinkedTokenSource(_batchCts.Token);
                var token = _currentRegionCts.Token;

                _telemetry.Info(
                    ParserPipelineStage.Batch,
                    "Region batch started",
                    $"Регион {BatchRegionIndex}/{BatchRegionTotal}: {region.Name}.",
                    itemIndex: BatchRegionIndex,
                    totalItems: BatchRegionTotal,
                    code: "BATCH_REGION_STARTED",
                    dataSource: region.Name);

                try
                {
                    await LoadOrganizationsCoreAsync(region, token);
                    await WaitIfBatchPausedAsync(token);

                    await LoadContactsCoreAsync(token);
                    await WaitIfBatchPausedAsync(token);

                    var sheet = BuildReportSheet(region);
                    completedSheets.RemoveAll(x => x.RegionName.Equals(region.Name, StringComparison.OrdinalIgnoreCase));
                    completedSheets.Add(sheet);

                    if (CurrentReportMode == RegistryReportMode.SeparateRegionFiles)
                    {
                        var path = await _reportWriter.WriteRegionAsync(
                            OutputFolder,
                            sheet,
                            CurrentReportSettings,
                            reportDate,
                            token);
                        LastReportPath = path;
                    }
                    else if (AutoSaveAfterRegion)
                    {
                        globalPath = await _reportWriter.WriteGlobalAsync(
                            OutputFolder,
                            completedSheets,
                            CurrentReportSettings,
                            reportDate,
                            token,
                            globalPath);
                        LastReportPath = globalPath;
                    }

                    succeededRegions++;
                    _telemetry.Success(
                        ParserPipelineStage.Batch,
                        "Region batch completed",
                        $"{region.Name}: обработано и сохранено в очередь отчёта.",
                        itemIndex: BatchRegionIndex,
                        totalItems: BatchRegionTotal,
                        succeeded: succeededRegions,
                        failed: failedRegions,
                        partial: skippedRegions,
                        code: "BATCH_REGION_COMPLETED",
                        dataSource: region.Name);
                }
                catch (OperationCanceledException) when (_skipCurrentRegionRequested && !_batchCts.Token.IsCancellationRequested)
                {
                    skippedRegions++;
                    Organizations.Clear();
                    FilteredOrganizations.Refresh();
                    BatchProgressText = $"{region.Name}: пропущен пользователем";
                    _telemetry.Warning(
                        ParserPipelineStage.Batch,
                        "Region skipped",
                        $"Регион «{region.Name}» пропущен и не включён в итоговый Excel.",
                        itemIndex: BatchRegionIndex,
                        totalItems: BatchRegionTotal,
                        succeeded: succeededRegions,
                        failed: failedRegions,
                        partial: skippedRegions,
                        code: "BATCH_REGION_SKIPPED",
                        dataSource: region.Name);
                }
                catch (Exception ex)
                {
                    failedRegions++;
                    _telemetry.Error(
                        ParserPipelineStage.Batch,
                        "Region batch failed",
                        $"Регион «{region.Name}» завершился с ошибкой.",
                        ex,
                        itemIndex: BatchRegionIndex,
                        totalItems: BatchRegionTotal,
                        succeeded: succeededRegions,
                        failed: failedRegions,
                        partial: skippedRegions,
                        code: "BATCH_REGION_FAILED",
                        dataSource: region.Name);

                    if (!ContinueAfterRegionError)
                        throw;
                }

                await WaitIfBatchPausedAsync(_batchCts.Token);
            }

            if (CurrentReportMode == RegistryReportMode.SingleWorkbookByRegion && completedSheets.Count > 0)
            {
                globalPath = await _reportWriter.WriteGlobalAsync(
                    OutputFolder,
                    completedSheets,
                    CurrentReportSettings,
                    reportDate,
                    _batchCts!.Token,
                    globalPath);
                LastReportPath = globalPath;
            }

            StatusText = failedRegions == 0 ? "Готов" : "Готово с ошибками";
            CurrentStage = "Все регионы обработаны";
            OverallProgressPercent = 100;
            OverallProgressText = $"Регионы: {BatchRegionTotal}/{BatchRegionTotal}";
            BatchProgressText = $"Готово · OK {succeededRegions} · пропущено {skippedRegions} · ошибок {failedRegions}";
            ExportStepStatus = string.IsNullOrWhiteSpace(LastReportPath) ? "Нет данных для отчёта" : "Excel сохранён";
            ProgressMessage = BatchProgressText;

            _telemetry.Success(
                ParserPipelineStage.Completed,
                "Batch completed",
                BatchProgressText,
                totalItems: BatchRegionTotal,
                itemIndex: BatchRegionTotal,
                succeeded: succeededRegions,
                failed: failedRegions,
                partial: skippedRegions,
                code: "BATCH_COMPLETED");

            if (OpenReportAfterSave && !string.IsNullOrWhiteSpace(LastReportPath))
                TryOpenPath(LastReportPath);
        }
        catch (OperationCanceledException)
        {
            SetCanceled();
            BatchProgressText = $"Остановлено на регионе {BatchRegionIndex}/{BatchRegionTotal}";
        }
        catch (Exception ex)
        {
            SetError(ex);
        }
        finally
        {
            IsBatchRunning = false;
            IsBatchPaused = false;
            IsBusy = false;
            _currentRegionCts?.Dispose();
            _currentRegionCts = null;
            RefreshCommandStates();
        }
    }

    private bool CanRunAllRegions()
        => !IsBusy && !IsBatchRunning && Regions.Count > 0 && SelectedSphere is not null && !string.IsNullOrWhiteSpace(OutputFolder);

    [RelayCommand]
    private void PauseOrResumeBatch()
    {
        if (!IsBatchRunning)
            return;

        lock (_pauseSync)
        {
            if (IsBatchPaused)
            {
                IsBatchPaused = false;
                _resumeTcs?.TrySetResult(true);
                _resumeTcs = null;
                BatchProgressText = $"Продолжаем · регион {BatchRegionIndex}/{BatchRegionTotal}";
                _telemetry.Info(ParserPipelineStage.Batch, "Batch resumed", "Пакетная обработка продолжена.", code: "BATCH_RESUMED");
            }
            else
            {
                IsBatchPaused = true;
                _resumeTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                BatchProgressText = "Пауза будет применена на ближайшей безопасной точке.";
                _telemetry.Info(ParserPipelineStage.Batch, "Batch pause requested", BatchProgressText, code: "BATCH_PAUSE_REQUESTED");
            }
        }
    }

    [RelayCommand]
    private void SkipCurrentRegion()
    {
        if (!IsBatchRunning || _currentRegionCts is null)
            return;

        _skipCurrentRegionRequested = true;
        BatchProgressText = $"Пропускаем текущий регион: {SelectedRegion?.Name}";
        _currentRegionCts.Cancel();
    }

    [RelayCommand]
    private void BrowseOutputFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Выберите папку для Excel-отчётов ReestrParse",
            SelectedPath = Directory.Exists(OutputFolder) ? OutputFolder : string.Empty,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK && !string.IsNullOrWhiteSpace(dialog.SelectedPath))
            OutputFolder = dialog.SelectedPath;
    }

    [RelayCommand]
    private void OpenOutputFolder()
    {
        if (string.IsNullOrWhiteSpace(OutputFolder))
            return;

        Directory.CreateDirectory(OutputFolder);
        TryOpenPath(OutputFolder);
    }

    [RelayCommand]
    private void OpenLastReport()
    {
        if (File.Exists(LastReportPath))
            TryOpenPath(LastReportPath);
    }

    [RelayCommand]
    private void Cancel()
    {
        _batchCts?.Cancel();
        _currentRegionCts?.Cancel();
        _operationCts?.Cancel();
    }

    private async Task<IReadOnlyList<OrganizationRowViewModel>> LoadOrganizationsCoreAsync(
        RegionOption region,
        CancellationToken cancellationToken)
    {
        if (SelectedSphere is null)
            throw new InvalidOperationException("Не выбрана сфера деятельности.");

        var sw = Stopwatch.StartNew();
        CurrentPipelineStep = 2;
        RegionStepStatus = region.Name;
        CatalogStepStatus = "Подготовка...";
        ContactsStepStatus = "Ожидание каталога";
        ExportStepStatus = "Ожидание данных";
        OverallProgressPercent = 0;
        OverallProgressText = "Подготовка каталога";
        StatusText = IsBatchRunning ? "Регион" : "Сбор";
        Organizations.Clear();
        FilteredOrganizations.Refresh();
        RefreshCommandStates();

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
        });

        var organizations = await _catalog.LoadOrganizationsAsync(
            region,
            SelectedSphere,
            progress,
            cancellationToken);

        foreach (var organization in organizations)
            Organizations.Add(new OrganizationRowViewModel(organization));

        FilteredOrganizations.Refresh();
        sw.Stop();

        StatusText = "Каталог готов";
        CurrentStage = "Каталог собран";
        CatalogStepStatus = $"Готово · {Organizations.Count} организаций";
        ContactsStepStatus = "Готов к запуску";
        OverallProgressPercent = 100;
        OverallProgressText = $"Каталог за {sw.Elapsed.TotalSeconds:F1} с";
        ProgressMessage = $"{region.Name}: найдено {Organizations.Count} организаций.";
        RefreshCommandStates();

        return Organizations.ToArray();
    }

    private async Task<IReadOnlyList<OrganizationDetailsResult>> LoadContactsCoreAsync(CancellationToken cancellationToken)
    {
        if (Organizations.Count == 0)
            return [];

        var totalSw = Stopwatch.StartNew();
        CurrentPipelineStep = 3;
        CatalogStepStatus = $"Готово · {Organizations.Count} организаций";
        ContactsStepStatus = $"Запуск {Math.Clamp(SelectedParallelism, 1, 10)} workers...";
        OverallProgressPercent = 0;
        OverallProgressText = $"Организации: 0/{Organizations.Count}";
        StatusText = "Карточки";
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

            CurrentStage = $"Организации {p.Completed}/{p.Total}";
            ContactsStepStatus = $"{p.Completed}/{p.Total} · OK {p.Succeeded} · PART {p.Partial} · ERR {p.Failed}";
            OverallProgressPercent = p.Total > 0
                ? Math.Clamp(p.Completed * 100d / p.Total, 0d, 100d)
                : 0;
            OverallProgressText = $"Организации: {p.Completed}/{p.Total}";
            ProgressMessage = $"{p.Message}. Ошибок: {p.Failed}.";
            FilteredOrganizations.Refresh();
        });

        var results = await _details.LoadDetailsAsync(
            references,
            new DetailsCrawlOptions(SelectedParallelism, HeadlessDetails),
            progress,
            cancellationToken);

        totalSw.Stop();
        var succeeded = results.Count(x => x.IsFull);
        var partial = results.Count(x => x.IsPartial);
        var failed = results.Count(x => !x.IsSuccess);

        StatusText = failed == 0 ? "Данные готовы" : "Есть ошибки";
        CurrentStage = "Данные форм собраны";
        ContactsStepStatus = $"Готово · {succeeded} полн. · {partial} част. · {failed} ошибок";
        ExportStepStatus = "Можно сохранить Excel";
        OverallProgressPercent = 100;
        OverallProgressText = $"Готово за {totalSw.Elapsed:hh\\:mm\\:ss}";
        ProgressMessage = $"Обработано {results.Count}: успешно {succeeded}, частично {partial}, ошибок {failed}.";
        FilteredOrganizations.Refresh();
        RefreshCommandStates();

        return results;
    }

    private RegistryReportSheet BuildReportSheet(RegionOption region)
    {
        var rows = Organizations
            .Where(x => x.IncludeInReport)
            .Select(x => new RegistryReportRow(
                x.Name,
                x.Inn,
                x.PreferredEmail,
                x.ReportSucceeded))
            .ToArray();

        return new RegistryReportSheet(region.Name, rows);
    }

    private async Task<string> ExportRegionAsync(RegionOption region, CancellationToken cancellationToken)
    {
        CurrentPipelineStep = 4;
        ExportStepStatus = "Создание Excel...";
        CurrentStage = "Excel-отчёт";
        ProgressMessage = "Формируем итоговую таблицу: Наименование · ИНН · Email.";

        var sheet = BuildReportSheet(region);
        string path;

        if (CurrentReportMode == RegistryReportMode.SingleWorkbookByRegion)
        {
            path = await _reportWriter.WriteGlobalAsync(
                OutputFolder,
                [sheet],
                CurrentReportSettings,
                DateOnly.FromDateTime(DateTime.Now),
                cancellationToken);
        }
        else
        {
            path = await _reportWriter.WriteRegionAsync(
                OutputFolder,
                sheet,
                CurrentReportSettings,
                DateOnly.FromDateTime(DateTime.Now),
                cancellationToken);
        }

        _telemetry.Success(
            ParserPipelineStage.Report,
            "Excel report saved",
            $"Excel сохранён: {path}",
            totalItems: sheet.Rows.Count,
            itemIndex: sheet.Rows.Count,
            code: "REPORT_SAVED",
            dataSource: region.Name);

        return path;
    }

    private void FinishReport(string path, string message)
    {
        LastReportPath = path;
        StatusText = "Готов";
        CurrentPipelineStep = 4;
        CurrentStage = "Excel сохранён";
        ExportStepStatus = Path.GetFileName(path);
        ProgressMessage = message;
        OverallProgressPercent = 100;
        OverallProgressText = "Результат готов";

        if (OpenReportAfterSave)
            TryOpenPath(path);
    }

    private RegistryReportMode CurrentReportMode =>
        SelectedReportMode == GlobalWorkbookMode
            ? RegistryReportMode.SingleWorkbookByRegion
            : RegistryReportMode.SeparateRegionFiles;

    private RegistryReportSettings CurrentReportSettings => new(
        CurrentReportMode,
        IncludeWithoutEmail,
        IncludeFailedOrganizations,
        AutoSaveAfterRegion);

    private async Task WaitIfBatchPausedAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            Task waitTask;
            lock (_pauseSync)
            {
                if (!IsBatchPaused)
                    return;

                _resumeTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                waitTask = _resumeTcs.Task;
            }

            await waitTask.WaitAsync(cancellationToken);
        }
    }

    private void ResetManualCancellation()
    {
        _operationCts?.Cancel();
        _operationCts?.Dispose();
        _operationCts = new CancellationTokenSource();
    }

    private void ResetBatchCancellation()
    {
        _batchCts?.Cancel();
        _batchCts?.Dispose();
        _batchCts = new CancellationTokenSource();
        _skipCurrentRegionRequested = false;
    }

    private void SetCanceled()
    {
        StatusText = "Остановлено";
        CurrentStage = "Операция остановлена";
        ProgressMessage = "Операция остановлена пользователем.";
        OverallProgressText = "Остановлено";
        if (CurrentPipelineStep == 2)
            CatalogStepStatus = "Остановлено";
        if (CurrentPipelineStep == 3)
            ContactsStepStatus = "Остановлено";

        _telemetry.Warning(
            ParserPipelineStage.Cancelled,
            "Operation cancelled",
            "Операция остановлена пользователем.");
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
        if (CurrentPipelineStep == 4)
            ExportStepStatus = "Ошибка";

        _telemetry.Error(
            ParserPipelineStage.Failed,
            "UI operation failed",
            ex.Message,
            ex);
    }

    private static void TryOpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Открытие проводника/Excel — удобство, но не часть критического pipeline.
        }
    }

    private void RefreshCommandStates()
    {
        LoadRegionsCommand.NotifyCanExecuteChanged();
        LoadOrganizationsCommand.NotifyCanExecuteChanged();
        LoadContactsCommand.NotifyCanExecuteChanged();
        ExportCurrentRegionCommand.NotifyCanExecuteChanged();
        RunCurrentRegionPipelineCommand.NotifyCanExecuteChanged();
        RunAllRegionsCommand.NotifyCanExecuteChanged();
    }
}
