using System.Linq;
using Godot;
using Godot.Collections;

namespace PetWobble.SoftBody.Extensions;

public partial class NodeTapeToPoint : Node2D
{
    [Export] public Node2D Target;
    [Export] public SoftBody SoftBody;
    [Export] public bool IncludeCenterPoint = true;
    [Export] public Array<float> PointDirections;
    [Export] public bool RotateWithSoftBody = true;

    [ExportGroup("Debug")]
    [Export] public bool DrawDebug;

    private const float DebugCircleRadius = 2f;
    private static readonly Color DebugAnchorColor = new(1, 0, 0, 1);
    private static readonly Color DebugAverageColor = new(0, 1, 0, 1);
    private static readonly Color DebugLineColor = new(1, 1, 0, 1);

    private Point _referenceRotationPoint;
    private Vector2 _referenceRotationVec;

    private Point[] _anchorPoints;

    public override void _Ready()
    {
        SoftBody.SoftBodyInitialized += OnSoftBodyInitialized;
    }

    private void OnSoftBodyInitialized()
    {
        _anchorPoints = PointDirections
            .SelectMany<float, Point>(radian => SoftBody.SBApi.GetPointsAtRadians(radian))
            .ToArray();

        _referenceRotationPoint = SoftBody.SBApi.GetPointsAtRadians(0)[0];
        
        SoftBody.StepFinished += OnStepFinished;
    }

    public override void _ExitTree()
    {
        SoftBody.StepFinished -= OnStepFinished;
        SoftBody.SoftBodyInitialized -= OnSoftBodyInitialized;
    }

    private void OnStepFinished()
    {
        Target.Position = CalculateAveragePosition();
        if (RotateWithSoftBody)
            Target.Rotation = CalculateNewRotation();
        
        QueueRedraw();
    }

    private Vector2 CalculateAveragePosition()
    {
        int pointCount = IncludeCenterPoint ? _anchorPoints.Length + 1 : _anchorPoints.Length;
        Vector2 averagePosition = Vector2.Zero;

        foreach (var point in _anchorPoints)
        {
            averagePosition += point.Position;
        }

        if (IncludeCenterPoint)
        {
            averagePosition += SoftBody.CenterPoint;
        }

        return SoftBody.ToGlobal(averagePosition / pointCount);
    }
    
    private float CalculateNewRotation()
    {
        return SoftBody.CenterPoint.AngleToPoint(_referenceRotationPoint.Position);
    }

    public override void _Draw()
    {
        if (!DrawDebug) return;
        
        Vector2 averagePosition = CalculateAveragePosition();

        DrawCircle(averagePosition, DebugCircleRadius, DebugAverageColor);

        foreach (var point in _anchorPoints)
        {
            Vector2 globalPointPosition = SoftBody.ToGlobal(point.Position);
            DrawCircle(globalPointPosition, DebugCircleRadius, DebugAnchorColor);
            DrawLine(globalPointPosition, averagePosition, DebugLineColor);
        }

        if (IncludeCenterPoint)
        {
            Vector2 globalCenterPosition = SoftBody.ToGlobal(SoftBody.CenterPoint);
            DrawCircle(globalCenterPosition, DebugCircleRadius, DebugAnchorColor);
            DrawLine(globalCenterPosition, averagePosition, DebugLineColor);
        }
    }
}