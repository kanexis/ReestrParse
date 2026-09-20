using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Data;
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
    private bool _disposed;

    public ObservableCollection<ParserLogEntryViewModel> Logs { get; } = [];
    public ObservableCollection<string> LevelFilters { get; } =
        ["Все", "Ошибки", "Предупреждения", "Успешные", "Информация"];

    public ICollectionView FilteredLogs { get; }

    [ObservableProperty]
    private string selectedLevelFilter = "Все";

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

    public ParserMonitorWindowViewModel(IParserTelemetry telemetry)
    {
        _telemetry = telemetry;
        _telemetry.Published += OnPublished;

        FilteredLogs = CollectionViewSource.GetDefaultView(Logs);
        FilteredLogs.Filter = FilterLog;
    }

    partial void OnSelectedLevelFilterChanged(string value) => FilteredLogs.Refresh();
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

        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        return log.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Organization.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Inn.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
               log.Operation.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
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
        if (evt.Operation.Equals("Catalog started", StringComparison.OrdinalIgnoreCase))
        {
            ResetRuntimeMetrics(clearLogs: false);
            _sessionStopwatch.Restart();
        }
        else if (!_sessionStopwatch.IsRunning && evt.Stage is not ParserPipelineStage.Idle)
        {
            _sessionStopwatch.Start();
        }

        Logs.Add(new ParserLogEntryViewModel(evt));

        // Ограничиваем UI-коллекцию, чтобы многочасовой прогон не начал тормозить WPF.
        while (Logs.Count > 5000)
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
        LastError = "Ошибок пока нет.";
    }

    [RelayCommand]
    private void ResetMetrics() => ResetRuntimeMetrics(clearLogs: false);

    private void ResetRuntimeMetrics(bool clearLogs)
    {
        if (clearLogs)
            Logs.Clear();

        TotalItems = 0;
        CompletedItems = 0;
        SucceededItems = 0;
        FailedItems = 0;
        ActiveWorkers = 0;
        CurrentPage = 0;
        TotalPages = 0;
        ProgressPercent = 0;
        ProgressCaption = "0%";
        AverageItemText = "—";
        EtaText = "—";
        ThroughputText = "—";
        CurrentOrganization = "—";
        CurrentOperation = "—";
        _itemDurations.Clear();
        _sessionStopwatch.Reset();
        ElapsedText = "00:00:00";
    }

    [RelayCommand]
    private void ExportLogs()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Сохранить журнал парсинга",
            Filter = "CSV (*.csv)|*.csv|Текст (*.txt)|*.txt",
            FileName = $"reestrparse-log-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
        };

        if (dialog.ShowDialog() != true)
            return;

        using var writer = new StreamWriter(dialog.FileName, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("Time;Level;Stage;Operation;Worker;Position;Duration;Organization;INN;Message;Error");

        foreach (var log in Logs)
        {
            writer.WriteLine(string.Join(";",
                Csv(log.Time),
                Csv(log.Level),
                Csv(log.Stage),
                Csv(log.Operation),
                Csv(log.Worker),
                Csv(log.Position),
                Csv(log.Duration),
                Csv(log.Organization),
                Csv(log.Inn),
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
        ParserPipelineStage.DetailsForm411 => "Форма 4.1.1",
        ParserPipelineStage.DetailsCompleted => "Контакты готовы",
        ParserPipelineStage.Completed => "Завершено",
        ParserPipelineStage.Cancelled => "Отменено",
        ParserPipelineStage.Failed => "Ошибка",
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
        _telemetry.Published -= OnPublished;
    }
}
