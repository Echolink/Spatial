using System.Numerics;
using Spatial.Physics;

namespace Spatial.Integration.Commands;

/// <summary>
/// Command object for spawning entities in the physics world.
/// Contains all necessary parameters for entity creation.
/// </summary>
public class SpawnEntityCommand
{
    /// <summary>
    /// Type of entity to spawn
    /// </summary>
    public EntityType EntityType { get; set; }
    
    /// <summary>
    /// Initial position in world space
    /// </summary>
    public Vector3 Position { get; set; }
    
    /// <summary>
    /// Shape type for collision
    /// </summary>
    public ShapeType ShapeType { get; set; }
    
    /// <summary>
    /// Mass of the entity (for dynamic bodies)
    /// </summary>
    public float Mass { get; set; } = 1.0f;
    
    /// <summary>
    /// Size parameters for the shape
    /// - For Box: (width, height, depth)
    /// - For Capsule: (radius, length, unused)
    /// - For Sphere: (radius, unused, unused)
    /// </summary>
    public Vector3 Size { get; set; } = new Vector3(1, 1, 1);
    
    /// <summary>
    /// Whether the entity is static (immovable)
    /// </summary>
    public bool IsStatic { get; set; } = false;
}

/// <summary>
/// Shape type for collision detection
/// </summary>
public enum ShapeType
{
    Box,
    Capsule,
    Sphere
}
