/// <summary>
/// Functional collision oracle used by the v0.9.x comparison work.
///
/// The interface is deliberately independent from the full enemy AI. It only
/// answers whether an enemy could move one pixel from a given arcade position in
/// a given enemy direction.
/// </summary>
public interface IEnemyMazeCollisionOracle
{
    EnemyCollisionProbeResult Probe(int x, int y, int direction);

    bool IsDecisionCenter(int x, int y);
}
