using System.Runtime.InteropServices;
using Godot;

namespace PetWobble.Const.Common;

public partial class MouseInteractionHandler : Node
{
	[Export] public float DragThreshold = 10f;
	[Export] public bool UseInput = true;
	[Export] public bool UseUnhandledInput;

	[Signal] public delegate void ClickedEventHandler(InputEvent @event, Vector2 pressPos);
	[Signal] public delegate void DragStartedEventHandler(InputEvent @event, Vector2 currentPos,  Vector2 pressPos);
	[Signal] public delegate void DragMovedEventHandler(InputEvent @event, Vector2 currentPos, Vector2 pressPos);
	[Signal] public delegate void DragEndedEventHandler(InputEvent @event, Vector2 currentPos, Vector2 pressPos);

	public bool Pressed;
	public bool Dragging;
	private MouseButton _activeBtn;
	public Vector2 PressPos;

	// [StructLayout(LayoutKind.Sequential)]
	// public struct POINT
	// {
	// 	public int X;
	// 	public int Y;
	// }
	//
	// [DllImport("user32.dll")]
	// public static extern bool GetCursorPos(out POINT lpPoint);

	public override void _Input(InputEvent @event)
	{
		if (!UseInput)
			return;
		
		EvaluateInput(@event);
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (!UseUnhandledInput)
			return;
		
		EvaluateInput(@event);
	}

	public void EvaluateInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Right } click)
		{
			Vector2 world = ToWorld(click.Position);

			if (!Pressed && click.Pressed)
			{
				Pressed = true;
				Dragging = false;
				_activeBtn = click.ButtonIndex;
				PressPos = world;
			}
			else if (!click.Pressed && click.ButtonIndex == _activeBtn)
			{
				Pressed = false;
				if (Dragging)
				{
					Dragging = false;
					EmitSignal(SignalName.DragEnded, @event, world, PressPos);
				}
				else
				{
					EmitSignal(SignalName.Clicked, @event, PressPos);
				}
			}
		}
		else if (@event is InputEventMouseMotion motion && Pressed)
		{
			Vector2 world = ToWorld(motion.Position);

			if (!Dragging && world.DistanceTo(PressPos) >= DragThreshold)
			{
				Dragging = true;
				EmitSignal(SignalName.DragStarted, @event, world, PressPos);
			}
			else if (Dragging)
			{
				EmitSignal(SignalName.DragMoved, @event, world, PressPos);
			}
		}
	}
	
	private Vector2 ToWorld(Vector2 viewportPos)
		=> GetViewport().CanvasTransform.AffineInverse() * viewportPos;
}
