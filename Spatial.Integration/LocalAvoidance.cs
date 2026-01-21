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
    /// Enhanced to handle head-on collisions by steering perpendicular to collision path.
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
        var currentVel = _physicsWorld.GetEntityVelocity(entity);
        
        // Calculate multiple avoidance forces
        var separationForce = CalculateSeparationForce(currentPos, nearbyEntities);
        var collisionAvoidanceForce = CalculateCollisionAvoidance(
            currentPos, 
            currentVel, 
            desiredVelocity, 
            nearbyEntities
        );
        
        // Combine forces (collision avoidance is stronger for imminent collisions)
        var totalAvoidance = separationForce * _avoidanceStrength + collisionAvoidanceForce * (_avoidanceStrength * 1.5f);
        
        // Blend desired velocity with avoidance forces
        var adjustedVelocity = desiredVelocity + totalAvoidance;
        
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
    /// Predicts if the current path will collide with another entity's path.
    /// Returns collision information if a collision is predicted.
    /// </summary>
    public CollisionPrediction? PredictPathCollision(
        Vector3 currentPos,
        Vector3 currentVel,
        Vector3 nextWaypoint,
        PhysicsEntity otherEntity)
    {
        var otherPos = _physicsWorld.GetEntityPosition(otherEntity);
        var otherVel = _physicsWorld.GetEntityVelocity(otherEntity);
        
        // Calculate our movement direction
        var ourDirection = nextWaypoint - currentPos;
        var ourDistance = ourDirection.Length();
        
        if (ourDistance < 0.01f)
            return null; // No movement
        
        ourDirection = Vector3.Normalize(ourDirection);
        
        // Check if we're moving toward each other (head-on)
        var toOther = otherPos - currentPos;
        var distance = toOther.Length();
        
        if (distance < 0.01f || distance > _avoidanceRadius)
            return null; // Too close or too far
        
        var toOtherNormalized = Vector3.Normalize(toOther);
        
        // Check if other agent is in our path
        var dotProduct = Vector3.Dot(ourDirection, toOtherNormalized);
        if (dotProduct < 0.5f)
            return null; // Not in our path
        
        // Check relative velocity - are we on collision course?
        var relativeVelocity = currentVel - otherVel;
        var relativeSpeed = relativeVelocity.Length();
        
        if (relativeSpeed < 0.1f)
            return null; // Not moving relative to each other
        
        // Predict time to collision
        var timeToCollision = distance / (relativeSpeed + 0.1f);
        
        // Check if other agent is also moving toward us (mutual collision)
        var otherToUs = -toOther;
        var otherDirection = Vector3.Normalize(otherVel);
        var otherDotProduct = Vector3.Dot(otherDirection, Vector3.Normalize(otherToUs));
        
        bool isHeadOn = otherDotProduct > 0.5f; // Other agent is also moving toward us
        
        // Only predict collision if imminent (less than 2 seconds)
        if (timeToCollision > 2.0f)
            return null;
        
        return new CollisionPrediction
        {
            OtherEntity = otherEntity,
            TimeToCollision = timeToCollision,
            CollisionDistance = distance,
            IsHeadOn = isHeadOn,
            ShouldReplan = isHeadOn && timeToCollision < 1.5f // Replan for imminent head-on collisions
        };
    }
    
    /// <summary>
    /// Gets all predicted collisions with nearby entities.
    /// </summary>
    public List<CollisionPrediction> PredictCollisions(
        Vector3 currentPos,
        Vector3 currentVel,
        Vector3 nextWaypoint,
        List<PhysicsEntity> nearbyEntities)
    {
        var predictions = new List<CollisionPrediction>();
        
        foreach (var entity in nearbyEntities)
        {
            var prediction = PredictPathCollision(currentPos, currentVel, nextWaypoint, entity);
            if (prediction != null)
            {
                predictions.Add(prediction);
            }
        }
        
        return predictions.OrderBy(p => p.TimeToCollision).ToList();
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
    
    /// <summary>
    /// Calculates collision avoidance force to steer perpendicular to collision path.
    /// This handles head-on collisions by moving sideways instead of trying to push through.
    /// </summary>
    private Vector3 CalculateCollisionAvoidance(
        Vector3 currentPos,
        Vector3 currentVel,
        Vector3 desiredVelocity,
        List<PhysicsEntity> nearbyEntities)
    {
        var avoidanceForce = Vector3.Zero;
        
        // Only apply collision avoidance if we're moving
        if (desiredVelocity.Length() < 0.1f)
            return avoidanceForce;
        
        var moveDirection = Vector3.Normalize(desiredVelocity);
        
        foreach (var other in nearbyEntities)
        {
            var otherPos = _physicsWorld.GetEntityPosition(other);
            var otherVel = _physicsWorld.GetEntityVelocity(other);
            
            var toOther = otherPos - currentPos;
            var distance = toOther.Length();
            
            if (distance < 0.01f || distance > _avoidanceRadius)
                continue;
            
            var toOtherNormalized = Vector3.Normalize(toOther);
            
            // Check if other agent is in our path (dot product > 0.5 means within ~60 degrees)
            var dotProduct = Vector3.Dot(moveDirection, toOtherNormalized);
            if (dotProduct < 0.3f)
                continue; // Not in our path
            
            // Check relative velocity - are we on collision course?
            var relativeVelocity = currentVel - otherVel;
            var relativeSpeed = relativeVelocity.Length();
            
            // Predict time to collision
            var timeToCollision = distance / (relativeSpeed + 0.1f);
            
            // Only avoid imminent collisions (less than 2 seconds away)
            if (timeToCollision > 2.0f)
                continue;
            
            // Calculate perpendicular steering direction
            // Use the right-hand perpendicular in XZ plane
            var perpendicularDir = new Vector3(moveDirection.Z, 0, -moveDirection.X);
            
            // Choose which side to steer based on relative position
            // If other agent is on the right, steer left (and vice versa)
            var rightDot = Vector3.Dot(perpendicularDir, toOtherNormalized);
            if (rightDot < 0)
            {
                perpendicularDir = -perpendicularDir; // Flip to steer the other way
            }
            
            // Strength based on proximity and time to collision
            var urgency = (1.0f - (distance / _avoidanceRadius)) * (1.0f / (timeToCollision + 0.1f));
            var clampedUrgency = Math.Min(urgency, 5.0f); // Cap maximum urgency
            
            avoidanceForce += perpendicularDir * clampedUrgency;
        }
        
        return avoidanceForce;
    }
}
