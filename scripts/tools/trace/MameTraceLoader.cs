using System;
using System.Collections.Generic;
using System.Text.Json;

/// <summary>
/// Loads the current Lady Bug MAME JSON / JSONL trace format into strongly typed
/// trace model objects used by the simulator UI.
/// </summary>
public static class MameTraceLoader
{
    private static readonly JsonDocumentOptions TraceJsonDocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
    };

    public static List<EnemyTraceFrame> Load(string path, string text)
    {
        return path.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase)
            ? ParseJsonLinesTrace(text)
            : ParseJsonTraceFile(text);
    }

    public static List<EnemyTraceFrame> ParseJsonTraceFile(string text)
    {
        using JsonDocument document = JsonDocument.Parse(text, TraceJsonDocumentOptions);
        JsonElement root = document.RootElement;

        var frames = new List<EnemyTraceFrame>();

        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("frames", out JsonElement framesElement))
        {
            foreach (JsonElement frameElement in framesElement.EnumerateArray())
                frames.Add(ParseFrame(frameElement));
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            frames.Add(ParseFrame(root));
        }

        return frames;
    }

    public static List<EnemyTraceFrame> ParseJsonLinesTrace(string text)
    {
        var frames = new List<EnemyTraceFrame>();
        string[] lines = text.Replace("\r\n", "\n").Split('\n');

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.Length == 0)
                continue;

            using JsonDocument document = JsonDocument.Parse(line, TraceJsonDocumentOptions);
            frames.Add(ParseFrame(document.RootElement));
        }

        return frames;
    }

    private static EnemyTraceFrame ParseFrame(JsonElement element)
    {
        var frame = new EnemyTraceFrame
        {
            schema = ReadString(element, "schema", string.Empty),
            frame = ReadInt(element, "frame", ReadInt(element, "tick", 0)),
            phase = ReadString(element, "phase", string.Empty),
            mameFrame = ReadInt(element, "mameFrame", -1),
            pc = ReadString(element, "pc", string.Empty),
            r = ReadString(element, "r", string.Empty),
            logicalMaze6200_62AF = ReadString(element, "logicalMaze6200_62AF", string.Empty)
        };

        if (element.TryGetProperty("player", out JsonElement playerElement))
            frame.player = ParseActor(playerElement, -1, true);

        if (element.TryGetProperty("enemies", out JsonElement enemiesElement) && enemiesElement.ValueKind == JsonValueKind.Array)
        {
            frame.enemies = new List<EnemyTraceActor>();
            foreach (JsonElement enemyElement in enemiesElement.EnumerateArray())
                frame.enemies.Add(ParseActor(enemyElement, ReadInt(enemyElement, "slot", 0), false));
        }

        if (element.TryGetProperty("gates", out JsonElement gatesElement) && gatesElement.ValueKind == JsonValueKind.Array)
        {
            frame.gates = new List<EnemyTraceGateState>();
            foreach (JsonElement gateElement in gatesElement.EnumerateArray())
                frame.gates.Add(ParseGate(gateElement));
        }

        if (element.TryGetProperty("enemyWork", out JsonElement enemyWorkElement) && enemyWorkElement.ValueKind == JsonValueKind.Object)
            frame.enemyWork = ParseEnemyWork(enemyWorkElement);

        if (element.TryGetProperty("timers", out JsonElement timersElement) && timersElement.ValueKind == JsonValueKind.Object)
            frame.timers = ParseTimers(timersElement);

        if (element.TryGetProperty("ports", out JsonElement portsElement) && portsElement.ValueKind == JsonValueKind.Object)
            frame.ports = ParsePorts(portsElement);

        return frame;
    }

    private static EnemyTraceActor ParseActor(JsonElement element, int defaultSlot, bool defaultActive)
    {
        int raw = ReadInt(element, "raw", -1);

        // In the arcade object layout, bit 1 marks the object as active / collision-enabled.
        // For older traces that do not expose collisionActive, use the raw byte when available.
        bool activeFromRaw = raw >= 0 ? (raw & 0x02) != 0 : defaultActive;
        bool activeFallback = ReadBool(element, "collisionActive", activeFromRaw);

        int turnTargetX = ReadInt(element, "turnTargetX", ReadInt(element, "targetX", -1));
        int turnTargetY = ReadInt(element, "turnTargetY", ReadInt(element, "targetY", -1));

        return new EnemyTraceActor
        {
            slot = ReadInt(element, "slot", defaultSlot),
            raw = raw,
            x = ReadInt(element, "x", 0),
            y = ReadInt(element, "y", 0),
            sprite = ReadInt(element, "sprite", -1),
            attr = ReadInt(element, "attr", -1),
            turnTargetX = turnTargetX,
            turnTargetY = turnTargetY,
            dir = ReadString(element, "dir", ReadString(element, "currentDir", string.Empty)),
            active = ReadBool(element, "active", activeFallback)
        };
    }

    private static EnemyTraceGateState ParseGate(JsonElement element)
    {
        int pivotX = -1;
        int pivotY = -1;

        if (element.TryGetProperty("pivot", out JsonElement pivotElement) && pivotElement.ValueKind == JsonValueKind.Object)
        {
            pivotX = ReadInt(pivotElement, "x", -1);
            pivotY = ReadInt(pivotElement, "y", -1);
        }
        else if (element.TryGetProperty("gatePivot", out JsonElement gatePivotElement) && gatePivotElement.ValueKind == JsonValueKind.Object)
        {
            pivotX = ReadInt(gatePivotElement, "x", -1);
            pivotY = ReadInt(gatePivotElement, "y", -1);
        }

        return new EnemyTraceGateState
        {
            gate_id = ReadInt(element, "gate_id", ReadInt(element, "godotGateId", ReadInt(element, "id", -1))),
            orientation = ReadString(element, "orientation", ReadString(element, "currentOrientation", "Unknown")),
            pivot_x = pivotX,
            pivot_y = pivotY
        };
    }

    private static EnemyTraceEnemyWorkState ParseEnemyWork(JsonElement element)
    {
        return new EnemyTraceEnemyWorkState
        {
            tempDir = ReadInt(element, "tempDir", -1),
            tempX = ReadInt(element, "tempX", -1),
            tempY = ReadInt(element, "tempY", -1),
            rejectedMask = ReadInt(element, "rejectedMask", -1),
            fallbackMask = ReadInt(element, "fallbackMask", -1),
            preferred = ReadIntArray(element, "preferred"),
            chaseTimers = ReadIntArray(element, "chaseTimers"),
            chaseRoundRobin = ReadInt(element, "chaseRoundRobin", -1)
        };
    }

    private static EnemyTraceTimersState ParseTimers(JsonElement element)
    {
        return new EnemyTraceTimersState
        {
            timer61B4 = ReadInt(element, "61B4", -1),
            timer61B5 = ReadInt(element, "61B5", -1),
            timer61B6 = ReadInt(element, "61B6", -1),
            timer61B7 = ReadInt(element, "61B7", -1),
            timer61B8 = ReadInt(element, "61B8", -1),
            timer61B9 = ReadInt(element, "61B9", -1),
            freeze61E1 = ReadInt(element, "freeze61E1", -1),
            collectibleColorCounter6199 = ReadInt(element, "collectibleColorCounter6199", -1)
        };
    }

    private static EnemyTracePortsState ParsePorts(JsonElement element)
    {
        return new EnemyTracePortsState
        {
            in0_9000 = ReadInt(element, "in0_9000", -1),
            in1_9001 = ReadInt(element, "in1_9001", -1),
            dsw0_9002 = ReadInt(element, "dsw0_9002", -1),
            dsw1_9003 = ReadInt(element, "dsw1_9003", -1)
        };
    }

    private static List<int> ReadIntArray(JsonElement element, string propertyName)
    {
        var result = new List<int>();

        if (!element.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.Array)
            return result;

        foreach (JsonElement item in value.EnumerateArray())
        {
            result.Add(item.ValueKind switch
            {
                JsonValueKind.Number when item.TryGetInt32(out int n) => n,
                JsonValueKind.String => ParseIntString(item.GetString(), -1),
                JsonValueKind.True => 1,
                JsonValueKind.False => 0,
                _ => -1
            });
        }

        return result;
    }

    private static int ReadInt(JsonElement element, string propertyName, int fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetInt32(out int n) => n,
            JsonValueKind.String => ParseIntString(value.GetString(), fallback),
            JsonValueKind.True => 1,
            JsonValueKind.False => 0,
            _ => fallback
        };
    }

    private static int ParseIntString(string? text, int fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        string value = text.Trim();

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return Convert.ToInt32(value[2..], 16);

        bool looksHex = value.Length <= 2;
        foreach (char c in value)
        {
            if (!Uri.IsHexDigit(c))
            {
                looksHex = false;
                break;
            }
        }

        if (looksHex)
            return Convert.ToInt32(value, 16);

        return int.TryParse(value, out int parsed) ? parsed : fallback;
    }

    private static string ReadString(JsonElement element, string propertyName, string fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString() ?? fallback,
            JsonValueKind.Number when value.TryGetInt32(out int n) => n.ToString("X2"),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => fallback
        };
    }

    private static bool ReadBool(JsonElement element, string propertyName, bool fallback)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
            return fallback;

        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out int n) => n != 0,
            JsonValueKind.String => ParseBoolString(value.GetString(), fallback),
            _ => fallback
        };
    }

    private static bool ParseBoolString(string? text, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(text))
            return fallback;

        if (bool.TryParse(text, out bool parsed))
            return parsed;

        return ParseIntString(text, fallback ? 1 : 0) != 0;
    }
}
