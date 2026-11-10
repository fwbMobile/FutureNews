namespace FutureNews.Core;

/// <summary>Well-known file locations used by the app.</summary>
public static class AppPaths
{
    private const string FolderName = "FutureNews";

    public static string DataDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), FolderName);

    public static string DatabasePath => Path.Combine(DataDirectory, "futurenews.db");

    public static string LogPath => Path.Combine(DataDirectory, "futurenews.log");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
    }
}
