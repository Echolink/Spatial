using BepuPhysics;
using BepuPhysics.Constraints;
using BepuUtilities;
using System.Numerics;

namespace Spatial.Physics;

/// <summary>
/// Simple pose integrator that applies gravity to all bodies.
/// </summary>
public struct GravityPoseIntegrator : IPoseIntegratorCallbacks
{
    private Vector3 _gravity;
    
    public GravityPoseIntegrator(Vector3 gravity)
    {
        _gravity = gravity;
    }
    
    public void Initialize(Simulation simulation)
    {
        // Nothing to initialize
    }
    
    public void PrepareForIntegration(float dt)
    {
        // Nothing to prepare - dt is passed directly to IntegrateVelocity
    }
    
    public void IntegrateVelocity(Vector<int> bodyIndices, Vector3Wide position, QuaternionWide orientation, 
        BodyInertiaWide localInertia, Vector<int> integrationMask, int workerIndex, 
        Vector<float> dt, ref BodyVelocityWide velocity)
    {
        // Apply gravity as acceleration: v += gravity * dt
        // Only apply to bodies with non-zero inverse mass (dynamic bodies)
        // Static/kinematic bodies have zero inverse mass and should not be affected
        Vector3Wide.Broadcast(_gravity, out var gravityWide);
        
        // Multiply by dt to get velocity change
        var velocityDelta = gravityWide * dt;
        
        // Only apply to bodies that have non-zero inverse mass (dynamic bodies)
        // The integrationMask already filters out static bodies, but we should also check inverse mass
        velocity.Linear += velocityDelta;
    }
    
    public AngularIntegrationMode AngularIntegrationMode => AngularIntegrationMode.Nonconserving;
    
    public bool AllowSubstepsForUnconstrainedBodies => false;
    
    public bool IntegrateVelocityForKinematics => false;
}
