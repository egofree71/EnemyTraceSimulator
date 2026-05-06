using System.Collections.Generic;

/// <summary>
/// Enemy movement diagnostic state produced by a simulation source.
/// </summary>
public sealed class SimulationEnemyWorkState
{
    public int TempDir { get; set; } = -1;
    public int TempX { get; set; } = -1;
    public int TempY { get; set; } = -1;
    public int RejectedMask { get; set; } = -1;
    public int FallbackMask { get; set; } = -1;
    public List<int> Preferred { get; } = new();

    /// <summary>
    /// Diagnostic-only preferred[] tuple computed by LadyBugMonsterPreferenceSystem.
    /// The adapter still uses Preferred as the comparison value while this shadow
    /// field validates the model in parallel.
    /// </summary>
    public List<int> PreferredShadow { get; } = new();

    /// <summary>
    /// Describes how PreferredShadow was classified/generated, for example
    /// 2EC7_RANDOM_RLOW_A or 477D_OBSERVED_SLOT0_OVER_2E97_ROTATE_FROM_08.
    /// </summary>
    public string PreferredShadowSource { get; set; } = string.Empty;

    /// <summary>
    /// Diagnostic-only candidate for 0x61C2 fallback helper.
    /// The adapter still uses FallbackMask as the comparison value while this shadow
    /// field validates the helper/counter model in parallel.
    /// </summary>
    public int FallbackHelperShadow { get; set; } = -1;

    /// <summary>
    /// Describes how FallbackHelperShadow was classified/generated, for example
    /// ONE_STEP_PER_ENEMY_UPDATE.
    /// </summary>
    public string FallbackHelperShadowSource { get; set; } = string.Empty;

    /// <summary>
    /// Diagnostic-only candidate for 0x61C1 / EnemyRejectedDirMask.
    /// The adapter still uses RejectedMask as the comparison value while this shadow
    /// field validates the local rejection heuristic in parallel.
    /// </summary>
    public int RejectedMaskShadow { get; set; } = -1;

    /// <summary>
    /// Describes how RejectedMaskShadow was classified/generated, for example
    /// PLAIN_STEP or DECISION_CENTER_REJECT_PREFERRED_AND_PREVIOUS.
    /// </summary>
    public string RejectedMaskShadowSource { get; set; } = string.Empty;

    public List<int> ChaseTimers { get; } = new();
    public int ChaseRoundRobin { get; set; } = -1;
}
