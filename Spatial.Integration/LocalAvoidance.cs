using System.Numerics;
using Spatial.Physics;

namespace Spatial.Integration;

/// <summary>
/// Provides local obstacle avoidance using steering behaviors.
/// Helps entities navigate around nearby dynamic obstacles without full pathfinding replan.
/// 
/// Key features:
/// - Calculate steering forces to avoid nearby units
/// - Apply only when close to other moving units (within 3-5 units)
/// - Cheaper than full pathfinding replan
/// - Uses simple steering behaviors
/// </summary>
public class LocalAvoidance
{
    private readonly PhysicsWorld _physicsWorld;
    private readonly float _avoidanceRadius = 5.0f;
    private readonly float _separationRadius = 2.0f;
    private readonly float _avoidanceStrength = 2.0f;
    
    public LocalAvoidance(PhysicsWorld physicsWorld, float avoidanceRadius = 5.0f)
    {
        _physicsWorld = physicsWorld;
        _avoidanceRadius = avoidanceRadius;
    }
    
    /// <summary>
    /// Calculates an avoidance velocity adjustment to steer around nearby entities.
    /// </summary>
    /// <param name="entity">The entity to calculate avoidance for</param>
    /// <param name="desiredVelocity">The desired velocity toward the target</param>
    /// <param name="nearbyEntities">Nearby entities to avoid</param>
    /// <returns>Adjusted velocity with avoidance applied</returns>
    public Vector3 CalculateAvoidanceVelocity(
        PhysicsEntity entity,
        Vector3 desiredVelocity,
        List<PhysicsEntity> nearbyEntities)
    {
        if (nearbyEntities.Count == 0)
            return desiredVelocity;
        
        var currentPos = _physicsWorld.GetEntityPosition(entity);
        var separationForce = CalculateSeparationForce(currentPos, nearbyEntities);
        
        // Blend desired velocity with separation force
        var adjustedVelocity = desiredVelocity + separationForce * _avoidanceStrength;
        
        // Maintain original speed magnitude
        var desiredSpeed = desiredVelocity.Length();
        if (adjustedVelocity.Length() > 0.001f)
        {
            adjustedVelocity = Vector3.Normalize(adjustedVelocity) * desiredSpeed;
        }
        
        return adjustedVelocity;
    }
    
    /// <summary>
    /// Checks if local avoidance can handle the current situation.
    /// Returns false if obstacles are too dense or blocking the direct path.
    /// </summary>
    public bool CanAvoidLocally(Vector3 currentPos, Vector3 targetPos, List<PhysicsEntity> obstacles)
    {
        if (obstacles.Count == 0)
            return true;
        
        // Check if path is directly blocked
        var direction = targetPos - currentPos;
        var distance = direction.Length();
        
        if (distance < 0.01f)
            return true;
        
        var normalizedDir = Vector3.Normalize(direction);
        
        // Check for obstacles directly in the path
        int directBlockCount = 0;
        foreach (var obstacle in obstacles)
        {
            var obstaclePos = _physicsWorld.GetEntityPosition(obstacle);
            var toObstacle = obstaclePos - currentPos;
            
            // Check if obstacle is in front
            var dot = Vector3.Dot(normalizedDir, Vector3.Normalize(toObstacle));
            if (dot > 0.7f) // Within 45 degrees of forward direction
            {
                var obstacleDistance = toObstacle.Length();
                if (obstacleDistance < _separationRadius)
                {
                    directBlockCount++;
                }
            }
        }
        
        // If too many obstacles directly blocking, need full replan
        return directBlockCount < 3;
    }
    
    /// <summary>
    /// Gets nearby entities within the avoidance radius.
    /// </summary>
    public List<PhysicsEntity> GetNearbyEntities(Vector3 position, int excludeEntityId, int maxNeighbors = 5)
    {
        var nearbyEntities = _physicsWorld.GetEntitiesInRadius(position, _avoidanceRadius);
        
        return nearbyEntities
            .Where(e => e.EntityId != excludeEntityId && !e.IsStatic)
            .OrderBy(e => Vector3.Distance(_physicsWorld.GetEntityPosition(e), position))
            .Take(maxNeighbors)
            .ToList();
    }
    
    /// <summary>
    /// Calculates separation force to steer away from nearby entities.
    /// Uses inverse square law for stronger repulsion when closer.
    /// </summary>
    private Vector3 CalculateSeparationForce(Vector3 currentPos, List<PhysicsEntity> nearbyEntities)
    {
        var separationForce = Vector3.Zero;
        
        foreach (var other in nearbyEntities)
        {
            var otherPos = _physicsWorld.GetEntityPosition(other);
            var offset = currentPos - otherPos;
            var distance = offset.Length();
            
            if (distance < 0.01f)
                continue; // Skip if at same position
            
            // Calculate repulsion force (stronger when closer)
            var strength = 1.0f / (distance * distance + 0.1f); // Add small value to avoid division by zero
            
            // Only apply significant force within separation radius
            if (distance < _separationRadius)
            {
                separationForce += Vector3.Normalize(offset) * strength;
            }
        }
        
        return separationForce;
    }
}
