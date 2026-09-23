using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using ReestrParse.Application.Monitoring;

namespace ReestrParse.Wpf.ViewModels;

public partial class ParserMonitorWindowViewModel : ObservableObject, IDisposable
{
    private readonly IParserTelemetry _telemetry;
    private readonly Stopwatch _sessionStopwatch = new();
    private readonly List<TimeSpan> _itemDurations = [];
    private readonly List<ParserLogEntryViewModel> _allLogs = [];
    private readonly DispatcherTimer _uiTimer; 
    private bool _disposed;
    private bool _batchSessionActive;

    public ObservableCollection<ParserLogEntryViewModel> Logs { get; } = [];
    public ObservableCollection<string> LevelFilters { get; } =
        ["Все", "Ошибки", "Предупреждения", "Успешные", "Информация"];
    public ObservableCollection<string> LogModeOptions { get; } =
        ["Основные события", "Подробный лог"];

    public ICollectionView FilteredLogs { get; }

    [ObservableProperty]
    private string selectedLevelFilter = "Все";

    [ObservableProperty]
    private string selectedLogMode = "Основные события";

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string currentStage = "Ожидание";

    [ObservableProperty]
    private string currentOperation = "—";

    [ObservableProperty]
    private string currentOrganization = "—";

    [ObservableProperty]
    private string elapsedText = "00:00:00";

    [ObservableProperty]
    private string averageItemText = "—";

    [ObservableProperty]
    private string etaText = "—";

    [ObservableProperty]
    private string throughputText = "—";

    [ObservableProperty]
    private int totalItems;

    [ObservableProperty]
    private int completedItems;

    [ObservableProperty]
    private int succeededItems;

    [ObservableProperty]
    private int failedItems;

    [ObservableProperty]
    private int partialItems;

    [ObservableProperty]
    private int activeWorkers;

    [ObservableProperty]
    private int currentPage;

    [ObservableProperty]
    private int totalPages;

    [ObservableProperty]
    private double progressPercent;

    [ObservableProperty]
    private string progressCaption = "0%";

    [ObservableProperty]
    private string lastError = "Ошибок пока нет.";

    [ObservableProperty]
    private int warningEvents;

    [ObservableProperty]
    private int errorEvents;

    [ObservableProperty]
    private int retryEvents;

    [ObservableProperty]
    private int completedRegions;

    public ParserMonitorWindowViewModel(IParserTelemetry telemetry)
    {
        _telemetry = telemetry;
        _telemetry.Published += OnPublished;

        FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
        FilteredLogs.Filter = FilterLog;

        // Метрики времени должны обновляться даже в паузах между telemetry-событиями.
        // DispatcherTimer работает в UI-потоке и не требует дополнительной синхронизации с WPF bindings.
        _uiTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _uiTimer.Tick += (_, _) => RefreshLiveTimeMetrics();
        _uiTimer.Start();
    }

    partial void OnSelectedLevelFilterChanged(string value) => FilteredLogs.Refresh();
    partial void OnSelectedLogModeChanged(string value) => FilteredLogs.Refresh();
    partial void OnSearchTextChanged(string value) => FilteredLogs.Refresh();

    private bool FilterLog(object item)
    {
        if (item is not ParserLogEntryViewModel log)
            return false;

        var levelMatches = SelectedLevelFilter switch
        {
            "Ошибки" => log.Source.Level == ParserLogLevel.Error,
            "Предупреждения" => log.Source.Level == ParserLogLevel.Warning,
            "Успешные" => log.Source.Level == ParserLogLevel.Success,
            "Информация" => log.Source.Level is ParserLogLevel.Info or ParserLogLevel.Trace,
            _ => true
        };

        if (!levelMatches)
            return false;

        if (SelectedLogMode == "Основные события" && !IsImportant(log.Source))
            return false;

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return log.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Organization.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Inn.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Operation.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.DataSource.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsImportant(ParserTelemetryEvent evt)
    {
        if (evt.Level is ParserLogLevel.Error or ParserLogLevel.Warning)
            return true;

        if (evt.Stage is ParserPipelineStage.Report or ParserPipelineStage.Batch or
                         ParserPipelineStage.Completed or ParserPipelineStage.Cancelled or ParserPipelineStage.Failed)
            return true;

        if (!string.IsNullOrWhiteSpace(evt.Code) &&
            (evt.Code.Contains("BACKOFF", StringComparison.OrdinalIgnoreCase) ||
             evt.Code.Contains("RETRY", StringComparison.OrdinalIgnoreCase) ||
             evt.Code.Contains("REPORT_", StringComparison.OrdinalIgnoreCase) ||
             evt.Code.Contains("BATCH_", StringComparison.OrdinalIgnoreCase)))
            return true;

        return evt.Operation.Equals("Regions loaded", StringComparison.OrdinalIgnoreCase) ||
               evt.Operation.Equals("Catalog completed", StringComparison.OrdinalIgnoreCase) ||
               evt.Operation.Equals("Details crawl completed", StringComparison.OrdinalIgnoreCase) ||
               evt.Operation.Equals("Pipeline completed", StringComparison.OrdinalIgnoreCase);
    }

    private void OnPublished(ParserTelemetryEvent evt)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        _ = dispatcher.BeginInvoke(() => Apply(evt));
    }

    private void Apply(ParserTelemetryEvent evt)
    {
        if (evt.Code?.Equals("BATCH_STARTED", StringComparison.OrdinalIgnoreCase) == true)
        {
            ResetRuntimeMetrics(clearLogs: false);
            _batchSessionActive = true;
            _sessionStopwatch.Restart();
        }
        else if (evt.Operation.Equals("Catalog started", StringComparison.OrdinalIgnoreCase) && !_batchSessionActive)
        {
            ResetRuntimeMetrics(clearLogs: false);
            _sessionStopwatch.Restart();
        }
        else if (!_sessionStopwatch.IsRunning && evt.Stage is not ParserPipelineStage.Idle)
        {
            _sessionStopwatch.Start();
        }

        var entry = new ParserLogEntryViewModel(evt);
        Logs.Add(entry);
        _allLogs.Add(entry);
        while (_allLogs.Count > 100_000)
            _allLogs.RemoveAt(0);

        if (evt.Level == ParserLogLevel.Warning) WarningEvents++;
        if (evt.Level == ParserLogLevel.Error) ErrorEvents++;
        if (!string.IsNullOrWhiteSpace(evt.Code) &&
            (evt.Code.Contains("RETRY", StringComparison.OrdinalIgnoreCase) ||
             evt.Code.Contains("BACKOFF", StringComparison.OrdinalIgnoreCase)))
            RetryEvents++;
        if (evt.Code?.Equals("BATCH_REGION_COMPLETED", StringComparison.OrdinalIgnoreCase) == true)
            CompletedRegions++;

        // Ограничиваем UI-коллекцию, чтобы многочасовой прогон не начал тормозить WPF.
        while (Logs.Count > 3000)
            Logs.RemoveAt(0);

        CurrentStage = StageCaption(evt.Stage);
        CurrentOperation = evt.Operation;
        CurrentOrganization = string.IsNullOrWhiteSpace(evt.OrganizationName)
            ? CurrentOrganization
            : evt.OrganizationName;

        if (evt.TotalItems is > 0)
            TotalItems = evt.TotalItems.Value;

        if (evt.ItemIndex is > 0)
            CompletedItems = Math.Max(CompletedItems, evt.ItemIndex.Value);

        if (evt.Succeeded.HasValue)
            SucceededItems = evt.Succeeded.Value;

        if (evt.Failed.HasValue)
            FailedItems = evt.Failed.Value;

        if (evt.Partial.HasValue)
            PartialItems = evt.Partial.Value;

        if (evt.Page is > 0)
            CurrentPage = evt.Page.Value;

        if (evt.TotalPages is > 0)
            TotalPages = evt.TotalPages.Value;

        if (evt.WorkerId is > 0 &&
            evt.Operation.Contains("Worker started", StringComparison.OrdinalIgnoreCase))
        {
            ActiveWorkers++;
        }

        if (evt.WorkerId is > 0 &&
            evt.Operation.Contains("Worker stopped", StringComparison.OrdinalIgnoreCase))
        {
            ActiveWorkers = Math.Max(0, ActiveWorkers - 1);
        }

        if (evt.Duration.HasValue &&
            evt.ItemIndex is > 0 &&
            evt.Operation.Equals("Organization completed", StringComparison.OrdinalIgnoreCase))
        {
            _itemDurations.Add(evt.Duration.Value);
            while (_itemDurations.Count > 500)
                _itemDurations.RemoveAt(0);
        }

        if (evt.Level == ParserLogLevel.Error)
            LastError = string.IsNullOrWhiteSpace(evt.Error) ? evt.Message : evt.Error;

        UpdateMetrics(evt);

        if (evt.Stage is ParserPipelineStage.Completed or
                         ParserPipelineStage.DetailsCompleted or
                         ParserPipelineStage.Cancelled or
                         ParserPipelineStage.Failed)
        {
            ElapsedText = FormatDuration(_sessionStopwatch.Elapsed);
        }

        if (evt.Code?.Equals("BATCH_COMPLETED", StringComparison.OrdinalIgnoreCase) == true ||
            evt.Stage is ParserPipelineStage.Cancelled or ParserPipelineStage.Failed)
        {
            _batchSessionActive = false;
        }

        FilteredLogs.Refresh();
    }

    private void UpdateMetrics(ParserTelemetryEvent evt)
    {
        ElapsedText = FormatDuration(_sessionStopwatch.Elapsed);

        if (evt.ProgressPercent.HasValue)
        {
            ProgressPercent = evt.ProgressPercent.Value;
        }
        else if (TotalItems > 0)
        {
            ProgressPercent = Math.Clamp(CompletedItems * 100d / TotalItems, 0d, 100d);
        }
        else if (TotalPages > 0)
        {
            ProgressPercent = Math.Clamp(CurrentPage * 100d / TotalPages, 0d, 100d);
        }

        ProgressCaption = $"{ProgressPercent:F1}%";
        RefreshDerivedMetrics();
    }

    private void RefreshLiveTimeMetrics()
    {
        if (!_sessionStopwatch.IsRunning)
            return;

        ElapsedText = FormatDuration(_sessionStopwatch.Elapsed);
        RefreshDerivedMetrics();
    }

    private void RefreshDerivedMetrics()
    {
        if (_itemDurations.Count == 0)
            return;

        var average = TimeSpan.FromTicks((long)_itemDurations.Average(x => x.Ticks));
        AverageItemText = FormatShortDuration(average);

        if (average.TotalSeconds > 0)
            ThroughputText = $"{60d / average.TotalSeconds:F1} орг/мин";

        if (TotalItems > 0 && CompletedItems < TotalItems)
        {
            var remaining = TotalItems - CompletedItems;
            var effectiveWorkers = Math.Max(1, ActiveWorkers);
            var eta = TimeSpan.FromTicks((long)(average.Ticks * remaining / (double)effectiveWorkers));
            EtaText = FormatDuration(eta);
        }
        else if (TotalItems > 0 && CompletedItems >= TotalItems)
        {
            EtaText = "00:00:00";
        }
    }

    [RelayCommand]
    private void ClearLogs()
    {
        Logs.Clear();
        _allLogs.Clear();
        LastError = "Ошибок пока нет.";
    }

    [RelayCommand]
    private void ResetMetrics() => ResetRuntimeMetrics(clearLogs: false);

    private void ResetRuntimeMetrics(bool clearLogs)
    {
        if (clearLogs)
        {
            Logs.Clear();
            _allLogs.Clear();
        }

        TotalItems = 0;
        CompletedItems = 0;
        SucceededItems = 0;
        FailedItems = 0;
        PartialItems = 0;
        ActiveWorkers = 0;
        CurrentPage = 0;
        TotalPages = 0;
        ProgressPercent = 0;
        ProgressCaption = "0%";
        AverageItemText = "—";
        EtaText = "—";
        ThroughputText = "—";
        WarningEvents = 0;
        ErrorEvents = 0;
        RetryEvents = 0;
        CompletedRegions = 0;
        CurrentOrganization = "—";
        CurrentOperation = "—";
        _itemDurations.Clear();
        _sessionStopwatch.Reset();
        ElapsedText = "00:00:00";
    }

    [RelayCommand]
    private void ExportLogs()
    {
        var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "Сохранить журнал парсинга",
            Filter = "CSV (*.csv)|*.csv|Текст (*.txt)|*.txt",
            FileName = $"reestrparse-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != DialogResult.OK)
            return;

        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("Time;Level;Stage;Operation;Worker;SourcePage;Position;Duration;Organization;INN;Code;DataSource;Message;Error");

        foreach (var log in _allLogs)
        {
            writer.WriteLine(string.Join(";",
                Csv(log.Time),
                Csv(log.Level),
                Csv(log.Stage),
                Csv(log.Operation),
                Csv(log.Worker),
                Csv(log.Page),
                Csv(log.Position),
                Csv(log.Duration),
                Csv(log.Organization),
                Csv(log.Inn),
                Csv(log.Code),
                Csv(log.DataSource),
                Csv(log.Message),
                Csv(log.Error)));
        }
    }

    private static string Csv(string? value)
        => $"\"{(value ?? string.Empty).Replace("\"", "\"\"")}\"";

    private static string StageCaption(ParserPipelineStage stage) => stage switch
    {
        ParserPipelineStage.Regions => "Регионы",
        ParserPipelineStage.CatalogNavigation => "Навигация каталога",
        ParserPipelineStage.CatalogFilters => "Фильтры каталога",
        ParserPipelineStage.CatalogPages => "Страницы каталога",
        ParserPipelineStage.CatalogCompleted => "Каталог готов",
        ParserPipelineStage.DetailsQueue => "Очередь карточек",
        ParserPipelineStage.DetailsNavigation => "Карточки организаций",
        ParserPipelineStage.DetailsFormDiscovery => "Поиск форм",
        ParserPipelineStage.DetailsTemplate => "TemplatePrinter",
        ParserPipelineStage.DetailsForm411 => "Форма 4.1.1",
        ParserPipelineStage.DetailsForm101 => "Форма 1.0.1",
        ParserPipelineStage.DetailsDataQuality => "Проверка данных",
        ParserPipelineStage.DetailsCompleted => "Данные готовы",
        ParserPipelineStage.Completed => "Завершено",
        ParserPipelineStage.Cancelled => "Отменено",
        ParserPipelineStage.Failed => "Ошибка",
        ParserPipelineStage.Report => "Excel-отчёт",
        ParserPipelineStage.Batch => "Пакетная обработка",
        _ => "Ожидание"
    };

    private static string FormatDuration(TimeSpan value)
        => value.TotalHours >= 1
            ? value.ToString(@"hh\:mm\:ss")
            : value.ToString(@"mm\:ss");

    private static string FormatShortDuration(TimeSpan value)
        => value.TotalSeconds >= 1
            ? $"{value.TotalSeconds:F2} с"
            : $"{value.TotalMilliseconds:F0} мс";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _uiTimer.Stop();
        _telemetry.Published -= OnPublished;
    }
}
