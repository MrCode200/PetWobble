using Godot;

namespace PetWobble.Components;

public partial class DragableComonent : Area2D
{
    [Export] public MoveComponent MoveComp;
    [Export] public AnimatedSprite2D Animator;
    public bool IsDragging;

    public override void _InputEvent(Viewport viewport, InputEvent @event, int shapeIdx)
    {
        if (@event is InputEventMouseButton mouseEvent &&
            mouseEvent.Pressed &&
            mouseEvent.ButtonIndex == MouseButton.Left)
        {
            IsDragging = true;
            MoveComp.IsRunning = false;
            Animator.Play(Const.Animations.Idle);
        }
        else if (@event is InputEventMouseButton mouseEvent2 &&
                 mouseEvent2.IsReleased() &&
                 mouseEvent2.ButtonIndex == MouseButton.Left)
        {
            IsDragging = false;
            MoveComp.IsRunning = true;
        }
}

    public override void _PhysicsProcess(double delta)
    {
        if (!IsDragging)
            return;
        
        DisplayServer.WindowSetPosition(
            (Vector2I)GetViewport().GetMousePosition()
        );
    }
}