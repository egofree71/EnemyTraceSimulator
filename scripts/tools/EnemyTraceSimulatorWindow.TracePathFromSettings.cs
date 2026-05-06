using Godot;
using System;
using System.Text.Json;

/// <summary>
/// Startup helper for EnemyTraceSimulatorWindow.
///
/// The main window keeps a private _currentTracePath field. Historically that field
/// started on a hard-coded default trace:
///
///     res://traces/mame/ladybug_sequence_v8_trace.jsonl
///
/// That was convenient for the first traces, but it became confusing once several
/// save-states / output prefixes were used. This partial class initializes
/// _currentTracePath from config/mame_trace_settings.json before the UI is ready.
///
/// It does not change the MAME launcher and it does not change trace generation.
/// It only fixes what the "Charger trace" button targets after restarting Godot.
/// </summary>
public partial class EnemyTraceSimulatorWindow : Control
{
    public override void _EnterTree()
    {
        TryInitializeCurrentTracePathFromSettings();
    }

    private void TryInitializeCurrentTracePathFromSettings()
    {
        try
        {
            string configAbsolutePath = ResolveProjectPath(DefaultMameConfigPath);
            if (!System.IO.File.Exists(configAbsolutePath))
                return;

            string json = System.IO.File.ReadAllText(configAbsolutePath);
            MameTraceSettings? settings =
                JsonSerializer.Deserialize<MameTraceSettings>(json, SettingsJsonOptions);

            if (settings == null)
                return;

            string outputDirectory = NormalizeTraceLoadDirectory(settings.OutputDirectory);
            string outputPrefix = settings.OutputPrefix.Trim();

            if (string.IsNullOrWhiteSpace(outputDirectory) ||
                string.IsNullOrWhiteSpace(outputPrefix))
            {
                return;
            }

            _currentTracePath = outputDirectory.TrimEnd('/') + "/" + outputPrefix + "_trace.jsonl";
        }
        catch
        {
            // Do not block the simulator UI if the settings file is temporarily invalid.
            // OnLoadTracePressed() will still fall back to the existing default path.
        }
    }

    private static string NormalizeTraceLoadDirectory(string path)
    {
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : path.Trim().Replace('\\', '/');
    }
}
