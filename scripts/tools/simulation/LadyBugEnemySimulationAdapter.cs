using System.Collections.Generic;

/// <summary>
/// Reference-direction validation adapter for the future real Lady Bug enemy simulation.
///
/// The adapter creates frames from its own mutable simulation state. It advances
/// through the reference trace with reference-driven enemy direction and partial
/// EnemyWork reconstruction. preferred[], rejectedMask, fallback helper, chase timers,
/// and chase round-robin are temporarily synced from MAME while their real arcade
/// generators are pending.
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
/// v0.7.07b appends an explicit preferred[] generator replay-shadow bridge scoped to active-enemy frames. It
/// does not remove the preferred[] reference sync yet; it documents and measures
/// the remaining bridge after the exact-PC 0x2E5C generator validation.
/// </summary>
public sealed class LadyBugEnemySimulationAdapter : IEnemySimulationAdapter
{
    public string Name => "Lady Bug reference-direction step";

    public string Description =>
        "Build the future Lady Bug simulation state from the trace. " +
        "AdvanceOneTick syncs reference controls, moves active enemies by one pixel using the MAME direction, updates first EnemyWork fields, keeps preferred[]/rejectedMask/fallback temporarily synced from the reference trace, and computes diagnostic preferred[], rejectedMask, fallback-helper, direction, source-first transition, source-first 0x4315, source-first Enemy_UpdateOne / 0x427E / 0x4130 / 0x4189, and preferred-generator replay-shadow summaries in parallel.";

    // This adapter is now a valid checkpoint for the current one-enemy trace.
    // It is still reference-assisted, but the comparison pipeline should pass.
    public bool ExpectedToMismatch => false;

    /// <summary>
    /// Builds the candidate timeline from mutable simulation state.
    ///
    /// The current implementation is intentionally reference-assisted: it uses the
    /// MAME direction and synced preferred/chase state so the remaining pipeline can
    /// be validated before the full arcade AI is reimplemented.
    /// </summary>
    public SimulationAdapterResult Run(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames.Count == 0)
            return new SimulationAdapterResult(new List<SimulationFrame>(), "empty trace; no initial state created");

        LadyBugSimulationInitialState initialState =
            LadyBugSimulationInitialState.FromFirstFrame(referenceFrames[0]);

        LadyBugSimulationState simulationState =
            LadyBugSimulationState.FromInitialState(initialState);

        var frames = new List<SimulationFrame>(referenceFrames.Count);
        for (int i = 0; i < referenceFrames.Count; i++)
        {
            if (i > 0)
                simulationState.AdvanceOneTick(referenceFrames[i]);

            frames.Add(simulationState.BuildFrame(i, referenceFrames[i]));
        }

        string decisionDiagnostics = LadyBugEnemyDecisionTraceDiagnostics.BuildSummary(referenceFrames);
        string sourceFirst4315Shadow = LadyBugEnemySourceFirst4315ShadowModel.BuildSummary(referenceFrames);
        string sourceFirst4130Shadow = LadyBugEnemyLocalDoor4130ShadowModel.BuildSummary(referenceFrames);
        string preferredReplayShadow = LadyBugEnemyPreferredGeneratorReplayShadowModel.BuildSummary(referenceFrames);

        return new SimulationAdapterResult(
            frames,
            "Lady Bug reference-synced EnemyWork checkpoint; initial state: " + initialState.Summary +
            "; AdvanceOneTick syncs player, ports, gates, timers, and enemy control state; " +
            "active enemies move one pixel using the MAME direction; " +
            "EnemyWork tempDir/tempX/tempY, transient rejectedMask, fallback helper and preferred[] are temporarily synced from the reference trace while shadow models run in parallel; " +
            simulationState.BuildPreferredShadowDiagnosticSummary() + "; " +
            decisionDiagnostics + "; " +
            sourceFirst4315Shadow + "; " +
            sourceFirst4130Shadow + "; " +
            preferredReplayShadow + "; " +
            "real enemy decision logic is not implemented yet");
    }
}
