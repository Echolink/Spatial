using BepuPhysics;
using System.Collections.Generic;

namespace Spatial.Physics;

/// <summary>
/// Registry that maps physics body handles to game entities.
/// This allows us to look up entities when processing collisions or queries.
/// 
/// Why we need this:
/// - BepuPhysics only knows about BodyHandles/StaticHandles, not our game entities
/// - When collisions occur, we need to map handles back to entities
/// - Provides fast lookup by handle
/// </summary>
public class PhysicsEntityRegistry
{
    private readonly Dictionary<BodyHandle, PhysicsEntity> _dynamicEntitiesByHandle = new();
    private readonly Dictionary<StaticHandle, PhysicsEntity> _staticEntitiesByHandle = new();
    private readonly Dictionary<int, PhysicsEntity> _entitiesById = new();
    
    /// <summary>
    /// Registers an entity with its physics body handle.
    /// </summary>
    public void Register(PhysicsEntity entity)
    {
        if (entity.IsStatic)
        {
            _staticEntitiesByHandle[entity.StaticHandle] = entity;
        }
        else
        {
            _dynamicEntitiesByHandle[entity.BodyHandle] = entity;
        }
        _entitiesById[entity.EntityId] = entity;
    }
    
    /// <summary>
    /// Unregisters an entity.
    /// </summary>
    public void Unregister(PhysicsEntity entity)
    {
        if (entity.IsStatic)
        {
            _staticEntitiesByHandle.Remove(entity.StaticHandle);
        }
        else
        {
            _dynamicEntitiesByHandle.Remove(entity.BodyHandle);
        }
        _entitiesById.Remove(entity.EntityId);
    }
    
    /// <summary>
    /// Gets an entity by its body handle (for dynamic bodies).
    /// </summary>
    public PhysicsEntity? GetEntityByBodyHandle(BodyHandle handle)
    {
        _dynamicEntitiesByHandle.TryGetValue(handle, out var entity);
        return entity;
    }
    
    /// <summary>
    /// Gets an entity by its static handle (for static bodies).
    /// </summary>
    public PhysicsEntity? GetEntityByStaticHandle(StaticHandle handle)
    {
        _staticEntitiesByHandle.TryGetValue(handle, out var entity);
        return entity;
    }
    
    /// <summary>
    /// Gets an entity by its game entity ID.
    /// </summary>
    public PhysicsEntity? GetEntityById(int entityId)
    {
        _entitiesById.TryGetValue(entityId, out var entity);
        return entity;
    }
    
    /// <summary>
    /// Gets all registered entities.
    /// </summary>
    public IEnumerable<PhysicsEntity> GetAllEntities()
    {
        return _entitiesById.Values;
    }
    
    /// <summary>
    /// Clears all registered entities.
    /// </summary>
    public void Clear()
    {
        _dynamicEntitiesByHandle.Clear();
        _staticEntitiesByHandle.Clear();
        _entitiesById.Clear();
    }
}

