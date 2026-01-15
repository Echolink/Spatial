namespace Spatial.Integration.Commands;

/// <summary>
/// Command object for despawning entities from the physics world.
/// </summary>
public class DespawnEntityCommand
{
    /// <summary>
    /// ID of the entity to despawn
    /// </summary>
    public int EntityId { get; set; }
    
    /// <summary>
    /// Whether to run cleanup logic (e.g., drop items, trigger events)
    /// </summary>
    public bool RunCleanup { get; set; } = true;
}
