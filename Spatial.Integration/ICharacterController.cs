using Spatial.Physics;
using Spatial.Pathfinding;
using System.Numerics;

namespace Spatial.Integration;

/// <summary>
/// Common interface for character controllers (velocity-based and motor-based).
/// Allows MovementController to work with either implementation.
/// </summary>
public interface ICharacterController
{
    /// <summary>
    /// Gets the current state of an entity.
    /// </summary>
    CharacterState GetState(PhysicsEntity entity);
    
    /// <summary>
    /// Checks if an entity is currently grounded.
    /// </summary>
    bool IsGrounded(PhysicsEntity entity);
    
    /// <summary>
    /// Checks if an entity is currently recovering (recently landed).
    /// </summary>
    bool IsRecovering(PhysicsEntity entity);
    
    /// <summary>
    /// Checks if an entity is currently airborne.
    /// </summary>
    bool IsAirborne(PhysicsEntity entity);
    
    /// <summary>
    /// Updates the grounded state for an entity based on physics.
    /// </summary>
    void UpdateGroundedState(PhysicsEntity entity, float deltaTime);
    
    /// <summary>
    /// Checks if an entity is stable (ready to resume pathfinding after landing).
    /// </summary>
    bool IsStable(PhysicsEntity entity);
    
    /// <summary>
    /// Applies grounding/movement control to keep agent on target path.
    /// </summary>
    void ApplyGroundingForce(PhysicsEntity entity, Vector3 moveDirection, float targetY, float agentHalfHeight);
    
    /// <summary>
    /// Applies idle grounding to keep stationary agent at current Y position.
    /// </summary>
    void ApplyIdleGrounding(PhysicsEntity entity);
    
    /// <summary>
    /// Notifies the controller that an entity has ground contact.
    /// </summary>
    void NotifyGroundContact(PhysicsEntity entity, PhysicsEntity groundEntity);
    
    /// <summary>
    /// Notifies the controller that an entity lost ground contact.
    /// </summary>
    void NotifyGroundContactRemoved(PhysicsEntity entity, PhysicsEntity groundEntity);
    
    /// <summary>
    /// Manually sets entity state to GROUNDED.
    /// </summary>
    void SetGrounded(PhysicsEntity entity);
    
    /// <summary>
    /// Manually sets entity state to AIRBORNE.
    /// </summary>
    void SetAirborne(PhysicsEntity entity);
    
    /// <summary>
    /// Cleans up state for a removed entity.
    /// </summary>
    void RemoveEntity(int entityId);

    /// <summary>
    /// Initiates kinematic arc traversal of an off-mesh link.
    /// For Teleport links, the entity is moved instantly and state becomes RECOVERING.
    /// </summary>
    void BeginLinkTraversal(PhysicsEntity entity, Vector3 entry, Vector3 exit, OffMeshLinkType type);

    /// <summary>
    /// Returns traversal type and normalized progress [0,1] for an entity currently in LINK_TRAVERSAL,
    /// or null if the entity is not traversing a link.
    /// </summary>
    (OffMeshLinkType Type, float T)? GetTraversalInfo(int entityId);
}
