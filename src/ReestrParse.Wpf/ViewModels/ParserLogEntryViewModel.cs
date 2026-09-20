using ReestrParse.Application.Monitoring;

namespace ReestrParse.Wpf.ViewModels;

public sealed class ParserLogEntryViewModel
{
    public ParserLogEntryViewModel(ParserTelemetryEvent source)
    {
        Source = source;
    }

    public ParserTelemetryEvent Source { get; }

    public string Time => Source.Timestamp.LocalDateTime.ToString("HH:mm:ss.fff");
    public string Level => Source.Level switch
    {
        ParserLogLevel.Trace => "TRACE",
        ParserLogLevel.Info => "INFO",
        ParserLogLevel.Success => "OK",
        ParserLogLevel.Warning => "WARN",
        ParserLogLevel.Error => "ERROR",
        _ => Source.Level.ToString().ToUpperInvariant()
    };

    public string Stage => Source.Stage switch
    {
        ParserPipelineStage.Regions => "Регионы",
        ParserPipelineStage.CatalogNavigation => "Каталог / навигация",
        ParserPipelineStage.CatalogFilters => "Каталог / фильтры",
        ParserPipelineStage.CatalogPages => "Каталог / страницы",
        ParserPipelineStage.CatalogCompleted => "Каталог готов",
        ParserPipelineStage.DetailsQueue => "Workers / очередь",
        ParserPipelineStage.DetailsNavigation => "Карточка организации",
        ParserPipelineStage.DetailsForm411 => "Форма 4.1.1",
        ParserPipelineStage.DetailsCompleted => "Контакты готовы",
        ParserPipelineStage.Completed => "Pipeline завершён",
        ParserPipelineStage.Cancelled => "Отменено",
        ParserPipelineStage.Failed => "Ошибка",
        _ => Source.Stage.ToString()
    };
    public string Operation => Source.Operation;
    public string Worker => Source.WorkerId is > 0 ? $"W{Source.WorkerId}" : "—";

    public string Position
    {
        get
        {
            if (Source.ItemIndex.HasValue && Source.TotalItems > 0)
                return $"{Source.ItemIndex}/{Source.TotalItems}";

            if (Source.Page.HasValue && Source.TotalPages > 0)
                return $"стр. {Source.Page}/{Source.TotalPages}";

            if (Source.Page.HasValue)
                return $"стр. {Source.Page}";

            return "—";
        }
    }

    public string Duration => Source.Duration.HasValue
        ? Source.Duration.Value.TotalSeconds >= 1
            ? $"{Source.Duration.Value.TotalSeconds:F2} с"
            : $"{Source.Duration.Value.TotalMilliseconds:F0} мс"
        : "—";

    public string Organization => Source.OrganizationName ?? string.Empty;
    public string Inn => Source.Inn ?? string.Empty;
    public string Message => Source.Message;
    public string Error => Source.Error ?? string.Empty;
}
