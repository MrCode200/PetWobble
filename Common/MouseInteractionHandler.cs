using Godot;

namespace PetWobble.Const.Common;

public partial class MouseInteractionHandler : Node
{
	[Export] public float DragThreshold = 10f;

	[Signal] public delegate void ClickedEventHandler(InputEvent @event, Vector2 pressPos);
	[Signal] public delegate void DragStartedEventHandler(InputEvent @event, Vector2 currentPos,  Vector2 pressPos);
	[Signal] public delegate void DragMovedEventHandler(InputEvent @event, Vector2 currentPos, Vector2 pressPos);
	[Signal] public delegate void DragEndedEventHandler(InputEvent @event, Vector2 currentPos, Vector2 pressPos);

	public bool Pressed;
	public bool Dragging;
	private MouseButton _activeBtn;
	public Vector2 PressPos;

	public override void _Input(InputEvent @event)
	{
		// if mousebutton and press start tracking
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } mouseClick)
		{
			if (!Pressed && mouseClick.Pressed) // guards against press from another btn
			{
				Pressed = true;
				Dragging = false;
				_activeBtn = mouseClick.ButtonIndex;
				PressPos = mouseClick.Position;
			}
			else if (!mouseClick.Pressed && mouseClick.ButtonIndex == _activeBtn) // Release
			{
				Pressed = false;
				if (Dragging)
				{
					Dragging = false;
					EmitSignal(SignalName.DragEnded, @event, mouseClick.Position,  PressPos);
				}
				else
				{
					EmitSignal(SignalName.Clicked, @event, PressPos);
				}
					
			}
		}
		else if (@event is InputEventMouseMotion mouseMotion && Pressed)
		{
			if (!Dragging &&
				mouseMotion.Position.DistanceTo(PressPos) >= DragThreshold)
			{
				Dragging = true;
				EmitSignal(SignalName.DragStarted, @event, mouseMotion.Position, PressPos);
			}
			else if (Pressed && Dragging)
			{
				EmitSignal(SignalName.DragMoved, @event, mouseMotion.Position, PressPos);
			}
		}
	}
}
