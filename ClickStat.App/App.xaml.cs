using System.Windows;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Infrastructure.Data;
using ClickStat.Presentation;
using ClickStat.Presentation.Services;
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

        var trayService = ServiceProvider.GetRequiredService<ITrayService>();
        trayService.Initialize(mainWindow);
        mainWindow.Show();

        // Raw Input requires the HWND to exist — available after Show()
        var mouseMonitor = ServiceProvider.GetRequiredService<IMouseMonitorService>();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
        mouseMonitor.InitializeRawInput(hwnd);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Keyboard
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<ITrayService, TrayService>();
        services.AddSingleton<IInputMonitorService, InputMonitorService>();
        services.AddSingleton<ISavingClick, SavingClickService>();
        services.AddSingleton<IGetDataClick, GetDataClickService>();

        // Mouse
        services.AddSingleton<IMouseMonitorService, MouseMonitorService>();
        services.AddSingleton<IMouseStatisticsService, MouseStatisticsService>();

        // Live event bus (UI dispatcher captured at startup)
        services.AddSingleton(sp => new LiveEventBus(System.Windows.Application.Current.Dispatcher));

        // ViewModels
        services.AddTransient<StatisticsViewModel>();
        services.AddTransient<KeyboardViewModel>();
        services.AddTransient<MouseViewModel>();
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        var keyService = ServiceProvider.GetRequiredService<ISavingClick>();
        if (keyService is SavingClickService keySvc)
            keySvc.OnApplicationExitAsync().Wait();

        var mouseService = ServiceProvider.GetRequiredService<IMouseStatisticsService>();
        mouseService.OnApplicationExitAsync().Wait();
    }
}
