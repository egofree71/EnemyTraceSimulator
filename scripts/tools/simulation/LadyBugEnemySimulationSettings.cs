using Godot;
using System;
using System.IO;
using System.Text.Json;

/// <summary>
/// Runtime settings for the Enemy Trace Simulator candidate replay.
///
/// This is deliberately separate from config/mame_trace_settings.json:
/// MAME settings describe how the reference trace is generated, while these
/// settings describe how the left-side C# candidate simulation consumes it.
/// </summary>
public sealed class LadyBugEnemySimulationSettings
{
    public const string ConfigResourcePath = "res://config/enemy_simulation_settings.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true
    };

    /// <summary>
    /// Preferred random source for the 0x2EC7 branch.
    ///
    /// Supported values:
    /// - TraceSynced: keep the random preferred[] result from MAME, for exact comparison.
    /// - CSharpSeeded: generate random preferred[] tuples with System.Random, for behavior testing.
    /// </summary>
    public string PreferredRandomMode { get; set; } = "TraceSynced";

    /// <summary>
    /// Seed used when PreferredRandomMode is CSharpSeeded.
    /// Keeping it fixed makes the left-side simulation repeatable between runs.
    /// </summary>
    public int CSharpRandomSeed { get; set; } = 1981;

    /// <summary>
    /// When the candidate uses its own random values, visual divergence from the
    /// MAME reference is expected. This option lets playback continue instead of
    /// stopping at the first random-caused mismatch.
    /// </summary>
    public bool ContinuePlaybackOnExpectedRandomDivergence { get; set; } = true;

    public LadyBugPreferredRandomMode RandomMode => ParseRandomMode(PreferredRandomMode);

    public bool UsesCSharpRandom => RandomMode == LadyBugPreferredRandomMode.CSharpSeeded;

    public bool ExpectedToDivergeFromTrace => UsesCSharpRandom && ContinuePlaybackOnExpectedRandomDivergence;

    public string RandomModeLabel => RandomMode switch
    {
        LadyBugPreferredRandomMode.CSharpSeeded => "csharp-seeded",
        _ => "trace-synced"
    };

    public static LadyBugEnemySimulationSettings LoadOrDefault()
    {
        string path = ProjectSettings.GlobalizePath(ConfigResourcePath);

        if (!File.Exists(path))
            return new LadyBugEnemySimulationSettings();

        try
        {
            string json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LadyBugEnemySimulationSettings>(json, JsonOptions)
                   ?? new LadyBugEnemySimulationSettings();
        }
        catch (Exception ex)
        {
            GD.PushWarning($"Could not load {ConfigResourcePath}: {ex.Message}. Using default simulation settings.");
            return new LadyBugEnemySimulationSettings();
        }
    }

    private static LadyBugPreferredRandomMode ParseRandomMode(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return LadyBugPreferredRandomMode.TraceSynced;

        string normalized = value.Trim().Replace("-", string.Empty).Replace("_", string.Empty);

        if (string.Equals(normalized, "CSharpSeeded", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "CSharp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "SystemRandom", StringComparison.OrdinalIgnoreCase))
        {
            return LadyBugPreferredRandomMode.CSharpSeeded;
        }

        return LadyBugPreferredRandomMode.TraceSynced;
    }
}

public enum LadyBugPreferredRandomMode
{
    TraceSynced,
    CSharpSeeded
}
