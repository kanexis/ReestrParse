using Microsoft.Extensions.DependencyInjection;
using ReestrParse.Application.Catalog;
using ReestrParse.Application.Details;
using ReestrParse.Infrastructure.Selenium.Browser;
using ReestrParse.Infrastructure.Selenium.Eias;
using ReestrParse.Infrastructure.Selenium.Eias.Details;

namespace ReestrParse.Infrastructure.Selenium;

public static class DependencyInjection
{
    public static IServiceCollection AddReestrParseSelenium(this IServiceCollection services)
    {
        services.AddSingleton<SeleniumBrowserSession>();
        services.AddSingleton<IEiasCatalogService, EiasCatalogService>();
        services.AddSingleton<IEiasOrganizationDetailsService, EiasOrganizationDetailsService>();
        return services;
    }
}
