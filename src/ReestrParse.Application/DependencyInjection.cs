using Microsoft.Extensions.DependencyInjection;
using ReestrParse.Application.Monitoring;

namespace ReestrParse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReestrParseApplication(this IServiceCollection services)
    {
        services.AddSingleton<ParserTelemetryHub>();
        services.AddSingleton<IParserTelemetry>(sp => sp.GetRequiredService<ParserTelemetryHub>());
        return services;
    }
}
