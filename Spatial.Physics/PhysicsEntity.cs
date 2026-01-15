using BepuPhysics;
using BepuPhysics.Collidables;

namespace Spatial.Physics;

/// <summary>
/// Represents a game entity with a physics body.
/// This class maps game entities (players, NPCs, objects) to BepuPhysics bodies.
/// 
/// BepuPhysics v2 uses different handle types:
/// - BodyHandle for dynamic bodies (in simulation.Bodies)
/// - StaticHandle for static bodies (in simulation.Statics)
/// </summary>
public class PhysicsEntity
{
    /// <summary>
    /// Unique identifier for this entity (from game server)
    /// </summary>
    public int EntityId { get; }
    
    /// <summary>
    /// Type of entity (Player, NPC, StaticObject)
    /// Used for collision filtering
    /// </summary>
    public EntityType EntityType { get; }
    
    /// <summary>
    /// Handle to the physics body (dynamic)
    /// Only valid if IsStatic = false
    /// </summary>
    public BodyHandle BodyHandle { get; }
    
    /// <summary>
    /// Handle to the static body
    /// Only valid if IsStatic = true
    /// </summary>
    public StaticHandle StaticHandle { get; }
    
    /// <summary>
    /// The shape index used by this entity
    /// Stored for navmesh generation and other queries
    /// </summary>
    public TypedIndex ShapeIndex { get; }
    
    /// <summary>
    /// Whether this entity is static (immovable) or dynamic (affected by forces)
    /// </summary>
    public bool IsStatic { get; }
    
    public PhysicsEntity(int entityId, EntityType entityType, BodyHandle bodyHandle, TypedIndex shapeIndex)
    {
        EntityId = entityId;
        EntityType = entityType;
        BodyHandle = bodyHandle;
        ShapeIndex = shapeIndex;
        IsStatic = false;
    }
    
    public PhysicsEntity(int entityId, EntityType entityType, StaticHandle staticHandle, TypedIndex shapeIndex)
    {
        EntityId = entityId;
        EntityType = entityType;
        StaticHandle = staticHandle;
        ShapeIndex = shapeIndex;
        IsStatic = true;
    }
}

