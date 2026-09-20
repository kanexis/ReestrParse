namespace ReestrParse.Application.Monitoring;

public sealed class ParserTelemetryHub : IParserTelemetry
{
    public event Action<ParserTelemetryEvent>? Published;

    public void Publish(ParserTelemetryEvent telemetryEvent)
    {
        ArgumentNullException.ThrowIfNull(telemetryEvent);
        Published?.Invoke(telemetryEvent);
    }
}
