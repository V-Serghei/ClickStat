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

        var mouseMonitor = ServiceProvider.GetRequiredService<IMouseMonitorService>();
        var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
        mouseMonitor.InitializeRawInput(hwnd);
    }

    private static void ConfigureServices(IServiceCollection s)
    {
        // Core infrastructure
        s.AddSingleton<IStartupService, StartupService>();
        s.AddSingleton<ITrayService, TrayService>();
        s.AddSingleton<IInputMonitorService, InputMonitorService>();
        s.AddSingleton<ISavingClick, SavingClickService>();
        s.AddSingleton<IGetDataClick, GetDataClickService>();

        // Mouse
        s.AddSingleton<IMouseMonitorService, MouseMonitorService>();
        s.AddSingleton<IMouseStatisticsService, MouseStatisticsService>();

        // New processors
        s.AddSingleton<WordProcessor>();
        s.AddSingleton<HourlyActivityProcessor>();
        s.AddSingleton<AppUsageProcessor>();
        s.AddSingleton<MouseDataProcessor>();  // shared instance for ActivityViewModel

        // Live bus (UI thread)
        s.AddSingleton(sp => new LiveEventBus(System.Windows.Application.Current.Dispatcher));

        // Break reminder (DispatcherTimer — must be on UI thread at creation)
        s.AddSingleton<BreakReminderService>();

        // ViewModels
        s.AddTransient<OverviewViewModel>();
        s.AddTransient<KeyboardViewModel>();
        s.AddTransient<MouseViewModel>();
        s.AddTransient<ActivityViewModel>();
        s.AddTransient<WordsViewModel>();
        s.AddTransient<AppsViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<MainWindow>();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        var key   = ServiceProvider.GetRequiredService<ISavingClick>();
        if (key is SavingClickService ks) ks.OnApplicationExitAsync().Wait();

        ServiceProvider.GetRequiredService<IMouseStatisticsService>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<WordProcessor>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<HourlyActivityProcessor>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<AppUsageProcessor>().OnApplicationExitAsync().Wait();
    }
}
