namespace ReestrParse.Application.Monitoring;

public interface IParserTelemetry
{
    event Action<ParserTelemetryEvent>? Published;

    void Publish(ParserTelemetryEvent telemetryEvent);
}
