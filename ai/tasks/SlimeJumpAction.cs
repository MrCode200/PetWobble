using Godot;

namespace PetWobble.AI.Tasks;

[Tool]
public partial class SlimeJumpAction : BTAction
{
	[Export] public NodePath AnimatedSpritePath;
	[Export] public float Speed = 300;

	private AnimatedSprite2D _animator;
	private Vector2 _direction;
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

	private void SetAnimationFinished() => _animationFinished = true;

	public override void _Enter()
	{
		_animator.Play(Const.Animations.Jump);
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

		var moveDir = GetNewDirection();
		var newWinPos = DisplayServer.WindowGetPosition() + 
						(moveDir * Speed * (float)delta);
		newWinPos = newWinPos.Clamp(Vector2.Zero, _screenSize - _windowSize);
		DisplayServer.WindowSetPosition((Vector2I)newWinPos);
		// Flip if moving backwards
		_animator.FlipH = moveDir.X < 0 ? true : false;

		return Status.Running;
	}

	private Vector2 GetNewDirection()
	{
		return Vector2.Left;
	}
}
