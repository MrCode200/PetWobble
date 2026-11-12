using System;
using Godot;

namespace PetWobble.Const.ai.tasks;

public partial class IsSpawnRotation : BTCondition
{
    [Export] public NodePath SoftBodyPath;
    [Export(PropertyHint.Range, "0,1,0.005")] public float ErrorTolerancePercent;
    
    private SoftBody.SoftBody _sb;
    
    public override void _Setup()
    {
        _sb = SceneRoot.GetNode<SoftBody.SoftBody>(SoftBodyPath);
        if (_sb is null)
            throw new InvalidCastException($"{nameof(SoftBodyPath)} is not a SoftBody Node");
    }

    public override Status _Tick(double delta)
    {
        return IsRotated() ? Status.Failure : Status.Success;
    }

    private bool IsRotated()
    {
        var error = MathF.Abs(_sb.SBApi.GetGlobalLocalAngleOffset()) / MathF.PI;
        return error <= ErrorTolerancePercent;
    }
}