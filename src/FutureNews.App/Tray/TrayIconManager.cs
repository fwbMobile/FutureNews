using System.Windows.Forms;
using FutureNews.Core.Models;
using FutureNews.Core.Pipeline;

namespace FutureNews.App.Tray;

/// <summary>
/// Owns the system-tray icon: context menu actions, balloon notifications for the daily
/// digest and the "run at startup" registration.
/// </summary>
public sealed class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private bool _disposed;

    public TrayIconManager()
    {
        _statusItem = new ToolStripMenuItem("就绪") { Enabled = false };
        var openItem = new ToolStripMenuItem("打开主界面", null, (_, _) => OpenRequested?.Invoke());
        var refreshItem = new ToolStripMenuItem("立即更新", null, (_, _) => RefreshRequested?.Invoke());
        var settingsItem = new ToolStripMenuItem("设置…", null, (_, _) => SettingsRequested?.Invoke());
        var exitItem = new ToolStripMenuItem("退出", null, (_, _) => ExitRequested?.Invoke());

        var menu = new ContextMenuStrip();
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openItem);
        menu.Items.Add(refreshItem);
        menu.Items.Add(settingsItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        menu.Opening += (_, _) => menu.Items[2].Enabled = true;

        _notifyIcon = new NotifyIcon
        {
            Icon = LoadIcon(),
            Visible = true,
            Text = "FutureNews — 科技前沿每日简报",
            ContextMenuStrip = menu,
        };

        _notifyIcon.DoubleClick += (_, _) => OpenRequested?.Invoke();
    }

    public event Action? OpenRequested;
    public event Action? RefreshRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public void SetStatus(string text) => _statusItem.Text = text;

    /// <summary>Shows a balloon notification with the digest summary.</summary>
    public void Notify(string title, string message, bool silent = false)
    {
        try
        {
            var icon = silent ? ToolTipIcon.Info : ToolTipIcon.Info;
            _notifyIcon.BalloonTipTitle = title;
            _notifyIcon.BalloonTipText = message;
            _notifyIcon.BalloonTipIcon = icon;
            _notifyIcon.ShowBalloonTip(8000);
        }
        catch (Exception)
        {
            // Notifications are best-effort; never let them break a run.
        }
    }

    /// <summary>Builds the notification body for a digest result.</summary>
    public static string BuildDigestMessage(DigestResult result)
    {
        if (result.Articles.Count == 0)
        {
            return result.Errors.Count > 0
                ? "本次没有抓取到新内容。\n" + string.Join("\n", result.Errors.Take(3))
                : "今天暂时没有符合订阅条件的新进展。";
        }

        var lines = result.Articles
            .Take(6)
            .Select(a => $"• {a.OneLiner ?? a.Title}")
            .ToList();

        if (result.Articles.Count > 6) lines.Add($"…以及另外 {result.Articles.Count - 6} 条");

        var topicCounts = result.Articles
            .SelectMany(a => a.Topics)
            .GroupBy(t => t)
            .Select(g => $"{g.Count()} {g.Key}")
            .Take(5);

        lines.Add($"\n共 {result.Articles.Count} 条 · {string.Join(" / ", topicCounts)}");
        return string.Join("\n", lines);
    }

    private static System.Drawing.Icon LoadIcon()
    {
        var exePath = Environment.ProcessPath;
        if (!string.IsNullOrEmpty(exePath))
        {
            try
            {
                var icon = System.Drawing.Icon.ExtractAssociatedIcon(exePath);
                if (icon is not null) return icon;
            }
            catch
            {
                // fall through to a generated icon
            }
        }

        // A simple generated glyph: a filled circle in the accent colour.
        using var bitmap = new System.Drawing.Bitmap(32, 32);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(System.Drawing.Color.Transparent);
        using var brush = new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x4F, 0x8C, 0xFF));
        graphics.FillEllipse(brush, 3, 3, 26, 26);
        return System.Drawing.Icon.FromHandle(bitmap.GetHicon());
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
