using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace FutureNews.App.Scheduling;

/// <summary>
/// Registers (and removes) a Windows Task Scheduler entry that runs the headless
/// fetch command once a day, so updates still happen when the tray app is not running.
/// </summary>
public static class TaskSchedulerRegistrar
{
    public const string TaskName = "FutureNews Daily Update";

    public static bool IsRegistered()
    {
        try
        {
            var (success, _) = RunSchtasks($"/Query /TN \"{TaskName}\"");
            return success;
        }
        catch
        {
            return false;
        }
    }

    public static (bool Success, string Message) Register(TimeSpan timeOfDay, string executablePath)
    {
        var time = timeOfDay.ToString(@"hh\:mm");
        var args = $"/Create /TN \"{TaskName}\" /TR \"\\\"{executablePath}\\\" --fetch --silent\" /SC DAILY /ST {time} /F /RL LIMITED";
        try
        {
            var (success, output) = RunSchtasks(args);
            return (success, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static (bool Success, string Message) Unregister()
    {
        try
        {
            var (success, output) = RunSchtasks($"/Delete /TN \"{TaskName}\" /F");
            return (success, output);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static (bool Success, string Output) RunSchtasks(string arguments)
    {
        var psi = new ProcessStartInfo("schtasks.exe", arguments)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(20_000);

        var output = (stdout + "\n" + stderr).Trim();
        var success = process.ExitCode == 0 &&
                      (output.Contains("SUCCESS", StringComparison.OrdinalIgnoreCase) ||
                       output.Contains("成功", StringComparison.Ordinal));
        return (success, output);
    }

    /// <summary>Opens the app's data folder in Explorer.</summary>
    public static void OpenDataFolder(string path)
    {
        if (!Directory.Exists(path)) Directory.CreateDirectory(path);
        Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
    }

    [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool ShellExecute(IntPtr hwnd, string lpOperation, string lpFile, string lpParameters,
        string lpDirectory, int nShowCmd);
}
