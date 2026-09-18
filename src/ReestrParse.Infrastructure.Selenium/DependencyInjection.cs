using Microsoft.Extensions.DependencyInjection;
using ReestrParse.Application.Catalog;
using ReestrParse.Infrastructure.Selenium.Browser;
using ReestrParse.Infrastructure.Selenium.Eias;

namespace ReestrParse.Infrastructure.Selenium;

public static class DependencyInjection
{
    public static IServiceCollection AddReestrParseSelenium(this IServiceCollection services)
    {
        services.AddSingleton<SeleniumBrowserSession>();
        services.AddSingleton<IEiasCatalogService, EiasCatalogService>();
        return services;
    }
}
