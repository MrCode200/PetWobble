using System.Drawing;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Text.Json.Serialization;
using Godot;
using Color = Godot.Color;

namespace PetWobble.Const;

// if perf to low change to  record struct
public sealed class Point
{
	public Vector2 Position;
	public Vector2 Velocity;
	public Vector2 Acceleration;
	
	public float DistanceTo(Point point) => (point.Position - Position).Length();
}

public readonly record struct SpringJoint
{
	public Point PointA { get; init; }
	public Point PointB { get; init; }
	public float RestLength { get; init; }
	public float Stiffness { get; init; }
}

public partial class SoftBody : Node2D
{
	[Export] public int ParticleCount = 32;
	[Export] public float ParticleRadius = 0.5f;
	[Export] public float Radius = 90.0f;

	[ExportGroup("Physics")]
	[Export] public Vector2 Gravity = Vector2.Down;
	// Uses velocity *= exp(-Damping * deltaTime) 
	[Export] public float Damping = 0.5f;
	[Export] public int MaxCollisionResolveRepetition = 5;

	[ExportGroup("Collisions")]
	[Export] public bool ScreenIsWall = true;

	[ExportGroup("Joints")] 
	[Export] public float JointRestLength = 10;
	[Export] public bool UseSpawnDistanceAsRestLength;

	[Export] public float JointStiffness = 100;

	// Each particle being connected to the next one (first connected to last)
	[Export] public bool CircularJoints;

	private Point[] _points;
	private SpringJoint[] _joints;

	public override void _Ready()
	{
		InitPoints();
		InitJoints();
	}

	private void InitPoints()
	{
		_points = new Point[ParticleCount];

		for (var i = 0; i < ParticleCount; i++)
		{
			var point =  new Point();
			
			float angle = Mathf.Tau / ParticleCount * i;
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

			point.Position = direction * Radius;
			point.Velocity = Vector2.Zero;
			point.Acceleration = Vector2.Zero;
			
			_points[i] = point;
			GD.Print(point);
		}
	}

	private void InitJoints()
	{
		if (!CircularJoints)
		{
			_joints = [];
			return;
		}

		_joints = new SpringJoint[_points.Length];
		var prevPoint = _points[^1];
		for (int i = 0; i < _points.Length; i++)
		{
			var p = _points[i];

			var restLength = UseSpawnDistanceAsRestLength ? p.DistanceTo(prevPoint) : JointRestLength;
			var joint = new SpringJoint
			{
				PointA = prevPoint,
				PointB = p,
				RestLength = restLength,
				Stiffness = JointStiffness
			};

			prevPoint = p;
			_joints[i] = joint;
			GD.Print(joint);
		}
	}

	public override void _Process(double delta)
	{
		CalculateAccelerations(delta);
		UpdateVelocities(delta);
		MovePoints(delta);

		QueueRedraw();
	}

	private void UpdateVelocities(double delta)
	{
		foreach (var p in  _points)
		{
			// 1. Damping
			p.Velocity *= (float)Mathf.Exp(-Damping * delta);
			// 2. Acceleration
			p.Velocity += p.Acceleration * (float)delta;
		}
	}

	private void ApplyJointAcceleration(double delta)
	{
		foreach (var j in _joints)
		{
			// Relative From Point A
			var distanceVector = j.PointB.Position - j.PointA.Position;
			var distance = distanceVector.Length();

			if (distance < 0.00001f)
				continue;

			Vector2 direction = distanceVector.Normalized();
			float stretch = distance - j.RestLength;
			var force = stretch * j.Stiffness * direction;
			
			j.PointA.Acceleration += force;
			j.PointB.Acceleration -= force;
		}
	}

	private SpringJoint[] GetJointsOfPoint(Point point)
	{
		return _joints.Where(j => j.PointA == point || j.PointB == point).ToArray();
	}

	private void CalculateAccelerations(double delta)
	{
		foreach (var p in  _points)
		{
			p.Acceleration = Gravity;
		}
		ApplyJointAcceleration(delta);
	}

	private void MovePoints(double delta)
	{
		foreach (var p in  _points)
		{
			float availableDelta = (float)delta;
			int repetitions = 0;

			while (availableDelta > 0.000001f &&
				   repetitions < MaxCollisionResolveRepetition)
			{
				repetitions++;
				
				if (p.Velocity.LengthSquared() < 0.000001f)
					break;

				Vector2 newLocalPos = p.Position + p.Velocity * availableDelta;

				if (!TryGetCollisionInfo(
						p.Velocity,
						newLocalPos,
						out Vector2 collisionLocalPos,
						out Vector2 collisionLocalNormal))
				{
					p.Position = newLocalPos;
					break;
				}

				float distanceToCollision =
					p.Position.DistanceTo(collisionLocalPos);

				float timeToCollision = distanceToCollision / p.Velocity.Length();

				availableDelta -= timeToCollision;

				p.Position =
					collisionLocalPos + collisionLocalNormal * 0.001f;

				if (p.Velocity.Dot(collisionLocalNormal) < 0.0f)
				{
					p.Velocity = ReflectVelocity(
						p.Velocity,
						collisionLocalNormal);
				}
			}
		}
	}

	private bool TryGetCollisionInfo(
		Vector2 fromLocal,
		Vector2 toLocal,
		out Vector2 collisionLocalPos,
		out Vector2 collisionLocalNormal)
	{
		collisionLocalPos = Vector2.Zero;
		collisionLocalNormal = Vector2.Zero;

		Vector2 fromGlobal = ToGlobal(fromLocal);
		Vector2 toGlobal = ToGlobal(toLocal);

		var space = GetWorld2D().DirectSpaceState;

		var parameters = new PhysicsRayQueryParameters2D
		{
			From = fromGlobal,
			To = toGlobal,
			CollideWithAreas = false,
			CollideWithBodies = true
		};

		var hit = space.IntersectRay(parameters);

		if (hit.Count == 0)
			return false;

		Vector2 collisionGlobalPos = hit["position"].AsVector2();
		Vector2 collisionGlobalNormal = hit["normal"].AsVector2();

		collisionLocalPos = ToLocal(collisionGlobalPos);
		collisionLocalNormal = ToLocalDirection(collisionGlobalNormal);

		return true;
	}

	private Vector2 ToLocalDirection(Vector2 globalDirection)
	{
		return ToLocal(globalDirection) - ToLocal(Vector2.Zero);
	}

	private static Vector2 ReflectVelocity(
		Vector2 velocity,
		Vector2 normal)
	{
		float normalLengthSquared = normal.LengthSquared();

		if (normalLengthSquared < 0.000001f)
			return velocity;

		return velocity
			- 2.0f * velocity.Dot(normal)
			/ normalLengthSquared
			* normal;
	}

	public override void _Draw()
	{
		DrawCircle(Vector2.Zero, 5.0f, new Color(0.5f, 0.5f, 0.5f));
		
		foreach (var p in _points)
		{
			DrawCircle(
				p.Position,
				4.0f,
				new Color(0.5f, 0.5f, 0.7f));

			DrawLine(
				p.Position,
				p.Position + p.Velocity,
				new Color(0.5f, 0.5f, 0.5f));
		}
		
		foreach (var j in _joints)
		{
			DrawLine(
				j.PointA.Position, 
				j.PointB.Position, new Color(0.2f, 0.4f, 0.6f)
			);
		}
	}
}
