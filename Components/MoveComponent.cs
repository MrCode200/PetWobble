using Godot;

namespace PetWobble.Components;

public partial class MoveComponent : Node
{
	[Export] public float Speed = 300;
	public Vector2 Direction => new(Animator.FlipH ? -1 : 1, 0);
	[Export] public AnimatedSprite2D Animator;

	public Vector2 WindowSize;
	public Vector2 ScreenSize;
	
	public bool IsRunning = true;

	public override void _Ready()
	{
		ScreenSize = DisplayServer.ScreenGetSize();
		WindowSize = GetWindow().Size;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!IsRunning || Animator.Frame is < 2 or > 8)
			return;
		
		var winPos = (Vector2)DisplayServer.WindowGetPosition();
		winPos += Direction * Speed * (float)delta;
		var newPos = winPos.Clamp(Vector2.Zero, ScreenSize - WindowSize);
		if (newPos != winPos)
		{
			Animator.FlipH = !Animator.FlipH;
		}
		DisplayServer.WindowSetPosition((Vector2I)newPos);
		Animator.Play(Const.Animations.Jump);
	}
}
