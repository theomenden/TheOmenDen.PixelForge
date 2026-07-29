using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using TheOmenDen.PixelForge.Services;
using TheOmenDen.PixelForge.ViewModels;
using Microsoft.UI.Xaml;
using Serilog;
using Serilog.Formatting.Compact;

namespace TheOmenDen.PixelForge;

/// <summary>
/// Provides application-specific behavior to supplement the default Application class.
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    private Window? _window;

    /// <summary>Composition root. Resolve dependencies from here in views and pages.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        // Stage 1: bootstrap logger. A WinUI startup crash surfaces nothing to the user
        // and no console exists yet, so anything thrown while building the host below
        // would be invisible without this.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug(formatProvider: CultureInfo.InvariantCulture)
            .CreateBootstrapLogger();

        InitializeComponent();

        UnhandledException += OnXamlUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        // Stage 2: replaces Log.Logger via AddSerilog.
        _host = BuildHost();
        Services = _host.Services;
    }

    private static IHost BuildHost()
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            // Default content root is the CWD, which is not the app directory for a
            // packaged launch — appsettings.json would not be found.
            ContentRootPath = AppContext.BaseDirectory,
        });

        builder.Services.AddSerilog((services, lc) => lc
            .ReadFrom.Configuration(builder.Configuration)
            .ReadFrom.Services(services)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "TheOmenDen.PixelForge")
            // File sink is composed here rather than in appsettings.json because the
            // path is only resolvable at runtime (see AppPaths.Logs). Async wraps it so
            // disk writes never block the UI thread during a batch pipeline run.
            .WriteTo.Async(sink => sink.File(
                new CompactJsonFormatter(),
                Path.Combine(AppPaths.Logs.Value, "pixelforge-.log"),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: 32L * 1024 * 1024,
                retainedFileCountLimit: 14)));

        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<SourcePackService>();
        builder.Services.AddSingleton<RampService>();
        builder.Services.AddSingleton<PickerService>();
        builder.Services.AddTransient<SettingsViewModel>();

        return builder.Build();
    }

    /// <summary>
    /// Invoked when the application is launched.
    /// </summary>
    /// <param name="args">Details about the launch request and process.</param>
    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Log.Information("PixelForge starting. Logs: {LogDirectory}", AppPaths.Logs);

        Services.GetRequiredService<SourcePackService>().Load();
        Services.GetRequiredService<RampService>().Load();

        _window = new MainWindow();
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        Log.Information("PixelForge shutting down");
        _host.Dispose();

        // Sinks.Async buffers in memory — without this the final events are lost.
        Log.CloseAndFlush();
    }

    private static void OnXamlUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
        => Log.Fatal(e.Exception, "Unhandled XAML exception: {Message}", e.Message);

    private static void OnDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled domain exception (terminating: {IsTerminating})", e.IsTerminating);
        Log.CloseAndFlush();
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}
