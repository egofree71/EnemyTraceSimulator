using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// v0.9.7 transition-based source-path inspector with compact normal reporting.
///
/// This diagnostic follows the arcade update timing instead of inspecting the
/// already displayed frame as if it were the routine input.
///
/// For a normal enemy update, the source code loads the current enemy slot into
/// the 0x61BD..0x61BF scratch area at the start of the update, then commits the
/// resulting one-pixel step. In a frame trace, that means the meaningful source
/// input for frame i is normally frame i-1, and the reference result is frame i.
///
/// The inspector deliberately does not test all four directions independently.
/// It follows the source path only:
///   0x427E decision gate
///     carry set   -> 0x42E6 preferred, then 0x4325 current, then 0x4241 fallback
///     carry clear -> 0x433A outside-center keep / door-forced-reversal path
///   0x43BA one-pixel movement step
///
/// It reports the first source path where a direction actually tested by the
/// source would be accepted by 0x3911+0x4130 while the static maze oracle says it
/// is blocked. v0.9.6 fixed the static-maze comparison by mirroring vertical
/// source enemy directions before reading maze.json wall flags. v0.9.7 keeps the
/// normal Compare output compact: detailed transition examples are only emitted
/// when a real problem is found.
/// </summary>
public static class LadyBugSourcePathDecisionInspector
{
    private const string Version = "v0.9.7";
    private const int MaxExamples = 6;

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        if (referenceFrames == null || referenceFrames.Count == 0)
            return $"Lady Bug source-path decision inspector {Version}: no frames";

        LadyBugGodotStaticMazeOracle staticOracle;
        try
        {
            staticOracle = new LadyBugGodotStaticMazeOracle();
        }
        catch (Exception ex)
        {
            return $"Lady Bug source-path decision inspector {Version}: could not initialize static maze oracle: {ex.Message}";
        }

        var stats = new Stats { Frames = referenceFrames.Count };

        for (int i = 1; i < referenceFrames.Count; i++)
            InspectTransition(i, referenceFrames[i - 1], referenceFrames[i], staticOracle, stats);

        return stats.BuildSummary();
    }

    private static void InspectTransition(
        int resultFrameIndex,
        EnemyTraceFrame startFrame,
        EnemyTraceFrame resultFrame,
        LadyBugGodotStaticMazeOracle staticOracle,
        Stats stats)
    {
        stats.Transitions++;

        if (resultFrame.enemyWork == null)
        {
            stats.SkippedMissingEnemyWork++;
            return;
        }

        if (startFrame.enemies == null || startFrame.enemies.Count == 0)
        {
            stats.SkippedNoStartEnemies++;
            return;
        }

        foreach (EnemyTraceActor startEnemy in startFrame.enemies)
        {
            if (!startEnemy.active || !startEnemy.HasKnownPosition)
                continue;

            stats.ActiveStartEnemyStates++;

            int slot = Math.Clamp(startEnemy.slot, 0, 3);
            if (!TryGetEnemySlot(resultFrame, slot, out EnemyTraceActor? resultEnemy) || resultEnemy == null)
            {
                stats.SkippedMissingResultSlot++;
                continue;
            }

            if (!resultEnemy.active || !resultEnemy.HasKnownPosition)
            {
                stats.SkippedInactiveResultSlot++;
                continue;
            }

            int currentDir = DirectionFromRaw(startEnemy.raw);
            if (!IsOneHotDirection(currentDir))
            {
                stats.InvalidCurrentDirection++;
                continue;
            }

            int preferred = TryGetPreferred(resultFrame, slot, out int value) ? value : 0;
            if (!IsOneHotDirection(preferred))
            {
                stats.MissingPreferred++;
                continue;
            }

            var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
            {
                TempDir = currentDir,
                TempX = startEnemy.x & 0xFF,
                TempY = startEnemy.y & 0xFF,
                RejectedDirMask = 0,
                FallbackHelper = 0
            };

            if (!LadyBugMameLocalTile4130Oracle.TryCreate(resultFrame, out LadyBugMameLocalTile4130Oracle? localTileOracle) || localTileOracle == null)
            {
                stats.MissingVramInspections++;
                continue;
            }

            TransitionInspection inspection = InspectSourcePath(
                resultFrameIndex,
                startFrame,
                resultFrame,
                startEnemy,
                resultEnemy,
                scratch,
                preferred,
                staticOracle,
                localTileOracle);

            stats.Record(inspection);
        }
    }

    private static TransitionInspection InspectSourcePath(
        int resultFrameIndex,
        EnemyTraceFrame startFrame,
        EnemyTraceFrame resultFrame,
        EnemyTraceActor startEnemy,
        EnemyTraceActor resultEnemy,
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        int preferred,
        LadyBugGodotStaticMazeOracle staticOracle,
        LadyBugMameLocalTile4130Oracle localTileOracle)
    {
        var tested = new List<DirectionProbeInfo>();
        LadyBugEnemyDecisionGate427EModel.Result gate =
            LadyBugEnemyDecisionGate427EModel.Evaluate(scratch.TempDir, scratch.TempX, scratch.TempY);

        string path;
        bool fallbackEntered = false;
        bool currentCheckedByLogicalMask = false;
        int selectedDirection;
        int rejectedMaskBeforeStep;

        if (gate.CarrySet)
        {
            DirectionProbeInfo preferredProbe = ProbeDirection(
                "preferred-42E6",
                preferred,
                scratch.TempX,
                scratch.TempY,
                staticOracle,
                localTileOracle);
            tested.Add(preferredProbe);

            if (preferredProbe.SourceFinalAllowed)
            {
                scratch.TempDir = preferred;
                path = "42E6_PREFERRED_ACCEPTED";
            }
            else
            {
                scratch.RejectedDirMask = (scratch.RejectedDirMask | preferred) & 0x0F;
                int current = scratch.TempDir & 0x0F;
                currentCheckedByLogicalMask = true;

                DirectionProbeInfo currentProbe = ProbeDirection(
                    "current-4325",
                    current,
                    scratch.TempX,
                    scratch.TempY,
                    staticOracle,
                    localTileOracle);
                tested.Add(currentProbe);

                if (currentProbe.SourceFinalAllowed)
                {
                    path = "4315_PREFERRED_REJECTED_CURRENT_KEPT";
                }
                else
                {
                    scratch.RejectedDirMask = (scratch.RejectedDirMask | current) & 0x0F;
                    fallbackEntered = true;
                    path = FindFallbackForInspection(
                        scratch,
                        staticOracle,
                        localTileOracle,
                        tested);
                }
            }
        }
        else
        {
            bool forcedReversal = LadyBugEnemyDecisionModel.CheckDoorForcedReversal(
                scratch.TempDir,
                scratch.TempX,
                scratch.TempY,
                localTileOracle.ReadTileAtProbe);

            if (forcedReversal)
            {
                scratch.TempDir = LadyBugEnemyDecisionModel.ReverseDirection(scratch.TempDir);
                path = "433A_OUTSIDE_CENTER_FORCED_REVERSAL";
            }
            else
            {
                path = "433A_OUTSIDE_CENTER_KEEP_DIRECTION";
            }
        }

        selectedDirection = scratch.TempDir & 0x0F;
        rejectedMaskBeforeStep = scratch.RejectedDirMask & 0x0F;

        int beforeStepX = scratch.TempX & 0xFF;
        int beforeStepY = scratch.TempY & 0xFF;
        LadyBugEnemyDecisionModel.ApplyEnemyTempMovementStep(scratch);

        bool resultMatchesSlot =
            ((resultEnemy.x & 0xFF) == (scratch.TempX & 0xFF)) &&
            ((resultEnemy.y & 0xFF) == (scratch.TempY & 0xFF)) &&
            (DirectionFromRaw(resultEnemy.raw) == selectedDirection);

        bool resultMatchesEnemyWork = resultFrame.enemyWork != null &&
            ((resultFrame.enemyWork.tempX & 0xFF) == (scratch.TempX & 0xFF)) &&
            ((resultFrame.enemyWork.tempY & 0xFF) == (scratch.TempY & 0xFF)) &&
            ((resultFrame.enemyWork.tempDir & 0x0F) == selectedDirection);

        bool sourceAcceptedButStaticBlocked = false;
        foreach (DirectionProbeInfo probe in tested)
        {
            if (probe.SourceFinalAllowed && !probe.StaticAllowed)
            {
                sourceAcceptedButStaticBlocked = true;
                break;
            }
        }

        return new TransitionInspection(
            resultFrameIndex,
            startFrame,
            resultFrame,
            startEnemy,
            resultEnemy,
            gate,
            preferred,
            selectedDirection,
            path,
            fallbackEntered,
            currentCheckedByLogicalMask,
            rejectedMaskBeforeStep,
            beforeStepX,
            beforeStepY,
            scratch.TempX & 0xFF,
            scratch.TempY & 0xFF,
            scratch.FallbackHelper & 0xFF,
            resultMatchesSlot,
            resultMatchesEnemyWork,
            sourceAcceptedButStaticBlocked,
            tested);
    }

    private static string FindFallbackForInspection(
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        LadyBugGodotStaticMazeOracle staticOracle,
        LadyBugMameLocalTile4130Oracle localTileOracle,
        List<DirectionProbeInfo> tested)
    {
        int scanMask = scratch.RejectedDirMask & 0x0F;
        int direction = LadyBugEnemyDecisionModel.DirLeft;

        for (int attempt = 1; attempt <= 8; attempt++)
        {
            bool alreadyRejected = (scanMask & 0x01) != 0;

            if (!alreadyRejected)
            {
                DirectionProbeInfo probe = ProbeDirection(
                    $"fallback-4241-attempt{attempt}",
                    direction,
                    scratch.TempX,
                    scratch.TempY,
                    staticOracle,
                    localTileOracle);
                tested.Add(probe);

                if (probe.SourceFinalAllowed)
                {
                    scratch.TempDir = direction;
                    return "4331_FALLBACK_SELECTED";
                }

                scratch.RejectedDirMask = (scratch.RejectedDirMask | direction) & 0x0F;
            }

            direction <<= 1;
            scanMask >>= 1;

            if ((direction & 0x10) != 0)
            {
                scanMask = 0;
                direction = LadyBugEnemyDecisionModel.DirLeft;
            }
        }

        return "4331_FALLBACK_NOT_FOUND";
    }

    private static DirectionProbeInfo ProbeDirection(
        string label,
        int direction,
        int x,
        int y,
        LadyBugGodotStaticMazeOracle staticOracle,
        LadyBugMameLocalTile4130Oracle localTileOracle)
    {
        LadyBugEnemyDecisionModel.LogicalMazeValidationResult logical =
            LadyBugEnemyDecisionModel.ValidateLogicalMazeDirection(
                direction,
                x,
                y,
                LadyBugStaticMazeRomTable.Table0DA2);

        EnemyCollisionProbeResult localTile = localTileOracle.Probe(x, y, direction);
        EnemyCollisionProbeResult staticMaze = staticOracle.Probe(x, y, direction);
        bool localOpen = localTile.Allowed;
        bool sourceFinalAllowed = logical.Accepted && localOpen;

        return new DirectionProbeInfo(
            label,
            direction & 0x0F,
            x & 0xFF,
            y & 0xFF,
            logical.Accepted,
            logical.TableIndex,
            logical.PackedByte,
            logical.AllowedMask,
            localOpen,
            localTile.BlockKind,
            localTile.Details,
            staticMaze.Allowed,
            staticMaze.BlockKind,
            staticMaze.Details,
            LadyBugGodotStaticMazeOracle.ToGodotMazeDirectionLabel(direction),
            sourceFinalAllowed);
    }

    private static bool TryGetPreferred(EnemyTraceFrame frame, int slot, out int preferred)
    {
        preferred = 0;
        if (frame.enemyWork == null || frame.enemyWork.preferred == null)
            return false;

        if (slot < 0 || slot >= frame.enemyWork.preferred.Count)
            return false;

        preferred = frame.enemyWork.preferred[slot] & 0x0F;
        return preferred != 0;
    }

    private static bool TryGetEnemySlot(EnemyTraceFrame frame, int slot, out EnemyTraceActor? enemy)
    {
        enemy = null;
        if (frame.enemies == null)
            return false;

        foreach (EnemyTraceActor candidate in frame.enemies)
        {
            if (candidate.slot == slot)
            {
                enemy = candidate;
                return true;
            }
        }

        return false;
    }

    private static int DirectionFromRaw(int raw)
    {
        return raw < 0 ? 0 : (raw >> 4) & 0x0F;
    }

    private static bool IsOneHotDirection(int direction)
    {
        int d = direction & 0x0F;
        return d == LadyBugEnemyDecisionModel.DirLeft ||
               d == LadyBugEnemyDecisionModel.DirUp ||
               d == LadyBugEnemyDecisionModel.DirRight ||
               d == LadyBugEnemyDecisionModel.DirDown;
    }

    private static string DirLabel(int direction)
    {
        return LadyBugDirectionBits.ToLabel(direction);
    }

    private static string Hex2(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string FormatFrameLabel(string role, int index, EnemyTraceFrame frame)
    {
        return $"{role}Index={index} {role}Tick={frame.frame}";
    }

    private sealed class Stats
    {
        public int Frames;
        public int Transitions;
        public int ActiveStartEnemyStates;
        public int InvalidCurrentDirection;
        public int PixelUnalignedStartStates;
        public int PixelAlignedStartStates;
        public int DecisionGateCarryClearStates;
        public int DecisionGateCarrySetStates;
        public int MissingPreferred;
        public int InspectedTransitions;
        public int TestedDirectionProbes;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int FallbackEntered;
        public int FallbackSelected;
        public int FallbackNotFound;
        public int OutsideCenterKeep;
        public int OutsideCenterForcedReversal;
        public int SourceAcceptedButStaticBlockedProbes;
        public int ResultMatchesSlot;
        public int ResultMismatchesSlot;
        public int ResultMatchesEnemyWork;
        public int ResultMismatchesEnemyWork;
        public int MissingVramInspections;
        public int SkippedMissingEnemyWork;
        public int SkippedNoStartEnemies;
        public int SkippedMissingResultSlot;
        public int SkippedInactiveResultSlot;
        public string? FirstDecisionPath;
        public string? FirstPreferredAccepted;
        public string? FirstPreferredRejectedCurrentKept;
        public string? FirstFallbackSelected;
        public string? FirstOutsideCenter;
        public string? FirstSourceAcceptedButStaticBlocked;
        public string? FirstResultMismatchSlot;
        public string? FirstResultMismatchEnemyWork;
        public string? FirstCarryClearExample;
        public readonly List<string> Examples = new();

        public void Record(TransitionInspection inspection)
        {
            InspectedTransitions++;
            TestedDirectionProbes += inspection.Tested.Count;

            if (inspection.Gate.PixelAligned)
                PixelAlignedStartStates++;
            else
                PixelUnalignedStartStates++;

            if (inspection.Gate.CarrySet)
                DecisionGateCarrySetStates++;
            else
                DecisionGateCarryClearStates++;

            switch (inspection.Path)
            {
                case "42E6_PREFERRED_ACCEPTED":
                    PreferredAccepted++;
                    FirstPreferredAccepted ??= inspection.ToSummary("firstPreferredAccepted");
                    break;
                case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
                    PreferredRejectedCurrentKept++;
                    FirstPreferredRejectedCurrentKept ??= inspection.ToSummary("firstPreferredRejectedCurrentKept");
                    break;
                case "4331_FALLBACK_SELECTED":
                    FallbackEntered++;
                    FallbackSelected++;
                    FirstFallbackSelected ??= inspection.ToSummary("firstFallbackSelected");
                    break;
                case "4331_FALLBACK_NOT_FOUND":
                    FallbackEntered++;
                    FallbackNotFound++;
                    break;
                case "433A_OUTSIDE_CENTER_FORCED_REVERSAL":
                    OutsideCenterForcedReversal++;
                    FirstOutsideCenter ??= inspection.ToSummary("firstOutsideCenter");
                    break;
                default:
                    if (inspection.Path.StartsWith("433A_OUTSIDE_CENTER", StringComparison.Ordinal))
                    {
                        OutsideCenterKeep++;
                        FirstOutsideCenter ??= inspection.ToSummary("firstOutsideCenter");
                    }
                    break;
            }

            if (inspection.ResultMatchesSlot)
            {
                ResultMatchesSlot++;
            }
            else
            {
                ResultMismatchesSlot++;
                FirstResultMismatchSlot ??= inspection.ToSummary("firstResultMismatchSlot");
            }

            if (inspection.ResultMatchesEnemyWork)
            {
                ResultMatchesEnemyWork++;
            }
            else
            {
                ResultMismatchesEnemyWork++;
                FirstResultMismatchEnemyWork ??= inspection.ToSummary("firstResultMismatchEnemyWork");
            }

            if (inspection.SourceAcceptedButStaticBlocked)
            {
                SourceAcceptedButStaticBlockedProbes++;
                FirstSourceAcceptedButStaticBlocked ??= inspection.ToSummary("firstSourceAcceptedButStaticBlocked", emphasizeStaticBlock: true);
            }

            if (!inspection.Gate.CarrySet)
            {
                FirstCarryClearExample ??=
                    $"firstCarryClearExample {FormatFrameLabel("start", inspection.StartFrameIndex, inspection.StartFrame)} " +
                    $"{FormatFrameLabel("result", inspection.ResultFrameIndex, inspection.ResultFrame)} " +
                    $"slot={inspection.Slot} startPos=({Hex2(inspection.StartX)},{Hex2(inspection.StartY)}) " +
                    $"current={DirLabel(inspection.StartDir)} gate={inspection.Gate.Source}/{inspection.Gate.Helper} " +
                    $"compare={inspection.Gate.CompareCount}:{Hex2(inspection.Gate.FinalA)}/{Hex2(inspection.Gate.FinalB)}";
            }

            FirstDecisionPath ??= inspection.ToSummary("firstDecisionPath");

            if (Examples.Count < MaxExamples)
                Examples.Add(inspection.ToSummary("example"));
        }

        public string BuildSummary()
        {
            bool clean =
                InvalidCurrentDirection == 0 &&
                MissingPreferred == 0 &&
                FallbackNotFound == 0 &&
                SourceAcceptedButStaticBlockedProbes == 0 &&
                ResultMismatchesSlot == 0 &&
                ResultMismatchesEnemyWork == 0 &&
                MissingVramInspections == 0 &&
                SkippedMissingEnemyWork == 0 &&
                SkippedNoStartEnemies == 0 &&
                SkippedMissingResultSlot == 0 &&
                SkippedInactiveResultSlot == 0;

            var builder = new StringBuilder();
            builder.Append($"Lady Bug source-path decision inspector {Version}: ");
            builder.Append($"frames={Frames}, ");
            builder.Append($"transitions={Transitions}, ");
            builder.Append($"inspectedTransitions={InspectedTransitions}, ");
            builder.Append($"activeStartEnemyStates={ActiveStartEnemyStates}, ");
            builder.Append($"pixelAlignedStartStates={PixelAlignedStartStates}, ");
            builder.Append($"pixelUnalignedStartStates={PixelUnalignedStartStates}, ");
            builder.Append($"decisionGateCarrySetStates={DecisionGateCarrySetStates}, ");
            builder.Append($"decisionGateCarryClearStates={DecisionGateCarryClearStates}, ");
            builder.Append($"testedDirectionProbes={TestedDirectionProbes}, ");
            builder.Append($"preferredAccepted={PreferredAccepted}, ");
            builder.Append($"preferredRejectedCurrentKept={PreferredRejectedCurrentKept}, ");
            builder.Append($"fallbackSelected={FallbackSelected}, ");
            builder.Append($"fallbackNotFound={FallbackNotFound}, ");
            builder.Append($"outsideCenterKeep={OutsideCenterKeep}, ");
            builder.Append($"outsideCenterForcedReversal={OutsideCenterForcedReversal}, ");
            builder.Append($"sourceAcceptedButStaticBlockedProbes={SourceAcceptedButStaticBlockedProbes}, ");
            builder.Append($"resultMismatchesSlot={ResultMismatchesSlot}, ");
            builder.Append($"resultMismatchesEnemyWork={ResultMismatchesEnemyWork}, ");
            builder.Append($"missingVramInspections={MissingVramInspections}, ");
            builder.Append($"skippedMissingEnemyWork={SkippedMissingEnemyWork}, ");
            builder.Append($"skippedNoStartEnemies={SkippedNoStartEnemies}, ");
            builder.Append($"skippedMissingResultSlot={SkippedMissingResultSlot}, ");
            builder.Append($"skippedInactiveResultSlot={SkippedInactiveResultSlot}, ");
            builder.Append($"clean={(clean ? "true" : "false")}, ");
            builder.Append("allDirectionProbeMode=disabled-by-transition-source-path-inspector, ");
            builder.Append("staticMazeDirectionMapping=source-enemy-y-mirrored-to-godot-maze, ");
            builder.Append("normalReport=compact");

            if (clean)
            {
                builder.Append("; firstProblem: none");
            }
            else
            {
                AppendOptional(builder, FirstResultMismatchSlot);
                AppendOptional(builder, FirstResultMismatchEnemyWork);
                AppendOptional(builder, FirstSourceAcceptedButStaticBlocked);

                if (FallbackNotFound > 0)
                    builder.Append("; fallbackNotFound: see detailed inspector dump needed");

                if (MissingVramInspections > 0)
                    builder.Append("; missingVramInspections: loaded trace must include rawMemory.vramD000_D3FF");

                if (InvalidCurrentDirection > 0 || MissingPreferred > 0 ||
                    SkippedMissingEnemyWork > 0 || SkippedNoStartEnemies > 0 ||
                    SkippedMissingResultSlot > 0 || SkippedInactiveResultSlot > 0)
                {
                    builder.Append("; skippedOrInvalidInput: inspect trace/frame shape before trusting this summary");
                }
            }

            builder.Append("; NOTE: compact transition-based inspector. Detailed examples are intentionally omitted from normal Compare output. Source input is frame[i-1] enemy slot; reference result is frame[i].");

            return builder.ToString();
        }

        private static void AppendOptional(StringBuilder builder, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            builder.Append("; ");
            builder.Append(value);
        }
    }

    private sealed class TransitionInspection
    {
        public TransitionInspection(
            int resultFrameIndex,
            EnemyTraceFrame startFrame,
            EnemyTraceFrame resultFrame,
            EnemyTraceActor startEnemy,
            EnemyTraceActor resultEnemy,
            LadyBugEnemyDecisionGate427EModel.Result gate,
            int preferred,
            int selectedDirection,
            string path,
            bool fallbackEntered,
            bool currentCheckedByLogicalMask,
            int rejectedMaskBeforeStep,
            int beforeStepX,
            int beforeStepY,
            int modeledX,
            int modeledY,
            int modeledFallbackHelper,
            bool resultMatchesSlot,
            bool resultMatchesEnemyWork,
            bool sourceAcceptedButStaticBlocked,
            List<DirectionProbeInfo> tested)
        {
            ResultFrameIndex = resultFrameIndex;
            StartFrameIndex = resultFrameIndex - 1;
            StartFrame = startFrame;
            ResultFrame = resultFrame;
            Slot = startEnemy.slot;
            StartRaw = startEnemy.raw;
            StartDir = DirectionFromRaw(startEnemy.raw);
            StartX = startEnemy.x & 0xFF;
            StartY = startEnemy.y & 0xFF;
            ResultRaw = resultEnemy.raw;
            ResultDir = DirectionFromRaw(resultEnemy.raw);
            ResultX = resultEnemy.x & 0xFF;
            ResultY = resultEnemy.y & 0xFF;
            Gate = gate;
            Preferred = preferred & 0x0F;
            SelectedDirection = selectedDirection & 0x0F;
            Path = path;
            FallbackEntered = fallbackEntered;
            CurrentCheckedByLogicalMask = currentCheckedByLogicalMask;
            RejectedMaskBeforeStep = rejectedMaskBeforeStep & 0x0F;
            BeforeStepX = beforeStepX & 0xFF;
            BeforeStepY = beforeStepY & 0xFF;
            ModeledX = modeledX & 0xFF;
            ModeledY = modeledY & 0xFF;
            ModeledFallbackHelper = modeledFallbackHelper & 0xFF;
            ResultMatchesSlot = resultMatchesSlot;
            ResultMatchesEnemyWork = resultMatchesEnemyWork;
            SourceAcceptedButStaticBlocked = sourceAcceptedButStaticBlocked;
            Tested = tested;
        }

        public int StartFrameIndex { get; }
        public int ResultFrameIndex { get; }
        public EnemyTraceFrame StartFrame { get; }
        public EnemyTraceFrame ResultFrame { get; }
        public int Slot { get; }
        public int StartRaw { get; }
        public int StartDir { get; }
        public int StartX { get; }
        public int StartY { get; }
        public int ResultRaw { get; }
        public int ResultDir { get; }
        public int ResultX { get; }
        public int ResultY { get; }
        public LadyBugEnemyDecisionGate427EModel.Result Gate { get; }
        public int Preferred { get; }
        public int SelectedDirection { get; }
        public string Path { get; }
        public bool FallbackEntered { get; }
        public bool CurrentCheckedByLogicalMask { get; }
        public int RejectedMaskBeforeStep { get; }
        public int BeforeStepX { get; }
        public int BeforeStepY { get; }
        public int ModeledX { get; }
        public int ModeledY { get; }
        public int ModeledFallbackHelper { get; }
        public bool ResultMatchesSlot { get; }
        public bool ResultMatchesEnemyWork { get; }
        public bool SourceAcceptedButStaticBlocked { get; }
        public List<DirectionProbeInfo> Tested { get; }

        public string ToSummary(string label, bool emphasizeStaticBlock = false)
        {
            var builder = new StringBuilder();
            builder.Append(label);
            builder.Append(' ');
            builder.Append(FormatFrameLabel("start", StartFrameIndex, StartFrame));
            builder.Append(' ');
            builder.Append(FormatFrameLabel("result", ResultFrameIndex, ResultFrame));
            builder.Append($" slot={Slot} ");
            builder.Append($"startRaw={Hex2(StartRaw)} start=({Hex2(StartX)},{Hex2(StartY)}) godotY={Hex2(MameTraceCoordinates.MameToGodotArcadeY(StartY))} current={DirLabel(StartDir)} ");
            builder.Append($"preferred={DirLabel(Preferred)} selected={DirLabel(SelectedDirection)} path={Path} ");
            builder.Append($"gate={Gate.Source}/{Gate.Helper}/{Gate.CompareCount}:{Hex2(Gate.FinalA)}/{Hex2(Gate.FinalB)} ");
            builder.Append($"beforeStep=({Hex2(BeforeStepX)},{Hex2(BeforeStepY)}) modeledAfter=({Hex2(ModeledX)},{Hex2(ModeledY)}) helper={Hex2(ModeledFallbackHelper)} ");
            builder.Append($"resultRaw={Hex2(ResultRaw)} result=({Hex2(ResultX)},{Hex2(ResultY)}) resultDir={DirLabel(ResultDir)} ");
            builder.Append($"resultMatchesSlot={ResultMatchesSlot} resultMatchesEnemyWork={ResultMatchesEnemyWork} ");
            builder.Append($"rejectedBeforeStep={Hex2(RejectedMaskBeforeStep)} currentCheckedByLogicalMask={CurrentCheckedByLogicalMask} fallbackEntered={FallbackEntered} ");
            builder.Append("tested=[");
            for (int i = 0; i < Tested.Count; i++)
            {
                if (i > 0)
                    builder.Append(" | ");

                DirectionProbeInfo probe = Tested[i];
                bool emph = emphasizeStaticBlock && probe.SourceFinalAllowed && !probe.StaticAllowed;
                if (emph)
                    builder.Append("**");
                builder.Append(probe.ToSummary());
                if (emph)
                    builder.Append("**");
            }
            builder.Append(']');
            return builder.ToString();
        }
    }

    private readonly struct DirectionProbeInfo
    {
        public DirectionProbeInfo(
            string label,
            int direction,
            int x,
            int y,
            bool logicalAccepted,
            int tableIndex,
            int packedByte,
            int allowedMask,
            bool localTileOpen,
            string localBlockKind,
            string localDetails,
            bool staticAllowed,
            string staticBlockKind,
            string staticDetails,
            string staticMazeDirectionLabel,
            bool sourceFinalAllowed)
        {
            Label = label;
            Direction = direction & 0x0F;
            X = x & 0xFF;
            Y = y & 0xFF;
            LogicalAccepted = logicalAccepted;
            TableIndex = tableIndex;
            PackedByte = packedByte & 0xFF;
            AllowedMask = allowedMask & 0x0F;
            LocalTileOpen = localTileOpen;
            LocalBlockKind = localBlockKind;
            LocalDetails = localDetails;
            StaticAllowed = staticAllowed;
            StaticBlockKind = staticBlockKind;
            StaticDetails = staticDetails;
            StaticMazeDirectionLabel = staticMazeDirectionLabel;
            SourceFinalAllowed = sourceFinalAllowed;
        }

        public string Label { get; }
        public int Direction { get; }
        public int X { get; }
        public int Y { get; }
        public bool LogicalAccepted { get; }
        public int TableIndex { get; }
        public int PackedByte { get; }
        public int AllowedMask { get; }
        public bool LocalTileOpen { get; }
        public string LocalBlockKind { get; }
        public string LocalDetails { get; }
        public bool StaticAllowed { get; }
        public string StaticBlockKind { get; }
        public string StaticDetails { get; }
        public string StaticMazeDirectionLabel { get; }
        public bool SourceFinalAllowed { get; }

        public string ToSummary()
        {
            return $"{Label}:{DirLabel(Direction)}@({Hex2(X)},{Hex2(Y)})" +
                   "{" +
                   $"3911={(LogicalAccepted ? "allow" : "block")} mask={AllowedMask:X1} table=0x{TableIndex:X2} byte={PackedByte:X2}," +
                   $"4130={(LocalTileOpen ? "open" : "block")}/{LocalBlockKind}," +
                   $"static={(StaticAllowed ? "allow" : "block")}/{StaticBlockKind}/godotDir={StaticMazeDirectionLabel}," +
                   $"sourceFinal={(SourceFinalAllowed ? "allow" : "block")}" +
                   "}";
        }
    }
}
