namespace Spatial.Physics;

/// <summary>
/// Represents the type of entity in the physics world.
/// Used for collision filtering and entity management.
/// </summary>
public enum EntityType
{
    /// <summary>
    /// Player entity - can collide with world and NPCs, but not other players
    /// </summary>
    Player,
    
    /// <summary>
    /// NPC entity - can collide with world and players
    /// </summary>
    NPC,
    
    /// <summary>
    /// Static world object - immovable, used for level geometry
    /// </summary>
    StaticObject,
    
    /// <summary>
    /// Dynamic obstacle - can be temporary or permanent, blocks pathfinding
    /// </summary>
    Obstacle,
    
    /// <summary>
    /// Projectile entity - for projectiles, spells, etc.
    /// </summary>
    Projectile,
    
    /// <summary>
    /// Enemy entity - hostile NPCs
    /// </summary>
    Enemy,
    
    /// <summary>
    /// Temporary obstacle - used for temporary blockages that auto-despawn
    /// </summary>
    TemporaryObstacle
}

