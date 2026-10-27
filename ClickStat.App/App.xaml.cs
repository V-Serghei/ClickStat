using System.Windows;
using System.Diagnostics;
using System.Windows.Threading;
using ClickStat.Core.Interfaces;
using ClickStat.Core.Services;
using ClickStat.Infrastructure.Data;
using ClickStat.Infrastructure.Diagnostics;
using ClickStat.Infrastructure.InputMonitoring;
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
        PerformanceLog.Record("event", "application.construct");
        _ = Task.Run(StartDiagnostics);
        using var timing = PerformanceLog.Measure("startup.configure_services");
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();
        ViewModelLocator.Current.ServiceProvider = ServiceProvider;
    }

    private void Application_Startup(object sender, StartupEventArgs e)
    {
        PerformanceLog.Record("event", "application.start");
        MainWindow mainWindow;
        using (PerformanceLog.Measure("startup.resolve_window"))
            mainWindow = ServiceProvider.GetRequiredService<MainWindow>();

        var trayService = ServiceProvider.GetRequiredService<ITrayService>();
        trayService.Initialize(mainWindow);
        mainWindow.Show();
        PerformanceLog.Record("event", "startup.window_shown");
        _ = Dispatcher.BeginInvoke(() =>
        {
            using var timing = PerformanceLog.Measure("startup.input_monitors");
            var hwnd = new System.Windows.Interop.WindowInteropHelper(mainWindow).Handle;
            ServiceProvider.GetRequiredService<IInputMonitorService>().InitializeRawInput(hwnd);
            ServiceProvider.GetRequiredService<MainViewModel>().StartMonitoring();
        }, DispatcherPriority.ContextIdle);
    }

    private static void StartDiagnostics()
    {
        try
        {
            string monitor = System.IO.Path.Combine(AppContext.BaseDirectory, "ClickStat.Diagnostics.exe");
            if (!System.IO.File.Exists(monitor))
            {
                PerformanceLog.Record("error", "diagnostics.missing");
                return;
            }

            using var current = Process.GetCurrentProcess();
            var start = new ProcessStartInfo(monitor)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            start.ArgumentList.Add(current.Id.ToString(System.Globalization.CultureInfo.InvariantCulture));
            start.ArgumentList.Add(current.StartTime.ToUniversalTime().Ticks.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Process.Start(start)?.Dispose();
            PerformanceLog.Record("event", "diagnostics.started");
        }
        catch (Exception ex)
        {
            PerformanceLog.Record("error", "diagnostics.launch." + ex.GetType().Name);
        }
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
        s.AddSingleton<InputTemplateProcessor>();
        s.AddSingleton<TypingTrainingProcessor>();
        s.AddSingleton<MouseDataProcessor>();  // shared instance for ActivityViewModel
        s.AddSingleton<GamepadMonitorService>();
        s.AddSingleton<AchievementService>();

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
        s.AddTransient<GamepadsViewModel>();
        s.AddTransient<TrainingProgressViewModel>();
        s.AddTransient<AchievementsViewModel>();
        s.AddTransient<SettingsViewModel>();
        s.AddSingleton<MainViewModel>();
        s.AddSingleton<MainWindow>();
    }

    private void Application_Exit(object sender, ExitEventArgs e)
    {
        PerformanceLog.Record("event", "application.exit");
        var key   = ServiceProvider.GetRequiredService<ISavingClick>();
        if (key is SavingClickService ks) ks.OnApplicationExitAsync().Wait();

        ServiceProvider.GetRequiredService<IMouseStatisticsService>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<WordProcessor>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<HourlyActivityProcessor>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<AppUsageProcessor>().OnApplicationExitAsync().Wait();
        ServiceProvider.GetRequiredService<GamepadMonitorService>().Dispose();
    }
}
