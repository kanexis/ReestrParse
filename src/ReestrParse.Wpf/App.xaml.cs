using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ReestrParse.Application;
using ReestrParse.Infrastructure.Selenium;
using ReestrParse.Wpf.ViewModels;

namespace ReestrParse.Wpf;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(System.Windows.StartupEventArgs e)
    {
        var builder = Host.CreateApplicationBuilder();

        builder.Services.AddReestrParseApplication();
        builder.Services.AddReestrParseSelenium();

        builder.Services.AddSingleton<MainWindowViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        _host.Services.GetRequiredService<MainWindow>().Show();

        base.OnStartup(e);
    }

    protected override async void OnExit(System.Windows.ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }

        base.OnExit(e);
    }
}
