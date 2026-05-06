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
    private const string DebugStartupScriptFileName = "ladybug_preferred_pc_debug_startup.cmd";
    private const string PreferredPcTraceScriptFileName = "ladybug_preferred_pc_trace.lua";
    private const string EnemyWorkPcTraceScriptFileName = "ladybug_enemywork_pc_trace.lua";

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

        bool preferredPcDiagnostic = IsPreferredPcDiagnosticScript(luaScriptPath);
        bool enemyWorkPcDiagnostic = IsEnemyWorkPcDiagnosticScript(luaScriptPath);
        bool exactPcDiagnostic = preferredPcDiagnostic || enemyWorkPcDiagnostic;
        bool enableDebugger = settings.EnableDebugger || exactPcDiagnostic;

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

        string? debugStartupScriptPath = null;
        if (enableDebugger)
        {
            debugStartupScriptPath = Path.Combine(scriptDirectory, DebugStartupScriptFileName);
            await File.WriteAllTextAsync(
                debugStartupScriptPath,
                BuildDebugStartupScript(),
                Encoding.ASCII).ConfigureAwait(false);

            result.Messages.Add("MAME debugger enabled.");
            result.Messages.Add("Debug startup script written:");
            result.Messages.Add("  " + debugStartupScriptPath);
            result.Messages.Add("This script sends 'g' so MAME does not remain stopped at the initial debugger break.");
        }

        string fallbackErrorLogPath = Path.Combine(scriptDirectory, "error.log");
        if (exactPcDiagnostic && File.Exists(fallbackErrorLogPath))
        {
            try
            {
                File.Delete(fallbackErrorLogPath);
                result.Messages.Add("Deleted previous diagnostic error.log:");
                result.Messages.Add("  " + fallbackErrorLogPath);
            }
            catch (Exception ex)
            {
                result.Messages.Add("WARNING: could not delete previous diagnostic error.log:");
                result.Messages.Add("  " + ex.Message);
            }
        }

        string preferredPcAnalysisPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_preferred_pc_analysis.txt");
        if (preferredPcDiagnostic && File.Exists(preferredPcAnalysisPath))
        {
            try
            {
                File.Delete(preferredPcAnalysisPath);
            }
            catch (Exception ex)
            {
                result.Messages.Add("WARNING: could not delete previous preferred PC analysis:");
                result.Messages.Add("  " + ex.Message);
            }
        }

        string enemyWorkPcAnalysisPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_enemywork_pc_analysis.txt");
        if (enemyWorkPcDiagnostic && File.Exists(enemyWorkPcAnalysisPath))
        {
            try
            {
                File.Delete(enemyWorkPcAnalysisPath);
            }
            catch (Exception ex)
            {
                result.Messages.Add("WARNING: could not delete previous EnemyWork PC analysis:");
                result.Messages.Add("  " + ex.Message);
            }
        }

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

        if (enableDebugger)
        {
            startInfo.ArgumentList.Add("-debug");

            if (exactPcDiagnostic)
            {
                // The breakpoint action uses logerror. With -log, MAME writes those
                // lines to error.log in the working directory. This is the most
                // reliable output for exact-PC diagnostics.
                startInfo.ArgumentList.Add("-log");
            }

            if (!string.IsNullOrWhiteSpace(debugStartupScriptPath))
            {
                startInfo.ArgumentList.Add("-debugscript");
                startInfo.ArgumentList.Add(debugStartupScriptPath);
            }
        }

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

        int watchdogMilliseconds = exactPcDiagnostic
            ? ComputeDiagnosticWatchdogMilliseconds(settings.FramesAfterTick0)
            : 0;

        if (preferredPcDiagnostic)
        {
            result.Messages.Add($"Preferred PC diagnostic watchdog enabled: MAME will be killed after about {watchdogMilliseconds / 1000.0:0.0} seconds of real time if it does not exit by itself.");
            result.Messages.Add("Tip: use framesAfterTick0=250..500 to stay in the one-active-enemy window.");
            result.Messages.Add("No -seconds_to_run is used because it can stop MAME before autoboot Lua/debugger setup completes.");
        }
        else if (enemyWorkPcDiagnostic)
        {
            result.Messages.Add($"EnemyWork PC diagnostic watchdog enabled: MAME will be killed after about {watchdogMilliseconds / 1000.0:0.0} seconds of real time if it does not exit by itself.");
            result.Messages.Add("Tip: use framesAfterTick0=250..600 for focused rejectedMask/fallbackMask captures.");
            result.Messages.Add("No -seconds_to_run is used because it can stop MAME before autoboot Lua/debugger setup completes.");
        }

        ProcessRunResult runResult = await Task.Run(() =>
            RunMameProcess(startInfo, watchdogMilliseconds)).ConfigureAwait(false);

        result.ExitCode = runResult.ExitCode;

        if (runResult.TimedOut)
        {
            result.Messages.Add("MAME watchdog timeout reached; process was killed intentionally after diagnostic capture time.");
        }

        if (preferredPcDiagnostic)
        {
            result.TracePath = string.Empty;
            result.InitialSnapshotPath = string.Empty;
            result.SummaryPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_preferred_pc_summary.txt");

            string hitLogPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_preferred_pc_hits.log");

            result.Messages.Add($"MAME exited with code {result.ExitCode}.");
            result.Messages.Add("Expected preferred[] exact-PC hit log, if Lua drained debugger.errorlog:");
            result.Messages.Add("  " + hitLogPath);
            result.Messages.Add("Expected preferred[] exact-PC summary, if Lua finish() ran:");
            result.Messages.Add("  " + result.SummaryPath);
            result.Messages.Add("Primary MAME error.log, because -log is enabled for this diagnostic:");
            result.Messages.Add("  " + fallbackErrorLogPath);
            result.Messages.Add("For this diagnostic, error.log is the most reliable raw output.");

            await TryBuildPreferredPcAnalysisAsync(
                fallbackErrorLogPath,
                preferredPcAnalysisPath,
                result.Messages).ConfigureAwait(false);

            result.Messages.Add("This diagnostic output is not a JSONL frame trace and should not be loaded with Charger trace.");
        }
        else if (enemyWorkPcDiagnostic)
        {
            result.TracePath = string.Empty;
            result.InitialSnapshotPath = string.Empty;
            result.SummaryPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_enemywork_pc_summary.txt");

            string hitLogPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_enemywork_pc_hits.log");

            result.Messages.Add($"MAME exited with code {result.ExitCode}.");
            result.Messages.Add("Expected EnemyWork exact-PC hit log, if Lua drained debugger.errorlog:");
            result.Messages.Add("  " + hitLogPath);
            result.Messages.Add("Expected EnemyWork exact-PC summary, if Lua finish() ran:");
            result.Messages.Add("  " + result.SummaryPath);
            result.Messages.Add("Primary MAME error.log, because -log is enabled for this diagnostic:");
            result.Messages.Add("  " + fallbackErrorLogPath);
            result.Messages.Add("For this diagnostic, error.log is the most reliable raw output.");

            await TryBuildEnemyWorkPcAnalysisAsync(
                fallbackErrorLogPath,
                enemyWorkPcAnalysisPath,
                result.Messages).ConfigureAwait(false);

            result.Messages.Add("This diagnostic output is not a JSONL frame trace and should not be loaded with Charger trace.");
        }
        else
        {
            result.InitialSnapshotPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_initial_snapshot.json");
            result.TracePath = Path.Combine(outputDirectory, settings.OutputPrefix + "_trace.jsonl");
            result.SummaryPath = Path.Combine(outputDirectory, settings.OutputPrefix + "_summary.txt");

            result.Messages.Add($"MAME exited with code {result.ExitCode}.");
            result.Messages.Add("Expected trace output:");
            result.Messages.Add("  " + result.TracePath);
            result.Messages.Add("Expected initial snapshot:");
            result.Messages.Add("  " + result.InitialSnapshotPath);
        }

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

    private static async Task TryBuildPreferredPcAnalysisAsync(
        string errorLogPath,
        string analysisPath,
        List<string> messages)
    {
        try
        {
            if (!File.Exists(errorLogPath))
            {
                messages.Add("Preferred PC analysis skipped: error.log not found.");
                messages.Add("  " + errorLogPath);
                return;
            }

            string errorLogText = await File.ReadAllTextAsync(errorLogPath).ConfigureAwait(false);
            IReadOnlyList<string> reportLines = LadyBugPreferredPcLogAnalyzer.BuildReport(errorLogText);

            string? directory = Path.GetDirectoryName(analysisPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                analysisPath,
                string.Join(System.Environment.NewLine, reportLines) + System.Environment.NewLine,
                Encoding.UTF8).ConfigureAwait(false);

            messages.Add("Preferred PC analysis generated:");
            messages.Add("  " + analysisPath);
        }
        catch (Exception ex)
        {
            messages.Add("WARNING: preferred PC analysis failed:");
            messages.Add("  " + ex.Message);
        }
    }

    private static async Task TryBuildEnemyWorkPcAnalysisAsync(
        string errorLogPath,
        string analysisPath,
        List<string> messages)
    {
        try
        {
            if (!File.Exists(errorLogPath))
            {
                messages.Add("EnemyWork PC analysis skipped: error.log not found.");
                messages.Add("  " + errorLogPath);
                return;
            }

            string errorLogText = await File.ReadAllTextAsync(errorLogPath).ConfigureAwait(false);
            IReadOnlyList<string> reportLines = LadyBugEnemyWorkPcLogAnalyzer.BuildReport(errorLogText);

            string? directory = Path.GetDirectoryName(analysisPath);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            await File.WriteAllTextAsync(
                analysisPath,
                string.Join(System.Environment.NewLine, reportLines) + System.Environment.NewLine,
                Encoding.UTF8).ConfigureAwait(false);

            messages.Add("EnemyWork PC analysis generated:");
            messages.Add("  " + analysisPath);
        }
        catch (Exception ex)
        {
            messages.Add("WARNING: EnemyWork PC analysis failed:");
            messages.Add("  " + ex.Message);
        }
    }

    private static ProcessRunResult RunMameProcess(ProcessStartInfo startInfo, int watchdogMilliseconds)
    {
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Failed to start MAME process.");

        if (watchdogMilliseconds <= 0)
        {
            process.WaitForExit();
            return new ProcessRunResult(process.ExitCode, false);
        }

        if (process.WaitForExit(watchdogMilliseconds))
            return new ProcessRunResult(process.ExitCode, false);

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch
        {
            try
            {
                process.Kill();
            }
            catch
            {
                // Best effort. The caller will still return a timeout result.
            }
        }

        try
        {
            process.WaitForExit(5000);
        }
        catch
        {
            // Best effort after kill.
        }

        int exitCode;
        try
        {
            exitCode = process.HasExited ? process.ExitCode : -1;
        }
        catch
        {
            exitCode = -1;
        }

        return new ProcessRunResult(exitCode, true);
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

    private static bool IsPreferredPcDiagnosticScript(string luaScriptPath)
    {
        string fileName = Path.GetFileName(luaScriptPath);
        return string.Equals(fileName, PreferredPcTraceScriptFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEnemyWorkPcDiagnosticScript(string luaScriptPath)
    {
        string fileName = Path.GetFileName(luaScriptPath);
        return string.Equals(fileName, EnemyWorkPcTraceScriptFileName, StringComparison.OrdinalIgnoreCase);
    }

    private static int ComputeDiagnosticWatchdogMilliseconds(int framesAfterTick0)
    {
        // This watchdog is intentionally short and controlled by framesAfterTick0:
        // for preferred[] reverse engineering we want to stay in the one-active-enemy window.
        //
        // The normal Lua frame limiter often cannot finish cleanly while debugger breakpoints
        // keep issuing "g", so this is a real-time kill switch, not an emulated-frame limit.
        //
        // Examples:
        //   framesAfterTick0=250 -> about  9.2 s
        //   framesAfterTick0=400 -> about 11.7 s
        //   framesAfterTick0=500 -> about 13.3 s
        //   framesAfterTick0=800 -> about 18.3 s
        int requestedFrames = Math.Max(1, framesAfterTick0);
        double emulatedSeconds = requestedFrames / 60.0;
        double realSeconds = emulatedSeconds + 5.0;

        return (int)(Math.Clamp(realSeconds, 7.0, 90.0) * 1000.0);
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

    private static string BuildDebugStartupScript()
    {
        // MAME enters the debugger immediately at startup when -debug is used.
        // This single debugger command lets the emulator run so the autoboot Lua
        // script can start and install its exact-PC breakpoints.
        return """
g

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

    private readonly record struct ProcessRunResult(int ExitCode, bool TimedOut);
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
