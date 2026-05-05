using System.Text.Json.Serialization;

/// <summary>
/// Persistent settings used by the Godot tool to launch MAME and the Lady Bug Lua tracer.
/// The file is intentionally JSON rather than a .bat file so paths and capture options can be edited
/// without coupling them to a Windows command wrapper.
/// </summary>
public sealed class MameTraceSettings
{
    [JsonPropertyName("mameExecutable")]
    public string MameExecutable { get; set; } = string.Empty;

    [JsonPropertyName("game")]
    public string Game { get; set; } = "ladybug";

    [JsonPropertyName("romPath")]
    public string RomPath { get; set; } = string.Empty;

    [JsonPropertyName("stateDirectory")]
    public string StateDirectory { get; set; } = "res://tools/mame/states";

    /// <summary>
    /// MAME -statename value. In the old batch file this was STATE_SUBDIR.
    /// With stateDirectory=res://tools/mame/states and stateSubdir=ladybug,
    /// MAME will look for states under tools/mame/states/ladybug/.
    /// </summary>
    [JsonPropertyName("stateSubdir")]
    public string StateSubdir { get; set; } = "ladybug";

    /// <summary>
    /// Save-state name without .sta. If .sta is provided, the launcher strips it before writing
    /// the Lua runtime configuration because manager.machine:load expects the state name.
    /// </summary>
    [JsonPropertyName("saveState")]
    public string SaveState { get; set; } = "test1";

    [JsonPropertyName("luaScriptPath")]
    public string LuaScriptPath { get; set; } = "res://tools/mame/lua/ladybug_sequence_trace.lua";

    [JsonPropertyName("outputDirectory")]
    public string OutputDirectory { get; set; } = "res://traces/mame";

    [JsonPropertyName("outputPrefix")]
    public string OutputPrefix { get; set; } = "ladybug_sequence_v8";

    [JsonPropertyName("framesAfterTick0")]
    public int FramesAfterTick0 { get; set; } = 600;

    [JsonPropertyName("windowed")]
    public bool Windowed { get; set; } = true;

    /// <summary>
    /// Optional manual debugger flag.
    ///
    /// The launcher also auto-enables the debugger when LuaScriptPath points to
    /// tools/mame/lua/ladybug_preferred_pc_trace.lua, because that diagnostic
    /// script requires cpu.debug:bpset().
    ///
    /// This property is intentionally safe for older config files: if the JSON
    /// does not contain enableDebugger, the default is false.
    /// </summary>
    [JsonPropertyName("enableDebugger")]
    public bool EnableDebugger { get; set; }

    [JsonPropertyName("autobootDelay")]
    public int AutobootDelay { get; set; } = 1;

    [JsonPropertyName("exitWhenDone")]
    public bool ExitWhenDone { get; set; } = true;

    [JsonPropertyName("pauseWhenDone")]
    public bool PauseWhenDone { get; set; }

    [JsonPropertyName("flushEveryTraceLine")]
    public bool FlushEveryTraceLine { get; set; } = true;

    [JsonPropertyName("includeFullMemoryEachFrame")]
    public bool IncludeFullMemoryEachFrame { get; set; }

    [JsonPropertyName("includeLogicalMazeEachFrame")]
    public bool IncludeLogicalMazeEachFrame { get; set; } = true;
}
