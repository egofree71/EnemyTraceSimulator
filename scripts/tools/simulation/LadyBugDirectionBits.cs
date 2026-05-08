using Godot;

/// <summary>
/// Shared Lady Bug enemy direction constants used by the v0.9.0a maze-collision
/// comparison helpers.
///
/// Enemy direction encoding, from the reverse-engineered enemy path:
/// 01 = left
/// 02 = up
/// 04 = right
/// 08 = down
///
/// Important: this is the enemy encoding. Do not mix it with the player input
/// direction byte used elsewhere in the arcade code.
/// </summary>
public static class LadyBugDirectionBits
{
    public const int None = 0x00;
    public const int Left = 0x01;
    public const int Up = 0x02;
    public const int Right = 0x04;
    public const int Down = 0x08;

    public static readonly int[] AllDirections =
    {
        Left,
        Up,
        Right,
        Down
    };

    /// <summary>
    /// Converts one enemy direction bit into a Godot-style one-pixel vector.
    /// </summary>
    public static Vector2I ToGodotVector(int direction)
    {
        return (direction & 0x0F) switch
        {
            Left => Vector2I.Left,
            Up => Vector2I.Up,
            Right => Vector2I.Right,
            Down => Vector2I.Down,
            _ => Vector2I.Zero
        };
    }

    public static string ToLabel(int direction)
    {
        return (direction & 0x0F) switch
        {
            Left => "01/left",
            Up => "02/up",
            Right => "04/right",
            Down => "08/down",
            _ => $"{direction & 0xFF:X2}/none"
        };
    }
}
