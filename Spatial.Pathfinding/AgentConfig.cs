namespace Spatial.Pathfinding;

/// <summary>
/// Configuration for pathfinding agents.
/// Different agent types (players, NPCs) can have different navigation capabilities.
/// </summary>
public class AgentConfig
{
    /// <summary>
    /// Agent radius in world units.
    /// Larger agents need wider paths.
    /// </summary>
    public float Radius { get; set; } = 0.5f;
    
    /// <summary>
    /// Agent height in world units.
    /// Used to determine if agent can fit through spaces.
    /// </summary>
    public float Height { get; set; } = 2.0f;
    
    /// <summary>
    /// Maximum height the agent can climb (step height).
    /// </summary>
    public float MaxClimb { get; set; } = 0.5f;
    
    /// <summary>
    /// Maximum slope angle the agent can walk on (in degrees).
    /// </summary>
    public float MaxSlope { get; set; } = 45.0f;
    
    /// <summary>
    /// Cell size for voxelization (XZ plane).
    /// Smaller values create more detailed navmeshes but take longer to build.
    /// </summary>
    public float CellSize { get; set; } = 0.3f;
    
    /// <summary>
    /// Cell height for voxelization (Y axis).
    /// Should be smaller than agent's step height.
    /// </summary>
    public float CellHeight { get; set; } = 0.2f;
    
    /// <summary>
    /// Maximum edge length in world units.
    /// Edges longer than this will be subdivided.
    /// </summary>
    public float EdgeMaxLength { get; set; } = 12.0f;
    
    /// <summary>
    /// Maximum error for edge simplification.
    /// Lower values create more accurate edges but more vertices.
    /// </summary>
    public float EdgeMaxError { get; set; } = 1.3f;
    
    /// <summary>
    /// Detail sampling distance.
    /// Controls detail mesh generation.
    /// </summary>
    public float DetailSampleDistance { get; set; } = 6.0f;
    
    /// <summary>
    /// Maximum detail sampling error.
    /// Controls accuracy of height detail on surfaces.
    /// </summary>
    public float DetailSampleMaxError { get; set; } = 1.0f;
    
    /// <summary>
    /// Default configuration for player characters.
    /// </summary>
    public static AgentConfig Player => new AgentConfig
    {
        Radius = 0.5f,
        Height = 2.0f,
        MaxClimb = 0.5f,
        MaxSlope = 45.0f,
        CellSize = 0.3f,
        CellHeight = 0.2f,
        EdgeMaxLength = 12.0f,
        EdgeMaxError = 1.3f,
        DetailSampleDistance = 6.0f,
        DetailSampleMaxError = 1.0f
    };
    
    /// <summary>
    /// Default configuration for NPCs.
    /// </summary>
    public static AgentConfig NPC => new AgentConfig
    {
        Radius = 0.4f,
        Height = 1.8f,
        MaxClimb = 0.3f,
        MaxSlope = 40.0f,
        CellSize = 0.3f,
        CellHeight = 0.2f,
        EdgeMaxLength = 12.0f,
        EdgeMaxError = 1.3f,
        DetailSampleDistance = 6.0f,
        DetailSampleMaxError = 1.0f
    };
}

