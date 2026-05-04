/// <summary>
/// Optional raw memory blocks decoded from the MAME trace.
/// These fields are large and are present only when the Lua trace script exports them.
/// </summary>
public sealed class EnemyTraceRawMemoryState
{
    public string? logicalMaze6200_62AF { get; set; }
    public string? ram6000_62AF { get; set; }
    public string? vramD000_D3FF { get; set; }
    public string? colorD400_D7FF { get; set; }

    public int LogicalMazeByteCount => CountHexBytes(logicalMaze6200_62AF);
    public int RamByteCount => CountHexBytes(ram6000_62AF);
    public int VramByteCount => CountHexBytes(vramD000_D3FF);
    public int ColorByteCount => CountHexBytes(colorD400_D7FF);

    private static int CountHexBytes(string? hex)
    {
        return string.IsNullOrWhiteSpace(hex) ? 0 : hex.Trim().Length / 2;
    }
}
