using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

/// <summary>
/// Source-first shadow diagnostic for the 0x4130 local-door/tile validator.
///
/// v0.6.96 proved with exact-PC logs that:
/// - 0x3C0A computes HL as D0A0 + ((D & F8) * 4) + (E >> 3);
/// - 0x4130 reads the true tile value immediately after each 0x3C0A call;
/// - tile values 35/37 reject vertical probes and 3D/3F reject horizontal probes.
///
/// This v0.7.00 class wires that knowledge into the existing source-first
/// LadyBugEnemyDecisionModel.TryPreferredDirection() transcription, but only as a
/// shadow diagnostic. Normal cycles load the start scratch from the previous
/// enemy slot state, matching the source flow at 0x43F0..0x4405.
///
/// v0.7.00 also replaces the old pixel-only center predicate with a source-first
/// transcription of the full 0x427E decision gate.  The arcade only enters
/// 0x42E0 preferred-decision logic when 0x427E returns carry set; pixel alignment
/// alone is not sufficient.
///
/// Important trace requirement:
/// the standard JSONL trace must include vramD000_D3FF on each frame
/// (config includeFullMemoryEachFrame=true) for this shadow model to run.  Older
/// traces without per-frame VRAM remain valid; this diagnostic will simply report
/// skippedMissingVram instead of guessing tile values.
/// </summary>
public static class LadyBugEnemyLocalDoor4130ShadowModel
{
    private const string Version = "v0.7.00";
    private const int VramBase = 0xD000;
    private const int VramLength = 0x0400;

    private sealed class Counters
    {
        public int Frames;
        public int Transitions;
        public int ReleaseActivationTransitions;
        public int DecisionCenterCandidates;
        public int PixelAlignedCandidates;
        public int DecisionGateCarrySet;
        public int DecisionGateCarryClear;
        public int OutsideCenterPath;
        public int ForcedReversalApplied;
        public int Checks;
        public int Matches;
        public int Mismatches;
        public int TileReads;
        public int TileAddressOutOfRange;
        public int SkippedMissingEnemyWork;
        public int SkippedMissingPreferred;
        public int SkippedMissingVram;
        public int SkippedNonCenter;
        public int SkippedNoActiveEnemy;
        public int SkippedPreviousSlotMissing;
        public int SkippedPreviousSlotInactive;
        public int SkippedPreviousSlotInvalidDirection;
        public int PreferredAccepted;
        public int PreferredRejectedCurrentKept;
        public int FallbackSelected;
        public int FallbackNotFound;
        public string FirstMatch = string.Empty;
        public string FirstMismatch = string.Empty;
        public readonly Dictionary<string, int> Sources = new(StringComparer.Ordinal);
        public readonly Dictionary<string, int> FirstTileByBranch = new(StringComparer.OrdinalIgnoreCase);
    }

    public static string BuildSummary(IReadOnlyList<EnemyTraceFrame> referenceFrames)
    {
        var counters = new Counters { Frames = referenceFrames.Count };

        for (int i = 1; i < referenceFrames.Count; i++)
            AnalyzeTransition(referenceFrames[i - 1], referenceFrames[i], counters);

        return BuildSummaryText(counters);
    }

    private static void AnalyzeTransition(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        Counters counters)
    {
        counters.Transitions++;

        if (currentFrame.enemyWork == null)
        {
            counters.SkippedMissingEnemyWork++;
            return;
        }

        if (LadyBugEnemyReleaseModel.TryObserveActivationTransition(
                previousFrame,
                currentFrame,
                out LadyBugEnemyReleaseModel.ReleaseTransitionObservation? releaseObservation) &&
            releaseObservation != null &&
            releaseObservation.MatchesEnemyWorkAfterFirstStep)
        {
            counters.ReleaseActivationTransitions++;
            AnalyzeReleaseCandidate(currentFrame, releaseObservation, counters);
            return;
        }

        AnalyzeNormalCandidate(previousFrame, currentFrame, counters);
    }

    private static void AnalyzeReleaseCandidate(
        EnemyTraceFrame currentFrame,
        LadyBugEnemyReleaseModel.ReleaseTransitionObservation observation,
        Counters counters)
    {
        if (!TrySelectPreferred(currentFrame, observation.Slot, out int preferred))
        {
            counters.SkippedMissingPreferred++;
            return;
        }

        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = LadyBugEnemyReleaseModel.SourceReleaseDir,
            TempX = LadyBugEnemyReleaseModel.SourceReleaseX,
            TempY = LadyBugEnemyReleaseModel.SourceReleaseY,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        LadyBugEnemyDecisionGate427EModel.Result gate =
            LadyBugEnemyDecisionGate427EModel.Evaluate(scratch.TempDir, scratch.TempX, scratch.TempY);

        counters.PixelAlignedCandidates += gate.PixelAligned ? 1 : 0;

        RunCandidate(
            currentFrame,
            scratch,
            preferred,
            observation.Slot,
            gate.CarrySet
                ? "3061_427E_CARRY_SET_4130_RELEASE_DECISION"
                : "3061_427E_CARRY_CLEAR_433A_RELEASE_OUTSIDE_CENTER",
            gate,
            counters);
    }

    private static void AnalyzeNormalCandidate(
        EnemyTraceFrame previousFrame,
        EnemyTraceFrame currentFrame,
        Counters counters)
    {
        if (currentFrame.enemyWork == null)
        {
            counters.SkippedMissingEnemyWork++;
            return;
        }

        if (!TrySelectEnemyWorkSlot(currentFrame, out EnemyTraceActor? currentEnemy) || currentEnemy == null)
        {
            counters.SkippedNoActiveEnemy++;
            return;
        }

        int slot = Math.Clamp(currentEnemy.slot, 0, 3);
        if (!TrySelectPreferred(currentFrame, slot, out int preferred))
        {
            counters.SkippedMissingPreferred++;
            return;
        }

        if (!TryGetEnemySlot(previousFrame, slot, out EnemyTraceActor? previousEnemy) || previousEnemy == null)
        {
            counters.SkippedPreviousSlotMissing++;
            return;
        }

        if (!previousEnemy.active || !previousEnemy.HasKnownPosition)
        {
            counters.SkippedPreviousSlotInactive++;
            return;
        }

        int startDir = DirectionFromRaw(previousEnemy.raw);
        if (!IsOneHotDirection(startDir))
        {
            counters.SkippedPreviousSlotInvalidDirection++;
            return;
        }

        // Source-first v0.6.98 correction:
        // 0x42BD calls 0x43F0..0x4405 to load the current enemy slot into
        // temporary scratch, then 0x42C9 LDIR copies that three-byte state into
        // 0x61BD..0x61BF before decision testing.  Therefore the correct normal
        // cycle start is the previous frame's slot state, not an inverse of the
        // already-committed current EnemyWork state.
        var scratch = new LadyBugEnemyDecisionModel.EnemyDecisionScratch
        {
            TempDir = startDir,
            TempX = previousEnemy.x & 0xFF,
            TempY = previousEnemy.y & 0xFF,
            RejectedDirMask = 0,
            FallbackHelper = 0
        };

        LadyBugEnemyDecisionGate427EModel.Result gate =
            LadyBugEnemyDecisionGate427EModel.Evaluate(scratch.TempDir, scratch.TempX, scratch.TempY);

        if (!gate.PixelAligned)
        {
            counters.SkippedNonCenter++;
            return;
        }

        counters.PixelAlignedCandidates++;

        RunCandidate(
            currentFrame,
            scratch,
            preferred,
            slot,
            gate.CarrySet
                ? "43F0_427E_CARRY_SET_4130_DECISION_CENTER"
                : "43F0_427E_CARRY_CLEAR_433A_OUTSIDE_CENTER",
            gate,
            counters);
    }

    private static void RunCandidate(
        EnemyTraceFrame currentFrame,
        LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
        int preferred,
        int slot,
        string sourcePrefix,
        LadyBugEnemyDecisionGate427EModel.Result decisionGate,
        Counters counters)
    {
        if (!TraceVramTileReader.TryCreate(currentFrame, scratch, out TraceVramTileReader? tileReader) || tileReader == null)
        {
            counters.SkippedMissingVram++;
            return;
        }

        counters.DecisionCenterCandidates++;
        counters.Checks++;

        string decisionSource;
        if (decisionGate.CarrySet)
        {
            counters.DecisionGateCarrySet++;
            LadyBugEnemyDecisionModel.DirectionAttemptResult decision =
                LadyBugEnemyDecisionModel.TryPreferredDirection(
                    scratch,
                    preferred,
                    LadyBugStaticMazeRomTable.Table0DA2,
                    tileReader.ReadTileAtProbe);

            decisionSource = decision.Source;
            CountDecisionKind(counters, decision.Source);
        }
        else
        {
            counters.DecisionGateCarryClear++;
            counters.OutsideCenterPath++;

            bool forcedReversal = LadyBugEnemyDecisionModel.CheckDoorForcedReversal(
                scratch.TempDir,
                scratch.TempX,
                scratch.TempY,
                tileReader.ReadTileAtProbe);

            if (forcedReversal)
            {
                scratch.TempDir = LadyBugEnemyDecisionModel.ReverseDirection(scratch.TempDir);
                counters.ForcedReversalApplied++;
                decisionSource = "433A_OUTSIDE_CENTER_FORCED_REVERSAL";
            }
            else
            {
                decisionSource = "433A_OUTSIDE_CENTER_KEEP_DIRECTION";
            }
        }

        LadyBugEnemyDecisionModel.ApplyEnemyTempMovementStep(scratch);

        counters.TileReads += tileReader.ReadCount;
        counters.TileAddressOutOfRange += tileReader.AddressOutOfRangeCount;
        Count(counters.Sources, sourcePrefix + ":" + decisionSource);
        Count(counters.Sources, "427E:" + decisionGate.Source);
        CountFirstTileByBranch(counters, tileReader);

        var enemyWork = currentFrame.enemyWork;
        if (enemyWork == null)
            return;

        int referenceRejected = enemyWork.rejectedMask & 0x0F;
        int referenceFallback = enemyWork.fallbackMask & 0xFF;
        int referenceDir = enemyWork.tempDir & 0x0F;
        int referenceX = enemyWork.tempX & 0xFF;
        int referenceY = enemyWork.tempY & 0xFF;

        int modeledRejected = scratch.RejectedDirMask & 0x0F;
        int modeledFallback = scratch.FallbackHelper & 0xFF;
        int modeledDir = scratch.TempDir & 0x0F;
        int modeledX = scratch.TempX & 0xFF;
        int modeledY = scratch.TempY & 0xFF;

        bool matched =
            modeledRejected == referenceRejected &&
            modeledFallback == referenceFallback &&
            modeledDir == referenceDir &&
            modeledX == referenceX &&
            modeledY == referenceY &&
            tileReader.AddressOutOfRangeCount == 0;

        string context =
            "tick=" + currentFrame.frame +
            " mameFrame=" + currentFrame.mameFrame +
            " slot=" + slot.ToString(CultureInfo.InvariantCulture) +
            " source=" + sourcePrefix +
            " gate=" + decisionGate.Source + "/" + decisionGate.Helper +
            " gateCompare=" + decisionGate.CompareCount.ToString(CultureInfo.InvariantCulture) + ":" + FormatByte(decisionGate.FinalA) + "/" + FormatByte(decisionGate.FinalB) +
            " start=" + FormatByte(tileReader.StartDir) + ":" + FormatByte(tileReader.StartX) + "," + FormatByte(tileReader.StartY) +
            " preferred=" + FormatByte(preferred) +
            " ref=" + FormatByte(referenceRejected) + ":" + FormatByte(referenceFallback) + ":" + FormatByte(referenceDir) + ":" + FormatByte(referenceX) + "," + FormatByte(referenceY) +
            " model=" + FormatByte(modeledRejected) + ":" + FormatByte(modeledFallback) + ":" + FormatByte(modeledDir) + ":" + FormatByte(modeledX) + "," + FormatByte(modeledY) +
            " tileReads=" + tileReader.DescribeReads();

        if (matched)
        {
            counters.Matches++;
            if (string.IsNullOrEmpty(counters.FirstMatch))
                counters.FirstMatch = context;
            return;
        }

        counters.Mismatches++;
        if (string.IsNullOrEmpty(counters.FirstMismatch))
            counters.FirstMismatch = context;
    }

    private static void CountDecisionKind(Counters counters, string source)
    {
        switch (source)
        {
            case "42E6_PREFERRED_ACCEPTED":
                counters.PreferredAccepted++;
                break;
            case "4315_PREFERRED_REJECTED_CURRENT_KEPT":
                counters.PreferredRejectedCurrentKept++;
                break;
            case "4331_FALLBACK_SELECTED":
                counters.FallbackSelected++;
                break;
            case "4331_FALLBACK_NOT_FOUND":
                counters.FallbackNotFound++;
                break;
        }
    }

    private static void CountFirstTileByBranch(Counters counters, TraceVramTileReader reader)
    {
        foreach (TraceVramTileReader.TileRead read in reader.Reads)
        {
            string key = read.Branch + ":tile=" + FormatByte(read.Tile);
            Count(counters.FirstTileByBranch, key);
        }
    }

    private static bool TrySelectPreferred(EnemyTraceFrame frame, int slot, out int preferred)
    {
        preferred = 0;

        if (frame.enemyWork == null || frame.enemyWork.preferred.Count <= slot || slot < 0)
            return false;

        preferred = frame.enemyWork.preferred[slot] & 0x0F;
        return preferred != 0;
    }

    private static bool TrySelectEnemyWorkSlot(EnemyTraceFrame frame, out EnemyTraceActor? selected)
    {
        selected = null;

        if (frame.enemies == null || frame.enemyWork == null)
            return false;

        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (!enemy.active || !enemy.HasKnownPosition)
                continue;

            if (enemy.x == frame.enemyWork.tempX && enemy.y == frame.enemyWork.tempY)
            {
                selected = enemy;
                return true;
            }
        }

        EnemyTraceActor? onlyActive = null;
        int activeCount = 0;
        foreach (EnemyTraceActor enemy in frame.enemies)
        {
            if (!enemy.active || !enemy.HasKnownPosition)
                continue;

            activeCount++;
            onlyActive = enemy;
        }

        if (activeCount == 1)
        {
            selected = onlyActive;
            return true;
        }

        return false;
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

    private static void Count(Dictionary<string, int> counts, string key)
    {
        if (!counts.TryGetValue(key, out int count))
            count = 0;

        counts[key] = count + 1;
    }

    private static string BuildSummaryText(Counters counters)
    {
        var builder = new StringBuilder();
        builder.Append("Lady Bug source-first 0x4130 local-door shadow ").Append(Version).Append(": ");
        builder.Append("frames=").Append(counters.Frames);
        builder.Append(", transitions=").Append(counters.Transitions);
        builder.Append(", releaseActivationTransitions=").Append(counters.ReleaseActivationTransitions);
        builder.Append(", decisionCenterCandidates=").Append(counters.DecisionCenterCandidates);
        builder.Append(", pixelAlignedCandidates=").Append(counters.PixelAlignedCandidates);
        builder.Append(", decisionGateCarrySet=").Append(counters.DecisionGateCarrySet);
        builder.Append(", decisionGateCarryClear=").Append(counters.DecisionGateCarryClear);
        builder.Append(", outsideCenterPath=").Append(counters.OutsideCenterPath);
        builder.Append(", forcedReversalApplied=").Append(counters.ForcedReversalApplied);
        builder.Append(", checks=").Append(counters.Checks);
        builder.Append(", matches=").Append(counters.Matches);
        builder.Append(", mismatches=").Append(counters.Mismatches);
        builder.Append(", tileReads=").Append(counters.TileReads);
        builder.Append(", tileAddressOutOfRange=").Append(counters.TileAddressOutOfRange);
        builder.Append(", preferredAccepted=").Append(counters.PreferredAccepted);
        builder.Append(", preferredRejectedCurrentKept=").Append(counters.PreferredRejectedCurrentKept);
        builder.Append(", fallbackSelected=").Append(counters.FallbackSelected);
        builder.Append(", fallbackNotFound=").Append(counters.FallbackNotFound);
        builder.Append(", skippedMissingEnemyWork=").Append(counters.SkippedMissingEnemyWork);
        builder.Append(", skippedMissingPreferred=").Append(counters.SkippedMissingPreferred);
        builder.Append(", skippedMissingVram=").Append(counters.SkippedMissingVram);
        builder.Append(", skippedNonCenter=").Append(counters.SkippedNonCenter);
        builder.Append(", skippedNoActiveEnemy=").Append(counters.SkippedNoActiveEnemy);
        builder.Append(", skippedPreviousSlotMissing=").Append(counters.SkippedPreviousSlotMissing);
        builder.Append(", skippedPreviousSlotInactive=").Append(counters.SkippedPreviousSlotInactive);
        builder.Append(", skippedPreviousSlotInvalidDirection=").Append(counters.SkippedPreviousSlotInvalidDirection);

        if (!string.IsNullOrEmpty(counters.FirstMatch))
            builder.Append(", firstMatch: ").Append(counters.FirstMatch);

        if (!string.IsNullOrEmpty(counters.FirstMismatch))
            builder.Append(", firstMismatch: ").Append(counters.FirstMismatch);

        builder.Append(", sources: ").Append(DescribeDictionary(counters.Sources));
        builder.Append(", tiles: ").Append(DescribeDictionary(counters.FirstTileByBranch));

        if (counters.Checks == 0 && counters.SkippedMissingVram > 0)
        {
            builder.Append(". NOTE: no authoritative 0x4130 shadow checks ran because the loaded JSONL trace has no per-frame vramD000_D3FF block. Run the standard trace with includeFullMemoryEachFrame=true to enable this diagnostic.");
        }
        else
        {
            builder.Append(". NOTE: shadow-only; normal cycles load scratch from the previous slot state following 0x43F0..0x4405 and use the full 0x427E carry gate before choosing between 0x42E0 preferred-decision and 0x433A outside-center logic; authoritative enemy direction/rejectedMask remain reference-synced.");
        }

        return builder.ToString();
    }

    private static string DescribeDictionary(Dictionary<string, int> values)
    {
        if (values.Count == 0)
            return "none";

        var parts = new List<string>();
        foreach (KeyValuePair<string, int> pair in values)
            parts.Add(pair.Key + "=" + pair.Value.ToString(CultureInfo.InvariantCulture));

        return string.Join("; ", parts);
    }

    private static string FormatByte(int value)
    {
        return (value & 0xFF).ToString("X2", CultureInfo.InvariantCulture);
    }

    private static string FormatWord(int value)
    {
        return (value & 0xFFFF).ToString("X4", CultureInfo.InvariantCulture);
    }

    private sealed class TraceVramTileReader
    {
        private readonly byte[] _vram;
        private readonly List<TileRead> _reads = new();

        private TraceVramTileReader(byte[] vram, LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch)
        {
            _vram = vram;
            StartDir = scratch.TempDir & 0x0F;
            StartX = scratch.TempX & 0xFF;
            StartY = scratch.TempY & 0xFF;
        }

        public int StartDir { get; }
        public int StartX { get; }
        public int StartY { get; }
        public int ReadCount => _reads.Count;
        public int AddressOutOfRangeCount { get; private set; }
        public IReadOnlyList<TileRead> Reads => _reads;

        public static bool TryCreate(
            EnemyTraceFrame frame,
            LadyBugEnemyDecisionModel.EnemyDecisionScratch scratch,
            out TraceVramTileReader? reader)
        {
            reader = null;
            string? text = frame.rawMemory?.vramD000_D3FF;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            if (!TryParseHexBytes(text, VramLength, out byte[]? bytes) || bytes == null)
                return false;

            reader = new TraceVramTileReader(bytes, scratch);
            return true;
        }

        public int ReadTileAtProbe(int x, int y)
        {
            int address = Compute3C0aAddress(x, y);
            int offset = address - VramBase;
            string branch = ClassifyBranch(StartDir, StartX, StartY, x, y);

            if (offset < 0 || offset >= _vram.Length)
            {
                AddressOutOfRangeCount++;
                _reads.Add(new TileRead(branch, x & 0xFF, y & 0xFF, address, 0xFF, false));
                return 0xFF;
            }

            int tile = _vram[offset] & 0xFF;
            _reads.Add(new TileRead(branch, x & 0xFF, y & 0xFF, address, tile, true));
            return tile;
        }

        public string DescribeReads()
        {
            if (_reads.Count == 0)
                return "none";

            var parts = new List<string>();
            foreach (TileRead read in _reads)
            {
                parts.Add(read.Branch + "@" + FormatWord(read.Address) +
                          "=" + FormatByte(read.Tile) +
                          " probe=" + FormatByte(read.X) + "," + FormatByte(read.Y));
            }

            return string.Join("/", parts);
        }

        private static int Compute3C0aAddress(int x, int y)
        {
            return 0xD0A0 + (((x & 0xFF) & 0xF8) * 4) + (((y & 0xFF) >> 3) & 0x1F);
        }

        private static string ClassifyBranch(int startDir, int startX, int startY, int probeX, int probeY)
        {
            int dx = ((probeX & 0xFF) - (startX & 0xFF)) & 0xFF;
            int dy = ((probeY & 0xFF) - (startY & 0xFF)) & 0xFF;

            if (dx == 0xFF)
                return "left";
            if (dx == 0x08)
                return "right";
            if (dy == 0xF9)
                return "up";
            if (dy == 0x02)
                return "down";

            return "dir=" + FormatByte(startDir);
        }

        private static bool TryParseHexBytes(string text, int expectedLength, out byte[]? bytes)
        {
            bytes = null;
            string compact = text.Trim();
            if (compact.Length < expectedLength * 2)
                return false;

            var result = new byte[expectedLength];
            for (int i = 0; i < expectedLength; i++)
            {
                string part = compact.Substring(i * 2, 2);
                if (!byte.TryParse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte value))
                    return false;

                result[i] = value;
            }

            bytes = result;
            return true;
        }

        public readonly struct TileRead
        {
            public TileRead(string branch, int x, int y, int address, int tile, bool inRange)
            {
                Branch = branch;
                X = x & 0xFF;
                Y = y & 0xFF;
                Address = address & 0xFFFF;
                Tile = tile & 0xFF;
                InRange = inRange;
            }

            public string Branch { get; }
            public int X { get; }
            public int Y { get; }
            public int Address { get; }
            public int Tile { get; }
            public bool InRange { get; }
        }
    }
}
