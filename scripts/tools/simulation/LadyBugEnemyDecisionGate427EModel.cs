using System;

/// <summary>
/// Source-first transcription of the 0x427E enemy decision gate.
///
/// This is deliberately separate from the older LadyBugEnemyDecisionModel.EnemyIsAtDecisionCenter()
/// helper.  The older helper only checks the two visible pixel-alignment tests:
///
///   0x427F..0x428B: (X & 0x0F) == 0x08 and (Y & 0x0F) == 0x06
///
/// The real arcade gate then continues through 0x428D..0x42B8:
///
///   - restore the candidate direction with EX AF,AF'
///   - choose the 0x377A or 0x36DA helper from direction bits
///   - iterate the helper bit field at 0x429F..0x42B3
///   - return carry set only at 0x42B6
///   - return carry clear at 0x42B8 otherwise
///
/// In the caller, 0x42DD uses this carry directly:
///   carry set   -> fall through to 0x42E0 / Enemy_TryPreferredDirection
///   carry clear -> jump to 0x433A / outside-center keep-or-reverse path
/// </summary>
public static class LadyBugEnemyDecisionGate427EModel
{
    // 0x0DFA table read by 0x377A when 0x427E uses the X-axis helper.
    // Ghidra bytes, little-endian words:
    //   0DFA:E5 05, 0DFC:FF 07, 0DFE:FF 07, 0E00:FE 07,
    //   0E02:DF 03, 0E04:75 05, 0E06:DF 03, 0E08:FE 07,
    //   0E0A:FF 07, 0E0C:BB 07, 0E0E:E5 05
    private static readonly int[] Table0DfaX =
    {
        0x05E5, 0x07FF, 0x07FF, 0x07FE, 0x03DF, 0x0575,
        0x03DF, 0x07FE, 0x07FF, 0x07BB, 0x05E5
    };

    // 0x0DE4 table read by 0x36DA when 0x427E uses the Y-axis helper.
    // Ghidra bytes, little-endian words:
    //   0DE4:77 07, 0DE6:DE 03, 0DE8:FD 05, 0DEA:DE 03,
    //   0DEC:FE 03, 0DEE:AF 07, 0DF0:FD 05, 0DF2:DF 07,
    //   0DF4:FF 07, 0DF6:DE 03, 0DF8:AF 07
    private static readonly int[] Table0De4Y =
    {
        0x0777, 0x03DE, 0x05FD, 0x03DE, 0x03FE, 0x07AF,
        0x05FD, 0x07DF, 0x07FF, 0x03DE, 0x07AF
    };

    public readonly struct Result
    {
        public Result(
            bool pixelAligned,
            bool carrySet,
            string source,
            string helper,
            int compareCount,
            int finalA,
            int finalB)
        {
            PixelAligned = pixelAligned;
            CarrySet = carrySet;
            Source = source;
            Helper = helper;
            CompareCount = compareCount;
            FinalA = finalA & 0xFF;
            FinalB = finalB & 0xFF;
        }

        public bool PixelAligned { get; }
        public bool CarrySet { get; }
        public string Source { get; }
        public string Helper { get; }
        public int CompareCount { get; }
        public int FinalA { get; }
        public int FinalB { get; }
    }

    /// <summary>
    /// Returns the source-faithful 0x427E decision-gate result.
    /// </summary>
    public static Result Evaluate(int direction, int x, int y)
    {
        int d = x & 0xFF;
        int e = y & 0xFF;
        int dir = direction & 0x0F;

        // 0x427F..0x428B: fast clear-carry return if the raw pixel alignment fails.
        if ((d & 0x0F) != 0x08 || (e & 0x0F) != 0x06)
        {
            return new Result(
                pixelAligned: false,
                carrySet: false,
                source: "427E_CARRY_CLEAR_PIXEL_ALIGNMENT",
                helper: "none",
                compareCount: 0,
                finalA: 0,
                finalB: 0);
        }

        // 0x428D EX AF,AF' restores the direction passed in A by the caller.
        // 0x428E AND 0x05 chooses between the two helper setup routines.
        HelperState state = (dir & 0x05) != 0
            ? SetupFrom36Da(y: e, b: d)  // 0x429A..0x429C: B=D, A=E, CALL 36DA.
            : SetupFrom377A(x: d, b: e); // 0x4292..0x4294: B=E, A=D, CALL 377A.

        // 0x429F..0x42B3.
        for (int guard = 0; guard < 64; guard++)
        {
            // SRL D; RR E.  The branch at 0x42A3 is taken when RR E shifts out bit 0.
            int carryFromD = state.D & 0x01;
            state.D = (state.D >> 1) & 0x7F;

            int carryFromE = state.E & 0x01;
            state.E = ((state.E >> 1) | (carryFromD << 7)) & 0xFF;

            if (carryFromE != 0)
            {
                state.CompareCount++;

                // 0x42AA CALL 3703; 0x3703 is CP B; CCF; RET.
                if (state.A == state.B)
                {
                    // 0x42AD JR Z,42B6; 0x42B6 SCF; RET.
                    return new Result(
                        pixelAligned: true,
                        carrySet: true,
                        source: "427E_CARRY_SET_HELPER_MATCH",
                        helper: state.Helper,
                        compareCount: state.CompareCount,
                        finalA: state.A,
                        finalB: state.B);
                }

                if (state.A > state.B)
                {
                    // CP B would be NC, CCF makes C, so 0x42AF jumps to 0x42B8.
                    return new Result(
                        pixelAligned: true,
                        carrySet: false,
                        source: "427E_CARRY_CLEAR_HELPER_OVERSHOT",
                        helper: state.Helper,
                        compareCount: state.CompareCount,
                        finalA: state.A,
                        finalB: state.B);
                }

                // A < B: neither Z nor C after CCF, so 0x42B1 SET 7,D and loop.
                state.D = (state.D | 0x80) & 0xFF;
            }

            // 0x42A6 ADD A,C, then loop to 0x429F.
            state.A = (state.A + state.C) & 0xFF;
        }

        // Guard path: should not happen in valid arcade states, but keep the diagnostic finite.
        return new Result(
            pixelAligned: true,
            carrySet: false,
            source: "427E_CARRY_CLEAR_GUARD_EXHAUSTED",
            helper: state.Helper,
            compareCount: state.CompareCount,
            finalA: state.A,
            finalB: state.B);
    }

    private static HelperState SetupFrom377A(int x, int b)
    {
        // 0x377A: A = X - 08; SRL A x4; SLA A; HL = 0DFA + A; DE=(HL); A=36; C=10.
        int tableIndex = (((x - 0x08) & 0xFF) >> 4);
        int word = ReadTableWord(Table0DfaX, tableIndex);
        return new HelperState(
            helper: "377A_X_TABLE_0DFA",
            a: 0x36,
            b: b,
            c: 0x10,
            d: (word >> 8) & 0xFF,
            e: word & 0xFF);
    }

    private static HelperState SetupFrom36Da(int y, int b)
    {
        // 0x36DA: A = Y - 36; SRL A x4; SLA A; HL = 0DE4 + A; DE=(HL); A=08; C=10.
        int tableIndex = (((y - 0x36) & 0xFF) >> 4);
        int word = ReadTableWord(Table0De4Y, tableIndex);
        return new HelperState(
            helper: "36DA_Y_TABLE_0DE4",
            a: 0x08,
            b: b,
            c: 0x10,
            d: (word >> 8) & 0xFF,
            e: word & 0xFF);
    }

    private static int ReadTableWord(int[] table, int index)
    {
        if (index < 0 || index >= table.Length)
            return 0;

        return table[index] & 0xFFFF;
    }

    private struct HelperState
    {
        public HelperState(string helper, int a, int b, int c, int d, int e)
        {
            Helper = helper;
            A = a & 0xFF;
            B = b & 0xFF;
            C = c & 0xFF;
            D = d & 0xFF;
            E = e & 0xFF;
            CompareCount = 0;
        }

        public string Helper { get; }
        public int A;
        public int B;
        public int C;
        public int D;
        public int E;
        public int CompareCount;
    }
}
