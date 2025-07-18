using System.Windows;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Presentation;
using ClickStat.Presentation.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace ClickStat.App;

public partial class App : Application
{
    private static IServiceProvider ServiceProvider { get; set; } = null!;

    public App()
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        ViewModelLocator.Current.ServiceProvider = ServiceProvider;
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IInputMonitorService, InputMonitorService>();
        services.AddTransient<MainViewModel>();
        services.AddTransient<MainWindow>();
    }
}