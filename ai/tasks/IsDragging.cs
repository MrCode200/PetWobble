using Godot;
using PetWobble.Const.Common;

namespace PetWobble.AI.Tasks;

[Tool]
public partial class IsDragging : BTCondition
{
	private MouseInteractionHandler _mih;
	private Node2D _collider;
	private bool _isDraggingCollider;

	[Export] public NodePath ColliderPath;

	public override void _Setup()
	{
		_collider = Agent.GetNode<Node2D>(ColliderPath);
		_mih = Agent.GetNode<MouseInteractionHandler>("%MouseInteractionHandler");

		_mih.DragStarted += OnDragStarted;
		_mih.DragEnded += OnDragEnded;
	}

	public override string _GenerateName()
	{
		return $"IsDragging: {_isDraggingCollider}";
	}

	public override Status _Tick(double delta)
	{
		return _isDraggingCollider ? Status.Success : Status.Failure;
	}

	private void OnDragStarted(InputEvent @event, Vector2 currentPos, Vector2 pressPos)
	{
		_isDraggingCollider = ContainsCollider(pressPos);
	}

	private void OnDragEnded(InputEvent @event, Vector2 currentPos, Vector2 pressPos)
	{
		_isDraggingCollider = false;
	}

	private bool ContainsCollider(Vector2 position)
	{
		var spaceState = Agent.GetViewport().GetWorld2D().DirectSpaceState;

		var parameters = new PhysicsPointQueryParameters2D
		{
			Position = position,
			CollideWithAreas = true,
			CollideWithBodies = true,
			CollisionMask = uint.MaxValue
		};

		foreach (var result in spaceState.IntersectPoint(parameters))
		{
			if (result["collider"].As<Node2D>() == _collider)
				return true;
		}

		return false;
	}
}
