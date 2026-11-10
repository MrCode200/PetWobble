using System;
using System.Collections.Generic;
using Godot;

namespace PetWobble.SoftBody;

public enum ForceType
{
	PredictedPosition,
	//Velocity,: doesn't work add support
	//Acceleration : doesn't work add support
}

public class SoftBodyApi(SoftBody softBody)
{
	// Getters
	public Point[] GetPointsInsideShape(Vector2 shapeCenterPosition, Shape2D shape)
	{
		List<Point> overlaps = new();
		foreach (var p in softBody.Points)
		{
			if (ContainsPoint(p, shapeCenterPosition, shape))
				overlaps.Add(p);
		}

		return overlaps.ToArray();
	}

	public Point[] GetPointsAtRadians(
		float angleRadian,
		int kNeighbours = 0,
		bool globalDirections = false,
		bool returnClosest = true
	)
	{
		var pointPerAngleStep = MathF.Tau / softBody.PointCount;

		angleRadian = ((angleRadian % MathF.Tau) + MathF.Tau) % MathF.Tau;
		
		if (!returnClosest && angleRadian % pointPerAngleStep > 0)
			return [];
		
		// Calc to global direction the radian, to get the correct points
		if (globalDirections)
			angleRadian += GetGlobalLocalAngleOffset();
		
		
		HashSet<Point> points = [];
		int i = (int)MathF.Round(angleRadian / pointPerAngleStep) % softBody.PointCount;
		
		points.Add(softBody.Points[i]);
		
		for (int k = 1; k <= kNeighbours; k++)
		{
			points.Add(softBody.Points[(i + k) % softBody.PointCount]);
			points.Add(softBody.Points[(i - k + softBody.PointCount) % softBody.PointCount]);
		}
		
		return [.. points];
	}

	public float GetGlobalLocalAngleOffset()
	{
		var localRight = softBody.CenterPoint
			.DirectionTo(softBody.Points[0].Position)
			.Normalized();
		
		return localRight.AngleTo(Vector2.Right);
	}
	
	// Checkers
	public bool ContainsPoint(Point p, Vector2 shapeCenterPosition, Shape2D shape)
	{
		var globalPointPos = softBody.ToGlobal(p.Position);
		
		switch (shape)
		{
			case CircleShape2D c:
				return globalPointPos.DistanceTo(shapeCenterPosition) <= c.Radius;
		}

		throw new ArgumentException("Unsupported shape type");
	}
	
	// Setters
	public void ApplyForce(Point p, Vector2 forceVector, ForceType forceType = ForceType.PredictedPosition)
	{
		ApplyForce([p], forceVector, forceType);
	}
	
	public void ApplyForce(Point[] points, Vector2 forceVector, ForceType forceType = ForceType.PredictedPosition)
	{
		void Solver(SoftBody sb, float h)
		{
			foreach (var p in points)
			{
				if (p.Frozen)
					continue;
				
				switch (forceType)
				{
					case ForceType.PredictedPosition:
						p.Predicted += forceVector * h;
						break;
					// case ForceType.Velocity:
					//     p.Velocity += forceVector;
					//     break;
					// case ForceType.Acceleration:
					//     p.Velocity += forceVector * h;
					//     break;
				}
			}
		}
		
		softBody.AddOneStepSolver(Solver);
	}
}
