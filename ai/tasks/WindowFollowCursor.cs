using Godot;

namespace PetWobble.AI.Tasks;

[Tool]
public partial class WindowFollowCursor : BTAction
{
    [Export] public NodePath NodeToOverlap { get; set; }
    [Export] public Vector2 Offset { get; set; }
    private Node2D _nodeToOverlap;

    public override string _GenerateName()
    {
        return $"FollowCursor: ";
    }

    public override void _Setup()
    {
        _nodeToOverlap = Agent.GetNode<Node2D>(NodeToOverlap);
    }

    public override Status _Tick(double delta)
    {
        var targetPos = GetNewPosToTargetPos();
        
        DisplayServer.WindowSetPosition((Vector2I)targetPos);
        
        return Status.Success;
    }

    private Vector2 GetNewPosToTargetPos()
    {
        var targetPos = (Vector2)DisplayServer.MouseGetPosition();
        targetPos -= _nodeToOverlap.Position;
        targetPos += Offset;
        
        return targetPos;
    }
}