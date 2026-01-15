using System.Numerics;

namespace Spatial.Physics;

/// <summary>
/// Configuration settings for the physics simulation.
/// These settings control how BepuPhysics behaves.
/// </summary>
public class PhysicsConfiguration
{
    /// <summary>
    /// Fixed timestep for physics simulation in seconds.
    /// Using a fixed timestep ensures deterministic simulation, which is crucial for multiplayer games.
    /// Common values: 1/60s (60 FPS) or 1/120s (120 FPS)
    /// </summary>
    public float Timestep { get; set; } = 1f / 60f;
    
    /// <summary>
    /// Gravity vector for the simulation.
    /// Default is Earth gravity pointing downward (-Y axis).
    /// </summary>
    public Vector3 Gravity { get; set; } = new Vector3(0, -9.81f, 0);
    
    /// <summary>
    /// Maximum number of solver iterations per timestep.
    /// Higher values improve accuracy but reduce performance.
    /// Default is 8, which is usually sufficient for most games.
    /// </summary>
    public int SolverIterations { get; set; } = 8;
    
    /// <summary>
    /// Maximum number of substeps allowed per frame.
    /// If simulation falls behind, it can subdivide timesteps.
    /// </summary>
    public int MaxSubsteps { get; set; } = 4;
}

