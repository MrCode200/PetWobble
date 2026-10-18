using Godot;

namespace PetWobble.AI.Tasks;

public partial class IsDragging : BTAction
{
    [Export] public NodePath Node;
    
    public Node2D TargetNode;

    public override void _Setup()
    {
        TargetNode = SceneRoot.GetNode<Node2D>(Node);
    }

    public override Status _Tick(double delta)
    {
        return Status.Success;
    }
}