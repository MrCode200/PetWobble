using System;
using System.Linq;
using Godot;

namespace PetWobble.Const.ai.tasks;

public partial class IsColliding : BTCondition
{
    [Export] public NodePath SoftBodyPath;
    
    private SoftBody.SoftBody _sb;
    
    public override void _Setup()
    {
        _sb = SceneRoot.GetNode<SoftBody.SoftBody>(SoftBodyPath);
        if (_sb is null)
            throw new InvalidCastException($"{nameof(SoftBodyPath)} is not a SoftBody Node");
    }

    public override Status _Tick(double delta)
    {
        return _sb.Points.Any(p => p.InContact) ? Status.Success : Status.Failure;
    }
}