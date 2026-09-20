namespace ReestrParse.Application.Monitoring;

public sealed record ParserTelemetryEvent(
    DateTimeOffset Timestamp,
    ParserLogLevel Level,
    ParserPipelineStage Stage,
    string Operation,
    string Message,
    TimeSpan? Duration = null,
    int? WorkerId = null,
    int? Page = null,
    int? TotalPages = null,
    int? ItemIndex = null,
    int? TotalItems = null,
    int? Succeeded = null,
    int? Failed = null,
    string? OrganizationName = null,
    string? Inn = null,
    string? Error = null)
{
    public double? ProgressPercent =>
        ItemIndex.HasValue && TotalItems > 0
            ? Math.Clamp(ItemIndex.Value * 100d / TotalItems.Value, 0d, 100d)
            : Page.HasValue && TotalPages > 0
                ? Math.Clamp(Page.Value * 100d / TotalPages.Value, 0d, 100d)
                : null;
}
