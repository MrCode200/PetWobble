using System;
using System.Collections.Generic;
using Godot;

namespace PetWobble.Const;

public enum ForceType
{
    PredictedPosition,
    //Velocity,: doesn't work add support
    //Acceleration : doesn't work add support
}

public class SoftBodyApi(SoftBody softBody)
{
    public Point[] GetPoints(Vector2 shapeCenterPosition, Shape2D shape)
    {
        List<Point> overlaps = new();
        foreach (var p in softBody.Points)
        {
            if (ContainsPoint(p, shapeCenterPosition, shape))
                overlaps.Add(p);
        }

        return overlaps.ToArray();
    }
    
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

    public void ApplyForce(Point p, Vector2 direction, float force, ForceType forceType = ForceType.PredictedPosition)
    {
        ApplyForce([p], direction * force, forceType);
    }
    
    public void ApplyForce(Point p, Vector2 forceVector, ForceType forceType = ForceType.PredictedPosition)
    {
        ApplyForce([p], forceVector, forceType);
    }
    
    public void ApplyForce(Point[] points, Vector2 direction, float force, ForceType forceType = ForceType.PredictedPosition)
    {
        ApplyForce(points, direction * force, forceType);
    }

    public void ApplyForce(Point[] points, Vector2 forceVector, ForceType forceType = ForceType.PredictedPosition)
    {
        void Solver(SoftBody sb, float h)
        {
            foreach (var p in points)
            {
                switch (forceType)
                {
                    case ForceType.PredictedPosition:
                        p.Predicted += forceVector;
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