using Godot;
using System;
using System.Text.Json;

/// <summary>
/// Restores the startup trace-path behavior after the v0.7.10 cleanup:
/// the UI should load the trace described by config/mame_trace_settings.json,
/// not the old hard-coded ladybug_sequence_v8_trace.jsonl fallback.
///
/// This file is intentionally small and non-invasive. It only initializes
/// _currentTracePath before the user presses "Load trace". When MAME is launched
/// from the UI, OnLaunchMameLuaPressed still replaces _currentTracePath with the
/// freshly generated trace path returned by MameTraceLauncher.
/// </summary>
public partial class EnemyTraceSimulatorWindow
{
    private static readonly JsonSerializerOptions TracePathSettingsJsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public override void _EnterTree()
    {
        ApplyConfiguredTracePathFromSettings();
    }

    private void ApplyConfiguredTracePathFromSettings()
    {
        try
        {
            string configAbsolutePath = ResolveProjectPath(DefaultMameConfigPath);
            if (!System.IO.File.Exists(configAbsolutePath))
                return;

            string json = System.IO.File.ReadAllText(configAbsolutePath);
            MameTraceSettings? settings = JsonSerializer.Deserialize<MameTraceSettings>(
                json,
                TracePathSettingsJsonOptions);

            if (settings == null)
                return;

            string outputDirectory = string.IsNullOrWhiteSpace(settings.OutputDirectory)
                ? "res://traces/mame"
                : settings.OutputDirectory.Trim();

            string outputPrefix = string.IsNullOrWhiteSpace(settings.OutputPrefix)
                ? "ladybug_sequence_v8"
                : settings.OutputPrefix.Trim();

            string outputDirectoryAbsolute = ResolveProjectPath(outputDirectory);
            string traceAbsolutePath = System.IO.Path.Combine(
                outputDirectoryAbsolute,
                outputPrefix + "_trace.jsonl");

            _currentTracePath = MameTraceLauncher.ToDisplayPath(traceAbsolutePath);
        }
        catch (Exception ex)
        {
            GD.PushWarning("Could not initialize configured trace path from MAME settings: " + ex.Message);
        }
    }
}
