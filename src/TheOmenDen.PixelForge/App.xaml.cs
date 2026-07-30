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
    /// <summary>Stamped on every event so a shared log sink can tell apps apart.</summary>
    private const string ApplicationName = "TheOmenDen.PixelForge";

    /// <summary>The date is filled in by <see cref="RollingInterval.Day"/> at the trailing dash.</summary>
    private const string RollingLogFileName = "pixelforge-.log";

    private const long RollingLogSizeLimitBytes = 32L * 1024 * 1024;

    private const int RetainedLogFileCount = 14;

    /// <summary>Not rolled by date: a crash is rare and wants to stay findable.</summary>
    private const string FatalLogFileName = "pixelforge-fatal.log";

    private const long FatalLogSizeLimitBytes = 8L * 1024 * 1024;

    private const int RetainedFatalLogFileCount = 3;

    /// <summary>Serilog.Expressions filter — <c>@l</c> is the event's level.</summary>
    private const string FatalOnly = "@l = 'Fatal'";

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
            .Enrich.WithProperty("Application", ApplicationName)
            // File sink is composed here rather than in appsettings.json because the
            // path is only resolvable at runtime (see AppPaths.Logs). Async wraps it so
            // disk writes never block the UI thread during a batch pipeline run.
            .WriteTo.Async(sink => sink.File(
                new CompactJsonFormatter(),
                Path.Combine(AppPaths.Logs.Value, RollingLogFileName),
                rollingInterval: RollingInterval.Day,
                rollOnFileSizeLimit: true,
                fileSizeLimitBytes: RollingLogSizeLimitBytes,
                retainedFileCountLimit: RetainedLogFileCount))
            // Fatals also go straight to disk, synchronously and unbuffered. A crash is
            // precisely when the async buffer above is lost: WinUI fails fast on an unhandled
            // XAML exception, which killed the process with the Fatal still in memory and left
            // no trace of why. This sink only ever sees Fatal events, so it costs nothing.
            .WriteTo.Conditional(
                FatalOnly,
                sink => sink.File(
                    new CompactJsonFormatter(),
                    Path.Combine(AppPaths.Logs.Value, FatalLogFileName),
                    rollOnFileSizeLimit: true,
                    fileSizeLimitBytes: FatalLogSizeLimitBytes,
                    retainedFileCountLimit: RetainedFatalLogFileCount)));

        builder.Services.AddSingleton<IThemeService, ThemeService>();
        builder.Services.AddSingleton<SourcePackService>();
        builder.Services.AddSingleton<CatalogService>();
        builder.Services.AddSingleton<RampService>();
        builder.Services.AddSingleton<PickerService>();
        // Singleton so its bounded cache and decode pump outlive navigation away from the page.
        builder.Services.AddSingleton<ThumbnailService>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<PaletteViewModel>();
        builder.Services.AddSingleton<BatchExportViewModel>();
        builder.Services.AddSingleton<AssetsViewModel>();

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

        // Explicit rather than left to SourcePackService.Load's Changed event: nothing has
        // resolved CatalogService yet at that point, so its subscription is not live and a first
        // run would show an empty picker until a pack path was re-picked.
        Services.GetRequiredService<CatalogService>().Rescan();

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
    {
        Log.Fatal(e.Exception, "Unhandled XAML exception: {Message}", e.Message);

        // WinUI fails fast on an unhandled XAML exception (0xC0000409), and Sinks.Async is
        // buffered — without this flush the Fatal above dies in memory and the crash leaves no
        // trace at all. Same reason OnDomainUnhandledException flushes.
        Log.CloseAndFlush();
    }

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
