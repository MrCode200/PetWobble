using Godot;

namespace PetWobble.Const.Common;

public partial class MouseInteractionHandler : Node
{
    [Export] public float DragThreshold = 10f;

    [Signal] public delegate void ClickedEventHandler(InputEvent @event, Vector2 position);
    [Signal] public delegate void DragStartedEventHandler(InputEvent @event, Vector2 position);
    [Signal] public delegate void DragMovedEventHandler(InputEvent @event, Vector2 position);
    [Signal] public delegate void DragEndedEventHandler(InputEvent @event, Vector2 position);

    private bool _pressed;
    private bool _dragging;
    private MouseButton _activeBtn;
    private Vector2 _pressPos;

    public override void _Input(InputEvent @event)
    {
        // if mousebutton and press start tracking
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mouseClick)
        {
            if (!_pressed && mouseClick.Pressed) // guards against press from another btn
            {
                _pressed = true;
                _dragging = false;
                _activeBtn = mouseClick.ButtonIndex;
                _pressPos = mouseClick.Position;
            }
            else if (!mouseClick.Pressed && mouseClick.ButtonIndex == _activeBtn) // Release
            {
                _pressed = false;
                if (_dragging)
                {
                    _dragging = false;
                    EmitSignal(SignalName.DragEnded, @event, mouseClick.Position);
                }
                else
                {
                    EmitSignal(SignalName.Clicked, @event, mouseClick.Position);
                }
                    
            }
        }
        else if (@event is InputEventMouseMotion mouseMotion && _pressed)
        {
            if (!_dragging &&
                mouseMotion.Position.DistanceTo(_pressPos) >= DragThreshold)
            {
                _dragging = true;
                EmitSignal(SignalName.DragStarted, @event, mouseMotion.Position);
            }
            else if (_pressed && _dragging)
            {
                EmitSignal(SignalName.DragMoved, @event, mouseMotion.Position);
            }
        }
    }
}