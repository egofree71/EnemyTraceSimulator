using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

/// <summary>
/// Launches MAME directly from the Godot tool by using a JSON configuration file.
/// The launcher replaces the old .bat wrapper: it resolves paths, writes the small Lua runtime config,
/// and starts mame.exe with the required command-line arguments.
/// </summary>
public static class MameTraceLauncher
{
    private const string RuntimeConfigFileName = "ladybug_sequence_runtime_config.lua";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    public static async Task<MameTraceLaunchResult> LaunchAsync(string configPath)
    {
        var result = new MameTraceLaunchResult();
        string configAbsolutePath = ResolvePath(configPath);

        if (!File.Exists(configAbsolutePath))
            throw new FileNotFoundException("MAME trace config file not found.", configAbsolutePath);

        string json = await File.ReadAllTextAsync(configAbsolutePath).ConfigureAwait(false);
        MameTraceSettings settings = JsonSerializer.Deserialize<MameTraceSettings>(json, JsonOptions)
            ?? throw new InvalidOperationException("MAME trace config is empty or invalid.");

        string mameExecutable = ResolvePath(settings.MameExecutable);
        string romPath = ResolvePath(settings.RomPath);
        string stateDirectory = ResolvePath(settings.StateDirectory);
        string luaScriptPath = ResolvePath(settings.LuaScriptPath);
        string outputDirectory = ResolvePath(settings.OutputDirectory);

        string scriptDirectory = Path.GetDirectoryName(luaScriptPath)
            ?? throw new InvalidOperationException("Could not resolve Lua script directory.");

        Directory.CreateDirectory(outputDirectory);
        Directory.CreateDirectory(scriptDirectory);

        if (!File.Exists(mameExecutable))
            throw new FileNotFoundException("mame.exe not found. Update config/mame_trace_settings.json.", mameExecutable);

        if (!Directory.Exists(romPath))
            throw new DirectoryNotFoundException($"ROM directory not found: {romPath}");

        if (!File.Exists(luaScriptPath))
            throw new FileNotFoundException("Lua trace script not found.", luaScriptPath);

        string saveState = NormalizeSaveStateName(settings.SaveState, result.Messages);
        string expectedStatePath = Path.Combine(stateDirectory, settings.StateSubdir, saveState + ".sta");
        if (!File.Exists(expectedStatePath))
        {
            result.Messages.Add("WARNING: save-state not found where expected:");
            result.Messages.Add("  " + expectedStatePath);
            result.Messages.Add("MAME may still find it depending on its own configuration, but check this path if load fails.");
        }

        string runtimeConfigPath = Path.Combine(scriptDirectory, RuntimeConfigFileName);
        await File.WriteAllTextAsync(
            runtimeConfigPath,
            BuildLuaRuntimeConfig(settings, saveState, outputDirectory),
            Encoding.UTF8).ConfigureAwait(false);

        result.Messages.Add("Runtime Lua config written:");
        result.Messages.Add("  " + runtimeConfigPath);

        var startInfo = new ProcessStartInfo
        {
            FileName = mameExecutable,
            WorkingDirectory = scriptDirectory,
            UseShellExecute = false,
            CreateNoWindow = false
        };

        startInfo.ArgumentList.Add(settings.Game);

        if (settings.Windowed)
            startInfo.ArgumentList.Add("-window");

        startInfo.ArgumentList.Add("-rompath");
        startInfo.ArgumentList.Add(romPath);
        startInfo.ArgumentList.Add("-state_directory");
        startInfo.ArgumentList.Add(stateDirectory);
        startInfo.ArgumentList.Add("-statename");
        startInfo.ArgumentList.Add(settings.StateSubdir);
        startInfo.ArgumentList.Add("-autoboot_script");
        startInfo.ArgumentList.Add(luaScriptPath);
        startInfo.ArgumentList.Add("-autoboot_delay");
        startInfo.ArgumentList.Add(Math.Max(0, settings.AutobootDelay).ToString());

        result.CommandPreview = BuildCommandPreview(startInfo);
        result.Messages.Add("Launching MAME:");
        result.Messages.Add("  " + result.CommandPreview);

        int exitCode = await Task.Run(() =>
        {
            using Process process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Failed to start MAME process.");

            process.WaitForExit();
            return process.ExitCode;
        }).ConfigureAwait(false);

        result.ExitCode = exitCode;
        result.InitialSnapshotPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_initial_snapshot.json");
        result.TracePath = Path.Combine(outputDirectory, settings.OutputPrefix + "_trace.jsonl");
        result.SummaryPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_summary.txt");

        result.Messages.Add($"MAME exited with code {exitCode}.");
        result.Messages.Add("Expected trace output:");
        result.Messages.Add("  " + result.TracePath);
        result.Messages.Add("Expected initial snapshot:");
        result.Messages.Add("  " + result.InitialSnapshotPath);

        return result;
    }

    public static string ToDisplayPath(string absolutePath)
    {
        string projectRoot = NormalizeSeparators(ProjectSettings.GlobalizePath("res://"));
        string normalizedPath = NormalizeSeparators(Path.GetFullPath(absolutePath));

        if (!projectRoot.EndsWith('/'))
            projectRoot += "/";

        if (normalizedPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
            return "res://" + normalizedPath[projectRoot.Length..];

        return absolutePath;
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        if (path.StartsWith("res://", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("user://", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectSettings.GlobalizePath(path);
        }

        if (Path.IsPathRooted(path))
            return Path.GetFullPath(path);

        string projectRoot = ProjectSettings.GlobalizePath("res://");
        return Path.GetFullPath(Path.Combine(projectRoot, path));
    }

    private static string NormalizeSaveStateName(string saveState, List<string> messages)
    {
        string normalized = saveState.Trim();

        if (normalized.EndsWith(".sta", StringComparison.OrdinalIgnoreCase))
        {
            normalized = Path.GetFileNameWithoutExtension(normalized);
            messages.Add($"Save-state name normalized to '{normalized}' because MAME Lua expects the name without .sta.");
        }

        return normalized;
    }

    private static string BuildLuaRuntimeConfig(MameTraceSettings settings, string saveState, string outputDirectory)
    {
        return $$"""
return {
    save_state = "{{LuaString(saveState)}}",
    frames_after_tick0_to_capture = {{Math.Max(0, settings.FramesAfterTick0)}},
    output_prefix = "{{LuaString(settings.OutputPrefix)}}",
    output_dir = "{{LuaString(NormalizeSeparators(outputDirectory))}}",
    exit_when_done = {{LuaBool(settings.ExitWhenDone)}},
    pause_when_done = {{LuaBool(settings.PauseWhenDone)}},
    flush_every_trace_line = {{LuaBool(settings.FlushEveryTraceLine)}},
    include_full_memory_each_frame = {{LuaBool(settings.IncludeFullMemoryEachFrame)}},
    include_logical_maze_each_frame = {{LuaBool(settings.IncludeLogicalMazeEachFrame)}}
}
""";
    }

    private static string LuaBool(bool value) => value ? "true" : "false";

    private static string LuaString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\r", "\\r")
            .Replace("\n", "\\n");
    }

    private static string NormalizeSeparators(string path) => path.Replace('\\', '/');

    private static string BuildCommandPreview(ProcessStartInfo startInfo)
    {
        var parts = new List<string> { Quote(startInfo.FileName) };
        foreach (string arg in startInfo.ArgumentList)
            parts.Add(Quote(arg));
        return string.Join(" ", parts);
    }

    private static string Quote(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        return value.Contains(' ') || value.Contains('\t')
            ? "\"" + value.Replace("\"", "\\\"") + "\""
            : value;
    }
}

public sealed class MameTraceLaunchResult
{
    public int ExitCode { get; set; }
    public string CommandPreview { get; set; } = string.Empty;
    public string TracePath { get; set; } = string.Empty;
    public string InitialSnapshotPath { get; set; } = string.Empty;
    public string SummaryPath { get; set; } = string.Empty;
    public List<string> Messages { get; } = new();
}
