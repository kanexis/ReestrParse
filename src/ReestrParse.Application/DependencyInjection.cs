using Microsoft.Extensions.DependencyInjection;
using ReestrParse.Application.Monitoring;
using ReestrParse.Application.Reports;

namespace ReestrParse.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddReestrParseApplication(this IServiceCollection services)
    {
        services.AddSingleton<ParserTelemetryHub>();
        services.AddSingleton<IParserTelemetry>(sp => sp.GetRequiredService<ParserTelemetryHub>());
        services.AddSingleton<IRegistryReportWriter, XlsxRegistryReportWriter>();
        return services;
    }
}
