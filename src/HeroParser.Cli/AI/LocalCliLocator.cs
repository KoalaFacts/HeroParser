using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace HeroParser.Cli.AI;

/// <summary>
/// Finds the executable for a locally installed AI CLI.
/// </summary>
/// <remarks>
/// Provider selection and process execution both need to know whether a given command
/// exists and where it lives, so the lookup lives here rather than in either of them.
/// Paths are built with <see cref="Path.Join(string, string)"/> rather than
/// <see cref="Path.Combine(string, string)"/>: Combine discards everything before a
/// rooted segment, so a command name that happened to be absolute would silently escape
/// the directory being probed.
/// </remarks>
internal readonly struct LocalCliLocator(string command)
{
    /// <summary>Extensions a command may carry on disk, in probe order.</summary>
    private static readonly string[] executableExtensions = ["", ".exe", ".cmd", ".bat", ".lnk"];

    /// <summary>
    /// Returns whether the command is resolvable through PATH.
    /// </summary>
    public bool IsAvailable()
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv)) return false;

        string target = command;
        return pathEnv.Split(Path.PathSeparator)
            .SelectMany(directory => executableExtensions.Select(ext => Path.Join(directory.Trim(), target + ext)))
            .Any(File.Exists);
    }

    /// <summary>
    /// Returns a full path to the command, or the command name itself when
    /// it is on PATH or cannot be located in a known install directory.
    /// </summary>
    public string ResolvePath()
    {
        if (IsAvailable())
        {
            return command;
        }

        return KnownInstallPaths().FirstOrDefault(File.Exists) ?? command;
    }

    /// <summary>
    /// Returns the well-known install locations for the command, for the case where its
    /// installer did not put it on PATH.
    /// </summary>
    private IEnumerable<string> KnownInstallPaths()
    {
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);

        if (command.Equals("agy", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(localAppData, "agy", "bin", "agy.exe");
        }
        else if (command.Equals("claude", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(userProfile, ".local", "bin", "claude.exe");
        }
        else if (command.Equals("copilot", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(appData, "npm", "copilot.cmd");
            yield return Path.Join(appData, "npm", "copilot.ps1");
            yield return Path.Join(localAppData, "Microsoft", "WindowsApps", "copilot.exe");
        }
        else if (command.Equals("codex", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(localAppData, "Programs", "codex.exe");
            yield return Path.Join(userProfile, ".local", "bin", "codex.exe");
        }
        else if (command.Equals("openai", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(localAppData, "Programs", "openai.exe");
            yield return Path.Join(userProfile, ".local", "bin", "openai.exe");
        }
        else if (command.Equals("ollama", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Join(localAppData, "Programs", "Ollama", "ollama.exe");
        }
    }
}
