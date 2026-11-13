using System;
using Godot;
using PetWobble.Config;

namespace PetWobble.Const.ai.tasks;

public partial class WobbleRollAction : BTAction
{
    [Export] public NodePath SoftBodyPath;
    [Export] public float Force;
    [Export] public float MinForce;
    [Export(PropertyHint.Range, "0,1,0.0005")] public float ErrorTolerancePercent;
    [Export] public bool Lerp;
    [Export] public int KNeighbours;
    
    private SoftBody.SoftBody _sb;
    private const float UpDegree = 4.712389f;
    
    public override void _Setup()
    {
        _sb = SceneRoot.GetNode<SoftBody.SoftBody>(SoftBodyPath);
        if (_sb is null)
            throw new InvalidCastException($"{nameof(SoftBodyPath)} is not a SoftBody Node");
        
        BTTaskRegistry.Register(this);
    }

    public override Status _Tick(double delta)
    {
        var angleOffset = _sb.SBApi.GetGlobalLocalAngleOffset();
        var error = MathF.Abs(angleOffset) / MathF.PI;
        if (error <= ErrorTolerancePercent)
            return Status.Success;
        
        ApplyRollForce(angleOffset, error);
        
        return Status.Running;
    }

    public void ApplyRollForce(float angleOffset, float errorPercent)
    {
        var dir = Vector2.Right;
        
        // Rotate Leftwards
        if (angleOffset < 0)
            dir = Vector2.Left;
        
        var points = _sb.SBApi.GetPointsAtRadians(
            UpDegree,
            KNeighbours,
            true
         );

        var force = Force;
        if (Lerp)
        {
            force = Mathf.Max(MinForce, Mathf.Lerp(0, Force, errorPercent));
        }        
        GD.Print($"Roll Dir {dir} | Froce {force}");
        
        _sb.SBApi.ApplyForce(points, dir * force);
    }
}