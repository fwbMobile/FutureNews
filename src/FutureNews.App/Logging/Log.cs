using System.IO;
using FutureNews.Core;

namespace FutureNews.App.Logging;

/// <summary>Appends timestamped lines to the app log file.</summary>
public static class Log
{
    private static readonly object Sync = new();

    public static void Write(string message)
    {
        try
        {
            AppPaths.EnsureCreated();
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}";
            lock (Sync)
            {
                File.AppendAllText(AppPaths.LogPath, line + Environment.NewLine);
            }
        }
        catch (Exception)
        {
            // Logging must never throw.
        }
    }

    public static void Error(string message, Exception? ex = null) =>
        Write(ex is null ? message : $"{message}: {ex.Message}");
}
