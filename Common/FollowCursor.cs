using Godot;

namespace PetWobble.Const.Common;

public partial class FollowCursor : Node2D
{
    public override void _PhysicsProcess(double delta)
    {
        Position = GetGlobalMousePosition();
    }
}