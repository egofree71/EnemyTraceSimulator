using System;
using System.Collections.Generic;

/// <summary>
/// MAME-side local tile oracle for the source routine at 0x4130.
///
/// It uses rawMemory.vramD000_D3FF from the trace and the validated 0x3C0A tile
/// address formula:
///   HL = 0xD0A0 + ((D & 0xF8) * 4) + (E >> 3)
///
/// Direction-specific 0x4130 probes:
///   left  -> D = X - 1; reject tiles 3D / 3F
///   up    -> E = Y - 7; reject tiles 35 / 37
///   right -> D = X + 8; reject tiles 3F / 3D
///   down  -> E = Y + 2; reject tiles 35 / 37
/// </summary>
public sealed class LadyBugMameLocalTile4130Oracle
{
    private const string SourceName = "MAME local tile 0x4130/0x3C0A";
    private const int VramBase = 0xD000;
    private const int VramLength = 0x0400;

    private readonly byte[] _vram;

    private LadyBugMameLocalTile4130Oracle(byte[] vram)
    {
        _vram = vram;
    }

    public static bool TryCreate(EnemyTraceFrame frame, out LadyBugMameLocalTile4130Oracle? oracle)
    {
        oracle = null;
        string? text = frame.rawMemory?.vramD000_D3FF;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        if (!TryParseHexBytes(text, VramLength, out byte[]? bytes) || bytes == null)
            return false;

        oracle = new LadyBugMameLocalTile4130Oracle(bytes);
        return true;
    }

    public EnemyCollisionProbeResult Probe(int x, int y, int direction)
    {
        int dir = direction & 0x0F;
        int mameX = x & 0xFF;
        int mameY = y & 0xFF;
        int probeX = mameX;
        int probeY = mameY;
        string branch;
        int rejectA;
        int rejectB;

        if (dir == LadyBugDirectionBits.Left)
        {
            probeX = (mameX - 1) & 0xFF;
            branch = "4130-left-D=X-1-reject-3D-3F";
            rejectA = 0x3D;
            rejectB = 0x3F;
        }
        else if (dir == LadyBugDirectionBits.Up)
        {
            probeY = (mameY - 7) & 0xFF;
            branch = "4130-up-E=Y-7-reject-35-37";
            rejectA = 0x35;
            rejectB = 0x37;
        }
        else if (dir == LadyBugDirectionBits.Right)
        {
            probeX = (mameX + 8) & 0xFF;
            branch = "4130-right-D=X+8-reject-3F-3D";
            rejectA = 0x3F;
            rejectB = 0x3D;
        }
        else if (dir == LadyBugDirectionBits.Down)
        {
            probeY = (mameY + 2) & 0xFF;
            branch = "4130-down-E=Y+2-reject-35-37";
            rejectA = 0x35;
            rejectB = 0x37;
        }
        else
        {
            return EnemyCollisionProbeResult.InvalidDirection(SourceName, direction);
        }

        int address = Compute3C0aAddress(probeX, probeY);
        int offset = address - VramBase;
        if (offset < 0 || offset >= _vram.Length)
        {
            return new EnemyCollisionProbeResult
            {
                Allowed = false,
                BlockKind = "tile-address-out-of-range",
                Source = SourceName,
                Details =
                    $"{branch} mame=({mameX:X2},{mameY:X2}) probe=({probeX:X2},{probeY:X2}) " +
                    $"address=0x{address:X4} offset=0x{offset:X3} dir={LadyBugDirectionBits.ToLabel(direction)}"
            };
        }

        int tile = _vram[offset] & 0xFF;
        bool blocked = tile == rejectA || tile == rejectB;

        return new EnemyCollisionProbeResult
        {
            Allowed = !blocked,
            BlockKind = blocked ? "local-tile-4130" : "none",
            Source = SourceName,
            Details =
                $"{branch} mame=({mameX:X2},{mameY:X2}) probe=({probeX:X2},{probeY:X2}) " +
                $"address=0x{address:X4} offset=0x{offset:X3} tile={tile:X2} " +
                $"dir={LadyBugDirectionBits.ToLabel(direction)} " +
                (blocked ? "blocked by 0x4130 local tile rule" : "not blocked by 0x4130 local tile rule")
        };
    }

    public int ReadTileAtProbe(int x, int y)
    {
        int address = Compute3C0aAddress(x & 0xFF, y & 0xFF);
        int offset = address - VramBase;
        if (offset < 0 || offset >= _vram.Length)
            return 0xFF;

        return _vram[offset] & 0xFF;
    }

    private static int Compute3C0aAddress(int d, int e)
    {
        int alignedX = d & 0xF8;
        int columnOffset = alignedX * 4;
        int rowOffset = (e & 0xFF) >> 3;
        return 0xD0A0 + columnOffset + rowOffset;
    }

    private static bool TryParseHexBytes(string text, int expectedLength, out byte[]? bytes)
    {
        bytes = null;
        string hex = RemoveWhitespace(text);
        if ((hex.Length & 1) != 0)
            return false;

        int count = hex.Length / 2;
        if (count < expectedLength)
            return false;

        var result = new byte[expectedLength];
        for (int i = 0; i < expectedLength; i++)
        {
            string token = hex.Substring(i * 2, 2);
            if (!byte.TryParse(token, System.Globalization.NumberStyles.HexNumber, null, out byte value))
                return false;

            result[i] = value;
        }

        bytes = result;
        return true;
    }

    private static string RemoveWhitespace(string text)
    {
        var chars = new List<char>(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c))
                chars.Add(c);
        }

        return new string(chars.ToArray());
    }
}
