using System.Security;
using Microsoft.Win32;

namespace AudioTunePro.App.Services;

/// <summary>Registers/unregisters AudioTune Pro to launch at Windows sign-in via the per-user Run key.</summary>
public sealed class StartupRegistrationService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "AudioTunePro";

    /// <summary>Returns false (instead of throwing) if the registry can't be written, e.g. locked down by policy.</summary>
    public bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, writable: true)
                ?? Registry.CurrentUser.CreateSubKey(RunKeyPath);

            if (enabled)
            {
                var exePath = Environment.ProcessPath ?? Environment.GetCommandLineArgs()[0];
                key.SetValue(ValueName, $"\"{exePath}\" --minimized");
            }
            else
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
            }
            return true;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Keeps an existing launch-at-sign-in entry pointing at a copy that exists, without letting whichever copy
    /// happens to be launched (a Downloads folder, a build output) silently take over autostart: the entry is
    /// rewritten only if the registered exe is gone, or if this is the installed copy under Program Files.
    /// </summary>
    public void RepairIfStale()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            var registered = ExtractExePath(key?.GetValue(ValueName) as string);
            var current = Environment.ProcessPath;
            if (registered is null || current is null) return;
            if (string.Equals(registered, current, StringComparison.OrdinalIgnoreCase)) return;

            if (!System.IO.File.Exists(registered) || IsUnderProgramFiles(current)) SetEnabled(true);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
        {
            // Nothing to repair if the registry can't be read.
        }
    }

    private static string? ExtractExePath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command)) return null;
        command = command.Trim();
        if (command[0] != '"') return command.Split(' ', 2)[0];
        var end = command.IndexOf('"', 1);
        return end > 1 ? command[1..end] : null;
    }

    private static bool IsUnderProgramFiles(string path)
    {
        foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
        {
            var root = Environment.GetFolderPath(folder);
            if (root.Length > 0 && path.StartsWith(root + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath);
            return key?.GetValue(ValueName) is not null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException or System.IO.IOException)
        {
            return false;
        }
    }
}
