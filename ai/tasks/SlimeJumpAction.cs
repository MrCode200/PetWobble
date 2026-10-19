using System;
using Godot;

namespace PetWobble.AI.Tasks;

[Tool]
public partial class SlimeJumpAction : BTAction
{
	[Export] public NodePath AnimatedSpritePath;
	[Export] public float Speed = 300;
	[Export] public float MaxDirectionChangePerTick = 0.1f;
	[Export] public float FlipChance = 0.25f;

	private AnimatedSprite2D _animator;
	private Vector2 _direction = Vector2.Left;
	private bool _animationFinished;

	private Vector2 _windowSize;
	private Vector2 _screenSize;

	public override string _GenerateName()
	{
		return "Jumping towards " + _direction;
}

	public override void _Setup()
	{
		_animator = SceneRoot.GetNode<AnimatedSprite2D>(AnimatedSpritePath);
		_windowSize = DisplayServer.WindowGetSize();
		_screenSize = DisplayServer.ScreenGetSize();
	}

	private void SetAnimationFinished()
	{
		_animationFinished = true;
		CalcNewDirection();
	}

	public override void _Enter()
	{
		_animator.Play(Const.Animations.Jump);
		TryDirectionChange();
		_animationFinished =  false;
		_animator.AnimationFinished += SetAnimationFinished;
	}

	public override void _Exit()
	{
		_animator.AnimationFinished -= SetAnimationFinished;
	}

	public override Status _Tick(double delta)
	{
		if (_animationFinished)
			return Status.Success;
		
		if (_animator.Frame is < 2 or > 8)
			return Status.Running;

		var newWinPos = DisplayServer.WindowGetPosition() + 
						(_direction * Speed * (float)delta);
		newWinPos = newWinPos.Clamp(Vector2.Zero, _screenSize - _windowSize);
		DisplayServer.WindowSetPosition((Vector2I)newWinPos);
		// Flip if moving backwards
		_animator.FlipH = _direction.X < 0 ? true : false;

		return Status.Running;
	}

	private void TryDirectionChange()
	{
		_direction *= new Vector2(
			GD.Randf() <= FlipChance / 2 ? -1 : 1, 
			GD.Randf() <= FlipChance / 2 ? -1 : 1
			);
	}

	private void CalcNewDirection()
	{
		float angle = (float)GD.RandRange(
			-MaxDirectionChangePerTick * GD.Randf(), 
			MaxDirectionChangePerTick * GD.Randf()
			);
		
		_direction = _direction.Rotated(angle).Normalized();
	}
}
