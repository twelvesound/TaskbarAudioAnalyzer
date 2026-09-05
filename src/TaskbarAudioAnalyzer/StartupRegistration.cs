using System.IO;

namespace TaskbarAudioAnalyzer;

internal static class StartupRegistration
{
    internal static string CommandPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup), "TaskbarAudioAnalyzer.cmd");

    internal static void Enable()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(CommandPath)!);
        File.WriteAllText(CommandPath, GetCommandText());
    }

    internal static void Disable() => File.Delete(CommandPath);

    internal static void RefreshIfEnabled()
    {
        if (File.Exists(CommandPath))
            Enable();
    }

    private static string GetCommandText()
    {
        // A development build must not move startup back out of Program Files.
        var installedExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "TaskbarAudioAnalyzer", "TaskbarAudioAnalyzer.exe");
        var exePath = File.Exists(installedExe) ? installedExe : Environment.ProcessPath
            ?? throw new InvalidOperationException("The executable path is unavailable.");
        var commandPath = exePath.Replace("%", "%%", StringComparison.Ordinal);
        return $"@echo off{Environment.NewLine}if exist \"{commandPath}\" start \"\" \"{commandPath}\"{Environment.NewLine}";
    }
}
