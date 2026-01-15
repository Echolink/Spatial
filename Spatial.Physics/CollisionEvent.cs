using System.Numerics;

namespace Spatial.Physics;

/// <summary>
/// Represents a collision event between two entities.
/// This is passed to collision callbacks for game logic processing.
/// </summary>
public class CollisionEvent
{
    /// <summary>
    /// First entity involved in the collision
    /// </summary>
    public PhysicsEntity EntityA { get; }
    
    /// <summary>
    /// Second entity involved in the collision
    /// </summary>
    public PhysicsEntity EntityB { get; }
    
    /// <summary>
    /// Contact point in world space
    /// </summary>
    public Vector3 ContactPoint { get; }
    
    /// <summary>
    /// Normal vector at contact point (points from A to B)
    /// </summary>
    public Vector3 Normal { get; }
    
    /// <summary>
    /// Penetration depth (how much the objects overlap)
    /// </summary>
    public float PenetrationDepth { get; }
    
    public CollisionEvent(PhysicsEntity entityA, PhysicsEntity entityB, Vector3 contactPoint, Vector3 normal, float penetrationDepth)
    {
        EntityA = entityA;
        EntityB = entityB;
        ContactPoint = contactPoint;
        Normal = normal;
        PenetrationDepth = penetrationDepth;
    }
}

/// <summary>
/// Delegate for collision event callbacks.
/// Called when two entities collide (after filtering).
/// </summary>
public delegate void CollisionEventHandler(CollisionEvent collisionEvent);

