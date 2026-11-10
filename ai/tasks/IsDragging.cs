using System;
using Godot;
using PetWobble.SoftBody.Extensions;

namespace PetWobble.Const.ai.tasks;

public partial class IsDragging : BTCondition
{
    [Export] public NodePath MouseSBDraggerPath;
    private MouseSoftBodyDragger _mouseSBDragger;
    
    public override void _Setup()
    {
        _mouseSBDragger = SceneRoot.GetNode<MouseSoftBodyDragger>(MouseSBDraggerPath);
        if (_mouseSBDragger is null)
            throw new InvalidCastException($"{nameof(MouseSBDraggerPath)} needs a MouseSoftBodyDragger Node");
    }

    public override Status _Tick(double delta)
    {
        return _mouseSBDragger.IsDraggingSB ? Status.Success : Status.Failure;
    }
}