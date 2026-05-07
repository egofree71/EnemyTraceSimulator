using System.Collections.Generic;

/// <summary>
/// Reference-direction validation adapter for the future real Lady Bug enemy simulation.
///
/// The adapter creates frames from its own mutable simulation state. It advances
/// through the reference trace with reference-driven enemy direction and partial
/// EnemyWork reconstruction. preferred[], rejectedMask, fallback helper, chase timers,
/// and chase round-robin are progressively moved away from MAME reference-sync as
/// each source-first block becomes validated.
///
/// v0.6.93 keeps the reference-synced comparison path unchanged and appends a
/// source-first 0x4315 shadow check that runs the reconstructed 0x42E6 model over
/// the exact-PC validated current-kept cycles.
///
/// v0.7.03 keeps the non-invasive source-first Enemy_UpdateOne shadow and
/// explicitly accounts for the 0x4189 forced-reversal probe on the 0x433A
/// outside-center path. The current static-player sequence validates the clear
/// path only; the carry-set 0x4347 reversal branch remains a later milestone.
///
/// v0.7.14 feeds the exact-PC aligned preferred[] provider into the full
/// source-first Enemy_UpdateOne shadow.
///
/// v0.7.17 stops overwriting runtime rejectedMask / 0x61C1 and fallback helper /
/// 0x61C2 with MAME in LadyBugSimulationState for the current validated one-enemy
/// trace.
///
/// v0.8.0 also feeds runtime preferred[] / 0x61C4..0x61C7 from the exact-PC
/// aligned provider inside LadyBugSimulationState, falling back to MAME only if
/// the provider is unavailable. Enemy direction, chase timers, round-robin, gates,
/// timers, player and ports remain reference-assisted.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "Build the future Lady Bug simulation state from the trace. " +
        "AdvanceOneTick syncs reference controls, moves active enemies by one pixel using the MAME direction, updates first EnemyWork fields, keeps modeled rejectedMask/fallback helper since v0.7.17, feeds runtime preferred[] from the exact-PC aligned provider in v0.8.0, and computes diagnostic preferred[], rejectedMask, fallback-helper, direction, source-first transition, source-first 0x4315, source-first Enemy_UpdateOne / 0x427E / 0x4130 / 0x4189 with exact-PC aligned preferred input, preferred-generator replay-shadow, Enemy_UpdateOne preferred-input bridge, offline exact-PC preferred-tape import, exact-PC tape alignment, exact-PC selected preferred-input shadow, EnemyWork sync-removal preflight, and rejected/fallback unsynced-shadow readiness summaries in parallel.";

    // This adapter is now a valid checkpoint for the current one-enemy trace.
    // It is still reference-assisted, but the comparison pipeline should pass.
    public bool ExpectedToMismatch => false;

    /// <summary>
    /// Builds the candidate timeline from mutable simulation state.
    ///
    /// The current implementation is intentionally still reference-assisted for enemy
    /// direction and chase/timer state, but v0.8.0 removes the runtime preferred[]
    /// MAME-copy bridge when the exact-PC aligned provider is available.
    /// </summary>
    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return new SimulationAdapterResult(new List<SimulationFrame>(), "empty trace; no initial state created");

        LadyBugSimulationInitialState initialState =
            LadyBugSimulationInitialState.FromFirstFrame(referenceFrames[0]);

        LadyBugPreferredExactPcAlignedProvider preferredExactPcProvider =
            LadyBugPreferredExactPcAlignedProvider.Build(referenceFrames);

        LadyBugSimulationState simulationState =
            LadyBugSimulationState.FromInitialState(initialState);
        simulationState.ConfigurePreferredExactPcProvider(preferredExactPcProvider);

        var frames = new List<SimulationFrame>(referenceFrames.Count);
        for (int i = 0; i < referenceFrames.Count; i++)
        {
            if (i > 0)
                simulationState.AdvanceOneTick(referenceFrames[i], i);

            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));
        }

        string decisionDiagnostics = LadyBugEnemyDecisionTraceDiagnostics.BuildSummary(referenceFrames);
        string sourceFirst4315Shadow = LadyBugEnemySourceFirst4315ShadowModel.BuildSummary(referenceFrames);
        string sourceFirst4130Shadow = LadyBugEnemyLocalDoor4130ShadowModel.BuildSummary(referenceFrames);
        string enemyWorkSyncRemovalPreflight =
            LadyBugEnemyWorkSyncRemovalPreflightSummaryModel.BuildSummary(sourceFirst4130Shadow);
        string rejectedFallbackUnsyncedShadow =
            LadyBugEnemyWorkRejectedFallbackUnsyncedShadowModel.BuildSummary(
                sourceFirst4130Shadow,
                enemyWorkSyncRemovalPreflight);
        string preferredReplayShadow = LadyBugEnemyPreferredGeneratorReplayShadowModel.BuildSummary(referenceFrames);
        string updateOnePreferredInputShadow = LadyBugEnemyUpdateOnePreferredInputShadowModel.BuildSummary(referenceFrames);
        string preferredExactPcTapeImport = LadyBugPreferredExactPcTapeImportSummaryModel.BuildSummary(referenceFrames);
        string preferredExactPcTapeAlignment = LadyBugPreferredExactPcTapeAlignmentSummaryModel.BuildSummary(referenceFrames);
        string updateOneExactPcPreferredInputShadow = LadyBugEnemyUpdateOneExactPcPreferredInputShadowModel.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-synced EnemyWork checkpoint; initial state: " + initialState.Summary +
            "; AdvanceOneTick syncs player, ports, gates, timers, and enemy control state; " +
            "active enemies move one pixel using the MAME direction; " +
            "EnemyWork tempDir/tempX/tempY are still updated by the reference-direction runtime path; rejectedMask and fallback helper stay modeled since v0.7.17; preferred[] is fed from the exact-PC aligned provider in v0.8.0 when available; " +
            simulationState.BuildPreferredShadowDiagnosticSummary() + "; " +
            decisionDiagnostics + "; " +
            sourceFirst4315Shadow + "; " +
            enemyWorkSyncRemovalPreflight + "; " +
            rejectedFallbackUnsyncedShadow + "; " +
            preferredReplayShadow + "; " +
            updateOnePreferredInputShadow + "; " +
            preferredExactPcTapeImport + "; " +
            preferredExactPcTapeAlignment + "; " +
            updateOneExactPcPreferredInputShadow + "; " +
            "real enemy decision logic is not implemented yet");
    }
}
