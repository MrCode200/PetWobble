using System.Collections.Generic;
using System.Linq;
using Godot;
using Color = Godot.Color;

namespace PetWobble.Const;

// if perf to low change to  record struct
public sealed class Point
{
	public Vector2 Position { get; private set; }
	public Vector2 PreviousPosition;
	public Vector2 Acceleration;
	public readonly List<Vector2> Corrections = [];
	
	public Vector2 Velocity(float delta) => (Position - PreviousPosition) / delta;
	public float DistanceTo(Point point) => (point.Position - Position).Length();
	public void SetPosition(Vector2 position) => Position = position;

	public void UpdatePositionsUsingCorrections()
	{
		var sumOfCorrections = Corrections.Aggregate(Vector2.Zero, (a, b) => a + b);
		Position += sumOfCorrections / Corrections.Count;
		Corrections.Clear();
	}
	public override string ToString()
	{
		return $"Point(Position: {Position}, Acceleration: {Acceleration})";
	}
}

public readonly record struct SpringJoint
{
	public Point PointA { get; init; }
	public Point PointB { get; init; }
	public float RestLength { get; init; }
}

public partial class SoftBody : Node2D
{
	[ExportGroup("Points&Blob")] 
	[Export] public int PointCount = 32;
	[Export] public float PointRadius = 0.5f;
	[Export] public float Radius = 90.0f;
	// If 0, will be calculated from points
	[Export] public float DesiredArea = 0f;
	[Export] public float AreaPressureCoefficient = 0.1f;

	[ExportGroup("Physics")]
	[Export] public Vector2 Gravity = Vector2.Down;
	// Uses velocity *= exp(-Damping * deltaTime) 
	[Export] public float Damping = 0.5f;
	[Export] public float TotalCorrectionIterations = 1;
	[Export] public int MaxCollisionResolveRepetition = 5;
	[Export] public int JointConstraintIterations = 5;

	[ExportGroup("Collisions")]
	[Export] public bool ScreenIsWall = true;

	[ExportGroup("Joints")] 
	[Export] public float JointRestLength = 10;
	[Export] public bool UseSpawnDistanceAsRestLength;
	[Export] public float JointStiffness = 0.5f;

	// Each Point being connected to the next one (first connected to last)
	[Export] public bool CircularJoints;

	private Point[] _points;
	private SpringJoint[] _joints; 
	private double _previousDelta = 1.0 / 60.0;
	private float _desiredArea;

	public override void _Ready()
	{
		InitPoints();
		InitJoints();
		_desiredArea = DesiredArea == 0 ? CalculateCurrentArea() : DesiredArea;
	}
	
	private void InitPoints()
	{
		_points = new Point[PointCount];

		for (var i = 0; i < PointCount; i++)
		{
			var point =  new Point();
			
			float angle = Mathf.Tau / PointCount * i;
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

			point.SetPosition(direction * Radius);
			point.PreviousPosition = point.Position;
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
				RestLength = restLength
			};

			prevPoint = p;
			_joints[i] = joint;
			GD.Print(joint);
		}
	}

	public override void _Process(double delta)
	{
		var dt = (float)delta;
		
		CalculateAccelerations();
		ApplyForces(dt);
		
		for (int i = 0; i < TotalCorrectionIterations; i++)
		{
			AddJointConstraints();
			AddAreaConstraints();
			ResolveCollisionsAdvanced(dt);
			
			foreach(var p in _points)
				p.UpdatePositionsUsingCorrections();
		}
		// TODO: Add hard clamp for collision at the end?

		_previousDelta = delta;
		QueueRedraw();
	}

	private void ApplyForces(float delta)
	{
		foreach (var p in  _points)
		{
			// 1. Damping
			Vector2 velocity = (p.Position - p.PreviousPosition) / (float)_previousDelta;
			
			velocity *= Mathf.Exp(-Damping * delta);
			Vector2 newPos = p.Position + velocity * delta + p.Acceleration * delta * delta; 
			p.PreviousPosition = p.Position;
			p.SetPosition(newPos);
		}
	}
	
	private void CalculateAccelerations()
	{
		foreach (var p in  _points)
		{
			p.Acceleration = Gravity;
		}
	}
	
	private void AddJointConstraints()
	{
		for (int iter = 0; iter < JointConstraintIterations; iter++)
		{
			foreach (var j in _joints)
			{
				// Relative From Point A
				var distanceVector = j.PointB.Position - j.PointA.Position;
				var distance = distanceVector.Length();

				if (distance < 0.000001f)
					continue;

				Vector2 direction = distanceVector.Normalized();
				float stretch = (distance - j.RestLength) * 0.5f;
				Vector2 correction = direction * stretch * JointStiffness;
				
				j.PointA.Corrections.Add(correction);
				j.PointB.Corrections.Add(-correction);
			}
		}
	}

	private void AddAreaConstraints()
	{
		var area = CalculateCurrentArea();
		
		if (_desiredArea < 0.000001f || !float.IsFinite(area))
			return;
		
		var normalizedAreaError = (_desiredArea - area) / _desiredArea;
		var areaDisplacement = normalizedAreaError * AreaPressureCoefficient;

		for (int i = 0; i < _points.Length; i++)
		{
			var prevPoint = _points[(i - 1 + _points.Length) % _points.Length];
			var currentPoint = _points[i];
			var nextPoint = _points[(i + 1) % _points.Length];
			
			var neighbourDistanceVector = prevPoint.Position - nextPoint.Position;
			var outwardNormalVector = new Vector2(
				-neighbourDistanceVector.Y, 
				neighbourDistanceVector.X
			);
			
			if (outwardNormalVector.LengthSquared() < 0.000001f)
				continue;
			
			var stretch = outwardNormalVector * areaDisplacement;
			currentPoint.Corrections.Add(stretch);
		}
	}
	
	private float CalculateCurrentArea()
	{
		float signedArea = 0.0f;
		var prevPoint = _points[^1];

		foreach (var p in _points)
		{
			float width = p.Position.X - prevPoint.Position.X;
			float height = (p.Position.Y + prevPoint.Position.Y) * 0.5f;

			signedArea += width * height;

			prevPoint = p;
		}

		return Mathf.Abs(signedArea);
	}

	private void ResolveCollisionsSimple()
	{
		foreach (var p in _points)
		{
			// Simple collision handling for Verlet
			if (TryGetCollisionInfo(
					p.PreviousPosition,
					p.Position,
					out Vector2 collisionLocalPos,
					out Vector2 collisionLocalNormal))
			{
				// Push position out of collision
				var newPos = collisionLocalPos + collisionLocalNormal * 0.001f;
				p.Corrections.Add(newPos - p.Position);
				
				// Update previous position to reflect the bounce
				// This ensures velocity (Position - PreviousPosition) reflects the new direction
				Vector2 velocity = p.Position - p.PreviousPosition;
				Vector2 reflectedVelocity = ReflectVelocity(velocity, collisionLocalNormal);
				p.PreviousPosition = p.Position - reflectedVelocity;
			}
		}
	} 
	
	private void ResolveCollisionsAdvanced(float delta)
	{
		foreach (var p in  _points)
		{
			float availableDelta = delta;
			int repetitions = 0;
			Vector2 startPos = p.PreviousPosition;
			Vector2 finalPos = p.Position;
			Vector2 velocity = p.Velocity(delta);

			while (availableDelta > 0.000001f &&
			       repetitions < MaxCollisionResolveRepetition)
			{
				repetitions++;
				
				if (velocity.LengthSquared() < 0.000001f)
					break;
				
				if (!TryGetCollisionInfo(
					    startPos,
					    finalPos,
					    out Vector2 collisionLocalPos,
					    out Vector2 collisionLocalNormal))
					break;

				float distanceToCollision =
					startPos.DistanceTo(collisionLocalPos);

				float timeToCollision = distanceToCollision / velocity.Length();

				availableDelta -= timeToCollision;

				startPos =
					collisionLocalPos + collisionLocalNormal * 0.001f;

				if (velocity.Dot(collisionLocalNormal) < 0.0f)
				{
					velocity = ReflectVelocity(
						velocity,
						collisionLocalNormal);
				}
				finalPos = startPos + velocity * availableDelta;
			}
			
			p.Corrections.Add(finalPos - p.Position);
			p.PreviousPosition = p.Position - velocity * delta;
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

		// Check if vectors are finite before raycast
		if (!float.IsFinite(fromGlobal.X) || !float.IsFinite(fromGlobal.Y) ||
		    !float.IsFinite(toGlobal.X) || !float.IsFinite(toGlobal.Y))
			return false;

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
		foreach (var p in _points)
		{
			DrawCircle(
				p.Position,
				4.0f,
				new Color(0.5f, 0.5f, 0.7f));

			Vector2 velocity = p.Velocity((float)_previousDelta);
			if (velocity == Vector2.Zero)
				continue;
			
			DrawLine(
				p.Position,
				p.Position + velocity,
				new Color(0.5f, 0.5f, 0.5f));
		
		}
		
		foreach (var j in _joints)
		{
			{
				DrawLine(
					j.PointA.Position,
					j.PointB.Position, new Color(0.2f, 0.4f, 0.6f)
				);
			}
		}
	}
}
