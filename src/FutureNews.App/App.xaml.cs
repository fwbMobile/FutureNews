using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using FutureNews.App.Logging;
using FutureNews.App.Tray;
using FutureNews.Core;
using FutureNews.Core.Models;
using FutureNews.Core.Pipeline;
using FutureNews.Core.Scheduling;
using FutureNews.Core.Topics;
using Application = System.Windows.Application;

namespace FutureNews.App;

public partial class App : Application
{
    private AppServices? _services;
    private TrayIconManager? _tray;
    private DailyScheduler? _scheduler;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        AppPaths.EnsureCreated();

        var args = e.Args ?? Array.Empty<string>();

        // Headless mode used by the Windows scheduled task and for scripting.
        if (args.Any(a => a.Equals("--fetch", StringComparison.OrdinalIgnoreCase)))
        {
            RunHeadless(args);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            Log.Error("未处理的界面异常", ex.Exception);
            var details = ex.Exception.ToString();
            var inner = ex.Exception.InnerException;
            while (inner is not null)
            {
                details += Environment.NewLine + "--- inner ---" + Environment.NewLine + inner.ToString();
                inner = inner.InnerException;
            }
            Log.Write(details);
            MessageBox.Show($"发生错误：{ex.Exception.Message}", "FutureNews", MessageBoxButton.OK, MessageBoxImage.Error);
        };

        _services = new AppServices();
        _tray = new TrayIconManager();
        _tray.OpenRequested += () => Dispatcher.Invoke(ShowMainWindow);
        _tray.RefreshRequested += () => Dispatcher.Invoke(() => (MainWindow as MainWindow)?.StartUpdate());
        _tray.SettingsRequested += () => Dispatcher.Invoke(() => (MainWindow as MainWindow)?.OpenSettings());
        _tray.ExitRequested += () => Dispatcher.Invoke(ShutdownApp);

        StartScheduler();

        // --tray starts the app in the notification area only (used by the startup launcher).
        var startHidden = args.Any(a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow(_services, _tray);
        FitToWorkingArea(window);
        MainWindow = window;
        if (!startHidden)
        {
            window.Show();
        }

        if (args.Any(a => a.Equals("--settings", StringComparison.OrdinalIgnoreCase)))
        {
            window.OpenSettings();
        }
    }

    /// <summary>
    /// Keeps the window inside the usable desktop area. On high-DPI displays the logical
    /// window size can exceed the physical screen, which would clip the layout.
    /// </summary>
    private static void FitToWorkingArea(Window window)
    {
        try
        {
            var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea
                       ?? System.Drawing.Rectangle.Empty;
            if (area.Width <= 0) return;

            var scale = GetScreenScale();
            var maxWidth = area.Width / scale - 12;
            var maxHeight = area.Height / scale - 12;

            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, maxWidth));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, maxHeight));
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            Log.Write($"窗口尺寸：屏幕 {area.Width}x{area.Height}px，缩放 {scale:F2}，工作区上限 {maxWidth:F0}x{maxHeight:F0} DIP，窗口 {window.Width:F0}x{window.Height:F0} DIP");
        }
        catch (Exception)
        {
            // fall back to the declared size
        }
    }

    /// <summary>The desktop's DPI scale (1.0 on a 96 DPI display, 2.0 at 200% scaling).</summary>
    private static double GetScreenScale()
    {
        try
        {
            var dpi = GetDpiForSystem();
            if (dpi > 48) return dpi / 96.0;
        }
        catch (Exception)
        {
            // fall through to the GDI fallback
        }

        try
        {
            using var graphics = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            var gdiDpi = graphics.DpiX;
            return gdiDpi > 48 ? gdiDpi / 96.0 : 1.0;
        }
        catch (Exception)
        {
            return 1.0;
        }
    }

    [DllImport("user32.dll")]
    private static extern uint GetDpiForSystem();

    private void ShowMainWindow()
    {
        if (MainWindow is null) return;
        MainWindow.Show();
        MainWindow.WindowState = WindowState.Normal;
        MainWindow.Activate();
    }

    private void ShutdownApp()
    {
        _scheduler?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        Shutdown();
    }

    private void StartScheduler()
    {
        var settings = _services!.Settings.Load();
        var time = ParseTime(settings.UpdateTime);
        _scheduler = new DailyScheduler(time, async ct =>
        {
            var result = await _services.Pipeline.RunAsync(ct).ConfigureAwait(false);
            if (settings.Notify.Enabled && result.Articles.Count > 0)
            {
                _tray?.Notify(
                    $"FutureNews · {result.Articles.Count} 条新进展",
                    TrayIconManager.BuildDigestMessage(result));
            }
        }, message => Log.Write(message));

        if (settings.AutoUpdateEnabled) _scheduler.Start();
    }

    public static TimeSpan ParseTime(string value)
    {
        return TimeSpan.TryParse(value, out var parsed) ? parsed : new TimeSpan(8, 30, 0);
    }

    /// <summary>Headless update used by the scheduled task; prints a report when asked.</summary>
    private void RunHeadless(string[] args)
    {
        var silent = args.Any(a => a.Equals("--silent", StringComparison.OrdinalIgnoreCase));
        var verbose = args.Any(a => a.Equals("--console", StringComparison.OrdinalIgnoreCase));

        if (verbose) AttachToParentConsole();

        try
        {
            using var services = new AppServices();
            ApplyCliTopicSelection(services, args);

            var settings = services.Settings.Load();
            var result = services.Pipeline.RunAsync(CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

            if (verbose)
            {
                try
                {
                    SetConsoleOutputCP(65001);
                    Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                }
                catch (Exception)
                {
                    // console encoding is best-effort
                }

                Console.WriteLine($"=== FutureNews {result.Date} ===");
                Console.WriteLine($"抓取 {result.Fetched} 条，保留 {result.Persisted} 条，去重 {result.Duplicate} 条，过滤 {result.Filtered} 条，用时 {result.Duration.TotalSeconds:F1}s");
                if (result.SourceCounts.Count > 0)
                {
                    Console.WriteLine("来源分布：" + string.Join("  ", result.SourceCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));
                }
                if (result.TopicCounts.Count > 0)
                {
                    Console.WriteLine("领域命中：" + string.Join("  ", result.TopicCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}(保留{result.KeptPerTopic.GetValueOrDefault(kv.Key)})")));
                }
                if (result.Summary is not null)
                {
                    Console.WriteLine($"摘要：LLM {result.Summary.LlmCount} 条，本地抽取 {result.Summary.FallbackCount} 条{(result.Summary.Error is null ? "" : "（" + result.Summary.Error + "）")}");
                }
                foreach (var article in result.Articles)
                {
                    Console.WriteLine();
                    Console.WriteLine($"[{string.Join("/", article.Topics)}] {article.Title}");
                    Console.WriteLine($"  {article.OneLiner}");
                    Console.WriteLine($"  {article.SourceName} · {article.PublishedAt.ToLocalTime():MM-dd HH:mm} · {article.Url}");
                }
                foreach (var error in result.Errors) Console.WriteLine("  ! " + error);
            }

            var reportIndex = Array.FindIndex(args, a => a.Equals("--report", StringComparison.OrdinalIgnoreCase));
            if (reportIndex >= 0 && reportIndex + 1 < args.Length)
            {
                WriteReport(args[reportIndex + 1], result);
            }

            Log.Write($"无头更新完成：抓取 {result.Fetched}，保留 {result.Persisted}，用时 {result.Duration.TotalSeconds:F1}s");

            if (!silent && settings.Notify.Enabled && result.Articles.Count > 0)
            {
                using var tray = new TrayIconManager();
                tray.Notify($"FutureNews · {result.Articles.Count} 条新进展", TrayIconManager.BuildDigestMessage(result));
                Thread.Sleep(1500);
            }
        }
        catch (Exception ex)
        {
            Log.Error("无头更新失败", ex);
            if (verbose) Console.WriteLine("运行失败: " + ex.Message);
        }
    }

    /// <summary>Writes a human-readable digest report as UTF-8.</summary>
    private static void WriteReport(string path, DigestResult result)
    {
        try
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var lines = new List<string>
            {
                $"FutureNews 每日简报 · {result.Date}",
                $"抓取 {result.Fetched} 条 → 保留 {result.Persisted} 条（去重 {result.Duplicate}，过滤 {result.Filtered}），用时 {result.Duration.TotalSeconds:F1} 秒",
                "",
            };

            if (result.SourceCounts.Count > 0)
                lines.Add("来源分布：" + string.Join("  ", result.SourceCounts.OrderBy(kv => kv.Key).Select(kv => $"{kv.Key}={kv.Value}")));

            foreach (var article in result.Articles)
            {
                lines.Add("");
                lines.Add($"【{string.Join(" / ", article.Topics)}】{article.Title}");
                lines.Add($"  {article.OneLiner}");
                lines.Add($"  {article.SourceName} · {article.PublishedAt.ToLocalTime():yyyy-MM-dd HH:mm} · {article.Url}");
            }

            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex)
        {
            Log.Error("写入简报报告失败", ex);
        }
    }

    /// <summary>Enables the given topic ids from the command line, e.g. --enable fusion,space.</summary>
    private static void ApplyCliTopicSelection(AppServices services, string[] args)
    {
        var index = Array.FindIndex(args, a => a.Equals("--enable", StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return;

        var requested = args[index + 1].Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim().ToLowerInvariant())
            .ToHashSet();

        if (requested.Contains("all")) requested = TopicCatalog.All.Select(t => t.Id).ToHashSet();

        foreach (var topic in TopicCatalog.All)
        {
            var subscription = services.Repository.GetSubscriptions().FirstOrDefault(s => s.TopicId == topic.Id)
                ?? new Subscription { TopicId = topic.Id };
            subscription.Enabled = requested.Contains(topic.Id);
            services.Repository.SaveSubscription(subscription);
        }
    }

    /// <summary>Attaches to the parent console so headless runs can print a report.</summary>
    private static void AttachToParentConsole()
    {
        try { SetConsoleOutputCP(65001); } catch (Exception) { }
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) return;
        try
        {
            SetConsoleOutputCP(65001);
            var stdout = Console.OpenStandardOutput();
            var writer = new StreamWriter(stdout) { AutoFlush = true };
            Console.SetOut(writer);
        }
        catch (Exception)
        {
            // console output is optional
        }
    }

    private const int ATTACH_PARENT_PROCESS = -1;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetConsoleOutputCP(uint codePage);

    protected override void OnExit(ExitEventArgs e)
    {
        _scheduler?.Dispose();
        _tray?.Dispose();
        _services?.Dispose();
        base.OnExit(e);
    }
}
