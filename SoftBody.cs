using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Godot;
using Godot.NativeInterop;
using Color = Godot.Color;

namespace PetWobble.Const;

public partial class SoftBody : Node2D
{
	[Export] public int ParticleCount = 32;
	[Export] public float ParticleRadius = 0.5f;
	[Export] public float Radius = 90.0f;

	[ExportGroup("Physics")] 
	[Export] public Vector2 Gravity = Vector2.Down;
	[ExportGroup("Static")]
	[Export] public bool WindowIsWall = true;

	private Vector2[] _relPositions;
	private Vector2[] _velocities;

	private Vector2 _windowSize;

	public override void _Ready()
	{
		_windowSize = DisplayServer.WindowGetSize();
		_relPositions = new Vector2[ParticleCount];
		_velocities = new Vector2[ParticleCount];

		for (var i = 0; i < ParticleCount; i++)
		{
			float angle = Mathf.Tau / ParticleCount * i;
			Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

			_relPositions[i] = direction * Radius;
			_velocities[i] = Vector2.Zero;
		}
	}

	public override void _Process(double delta)
	{
		MovePoints(delta);
		ResolveCollisions();
		QueueRedraw();
	}

	private void MovePoints(double delta)
	{
		for (int i = 0; i < ParticleCount; i++)
		{
			_velocities[i] += Gravity * (float)delta;
			
			
			
			_relPositions[i] += _velocities[i] * (float)delta;
		}
	}

	private void ResolveCollisions()
	{
		var space = GetWorld2D().DirectSpaceState;

		for (int i = 0; i < ParticleCount; i++)
		{
			// CHECK: send ray towards location? if collision hit get normal, move to collision point and update velocity?
			// CHECK: then when time, move not just to collision point but also if it should bounce, the path it bounces too...
			
			// 1. Check for any Collisions at the current space
			var param = new PhysicsShapeQueryParameters2D
			{
				Shape = new CircleShape2D
				{
					Radius = ParticleRadius,
				},
				Transform = new Transform2D(0, Position + _relPositions[i]),
				CollideWithAreas = false,
				CollideWithBodies = true
			};

			var hits = space.IntersectShape(param);
			if (hits.Count == 0)
				continue;
			
			// hit ray from prev position to position now, 
			// get the normal 
			// update velocity appropriate to the normal
			// Move Body Outside
		}	
	}

	private CollisionObject2D[] GetCollidersAt(Vector2 pos)
	{
		var space = GetWorld2D().DirectSpaceState;

		var param = new PhysicsPointQueryParameters2D
		{
			Position = pos,
			CollideWithAreas = false,
			CollideWithBodies = true
		};
		var results = space.IntersectPoint(param);
		
		List<CollisionObject2D> colliders = [];
		foreach (var dict in results)
		{
			colliders.Add((CollisionObject2D)dict["collider"]);
		}

		return colliders.ToArray();
	}

	public override void _Draw()
	{
		DrawCircle(Position, 5, new Color(0.5f, 0.5f, 0.5f));

		for (int i = 0; i < ParticleCount; i++)
		{
			DrawCircle(Position + _relPositions[i], 4, new Color(0.5f, 0.5f, 0.7f));
		}
	}
}
