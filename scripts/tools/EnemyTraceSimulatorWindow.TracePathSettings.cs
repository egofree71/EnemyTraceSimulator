using Godot;
using System;
using System.IO;

/// <summary>
/// Keeps the default trace loader path synchronized with config/mame_trace_settings.json.
///
/// Regression context:
/// EnemyTraceSimulatorWindow keeps an in-memory _currentTracePath.  After a cold
/// start, that field used to stay on the hard-coded ladybug_sequence_v8_trace.jsonl
/// path until MAME was launched in the same session.  When the configured output
/// prefix is ladybug_sequence_v8_fullmem, pressing "Charger trace" immediately
/// after starting the simulator therefore tried to load the wrong file.
///
/// This partial companion is intentionally small: it does not change trace parsing
/// or MAME launch behavior.  It only derives the expected standard JSONL trace path
/// from the current MAME settings once the UI is ready.
/// </summary>
public partial class EnemyTraceSimulatorWindow
{
    private const string StandardJsonlTraceScriptFileName = "ladybug_sequence_trace.lua";
    private bool _tracePathRefreshQueuedFromSettings;

    public override void _Notification(int what)
    {
        if (what != NotificationReady || _tracePathRefreshQueuedFromSettings)
            return;

        _tracePathRefreshQueuedFromSettings = true;

        // Run after _Ready() has bound the console, so the user sees the corrected
        // path in the UI log instead of only the older hard-coded startup line.
        Callable.From(RefreshTracePathFromSettingsAfterReady).CallDeferred();
    }

    private void RefreshTracePathFromSettingsAfterReady()
    {
        if (TryBuildConfiguredJsonlTracePath(out string tracePath, out string message))
        {
            _currentTracePath = tracePath;
            Log($"Trace configurée depuis {DefaultMameConfigPath}: {_currentTracePath}");
            return;
        }

        if (!string.IsNullOrWhiteSpace(message))
            Log(message);
    }

    private bool TryBuildConfiguredJsonlTracePath(out string tracePath, out string message)
    {
        tracePath = DefaultTracePath;
        message = string.Empty;

        try
        {
            MameTraceSettings settings = LoadMameTraceSettings();
            string scriptFileName = Path.GetFileName(settings.LuaScriptPath.Replace('\\', '/'));

            if (!string.Equals(scriptFileName, StandardJsonlTraceScriptFileName, StringComparison.OrdinalIgnoreCase))
            {
                message = $"Trace par défaut conservée: {DefaultTracePath} " +
                          $"car le script configuré n'est pas une trace JSONL standard ({scriptFileName}).";
                return false;
            }

            string outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? "res://traces/mame"
                : settings.OutputDirectory.Trim();

            string outputPrefix = string.IsNullOrWhiteSpace(settings.OutputPrefix)
                ? "ladybug_sequence_v8"
                : settings.OutputPrefix.Trim();

            string absoluteOutputDirectory = ResolveProjectPath(outputDirectory);
            string absoluteTracePath = Path.Combine(absoluteOutputDirectory, outputPrefix + "_trace.jsonl");

            tracePath = MameTraceLauncher.ToDisplayPath(absoluteTracePath);
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not derive trace path from {DefaultMameConfigPath}: {ex.Message}. " +
                      $"Keeping fallback {DefaultTracePath}.";
            return false;
        }
    }
}
