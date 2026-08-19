using System;
using System.Collections.Generic;
using System.IO;

namespace HeroParser.Cli.AI;

/// <summary>
/// Finds the executable for a locally installed AI CLI.
/// </summary>
/// <remarks>
/// Provider selection and process execution both need to know whether a given command
/// exists and where it lives, so the lookup lives here rather than in either of them.
/// </remarks>
internal static class LocalCliLocator
{
    /// <summary>
    /// Returns whether <paramref name="command"/> is resolvable through PATH.
    /// </summary>
    public static bool IsCommandAvailable(string command)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        var extensions = new[] { "", ".exe", ".cmd", ".bat", ".lnk" };
        var paths = pathEnv.Split(Path.PathSeparator);
        foreach (var path in paths)
        {
            try
            {
                foreach (var ext in extensions)
                {
                    var fullPath = Path.Combine(path.Trim(), command + ext);
                    if (File.Exists(fullPath)) return true;
                }
            }
            catch (ArgumentException)
            {
                // A PATH entry with invalid path characters: skip it rather than fail the lookup.
            }
        }
        return false;
    }

    /// <summary>
    /// Returns a full path to <paramref name="command"/>, or the command name itself when
    /// it is on PATH or cannot be located in a known install directory.
    /// </summary>
    public static string ResolveCommandPath(string command)
    {
        if (IsCommandAvailable(command))
        {
            return command;
        }

        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        var candidatePaths = new List<string>();

        if (command.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "agy", "bin", "agy.exe"));
        }
        else if (command.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "claude.exe"));
        }
        else if (command.Equals("copilot", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(appData, "npm", "copilot.cmd"));
            candidatePaths.Add(Path.Combine(appData, "npm", "copilot.ps1"));
            candidatePaths.Add(Path.Combine(localAppData, "Microsoft", "WindowsApps", "copilot.exe"));
        }
        else if (command.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "Programs", "codex.exe"));
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "codex.exe"));
        }
        else if (command.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "Programs", "openai.exe"));
            candidatePaths.Add(Path.Combine(userProfile, ".local", "bin", "openai.exe"));
        }
        else if (command.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            candidatePaths.Add(Path.Combine(localAppData, "Programs", "Ollama", "ollama.exe"));
        }

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        return command;
    }
}
