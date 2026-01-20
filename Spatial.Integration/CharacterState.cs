namespace Spatial.Integration;

/// <summary>
/// State of a character controller for physics-pathfinding integration.
/// Determines how pathfinding and physics interact for an agent.
/// </summary>
public enum CharacterState
{
    /// <summary>
    /// Agent is on solid ground.
    /// Pathfinding controls XZ movement, physics handles Y (gravity).
    /// </summary>
    GROUNDED,
    
    /// <summary>
    /// Agent is in the air (falling, jumping, knocked back).
    /// Physics takes full control, pathfinding is paused.
    /// </summary>
    AIRBORNE,
    
    /// <summary>
    /// Agent recently landed and is stabilizing.
    /// Brief state before transitioning back to GROUNDED and replanning path.
    /// </summary>
    RECOVERING
}
