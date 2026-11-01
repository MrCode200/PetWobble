using System.Linq;
using Godot;
using PetWobble.Const.Common;

namespace PetWobble.Const;

public partial class MouseSoftBodyDragger : Node2D
{
    [Export] public SoftBody SB;
    [Export] public MouseInteractionHandler MIH;
    [Export] public Shape2D Shape;
    [Export] public float LerpSpeed = 10f;
    [Export] public float MaxSpeed = 10f;
    [Export] public int ClosestPointCount = 1;
    [Export] public float UnfreezeDelay = 0.1f;
    
    
    
    [ExportGroup("Debug")] 
    [Export] public bool DrawShape = true;
    
    private Vector2 _shapeCenter;
    
    // ReSharper disable once InconsistentNaming
    private bool _isDraggingSB;
    private Point[] _overlappingPoints = [];
    private Vector2[] _grabOffsets = [];
    private float _unfreezeCountdown;
    
    public override void _Ready()
    {
        MIH.DragStarted += OnDragStarted;
        MIH.DragEnded += OnDragEnded;
    }

    public override void _ExitTree()
    {
        MIH.DragStarted -= OnDragStarted;
        MIH.DragEnded -= OnDragEnded;
        SB.AddCustomSolver(Solve);
    }

    private void OnDragStarted(InputEvent @event, Vector2 currentPos, Vector2 pressPos)
    {
        _unfreezeCountdown = -1f; // Cancels Countdown
        UnfreezePoints();
        _shapeCenter = pressPos; // Starts Drag Lerping at drag position.
        
        var overlap = SB.SBApi.GetPoints(pressPos, Shape).Concat(SB.SBApi.GetPoints(currentPos, Shape)).ToArray();
        GD.Print(overlap.Length);
        if (overlap.Length == 0)
            return;

        _overlappingPoints = ClosestPointCount == -1 
            ? overlap 
            : overlap.OrderBy(p => p.Position.DistanceTo(pressPos)).Take(ClosestPointCount).ToArray();
        
        _grabOffsets = new Vector2[_overlappingPoints.Length];
        for (int i = 0; i < _overlappingPoints.Length; i++)
        {
            _grabOffsets[i] = SB.ToGlobal(_overlappingPoints[i].Position) - pressPos;
            _overlappingPoints[i].Frozen = true;
        }
        _isDraggingSB = true;
    }
    
    private void OnDragEnded(InputEvent @event, Vector2 currentPos, Vector2 pressPos)
    {
        _isDraggingSB = false;
        _unfreezeCountdown = UnfreezeDelay; // Requests To Unfreeze
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_isDraggingSB)
        {
            var dt = (float)delta;
            var mousePos = GetGlobalMousePosition();

            // Smooth follow
            float t = 1f - Mathf.Exp(-LerpSpeed * dt);
            var targetPos = _shapeCenter.Lerp(mousePos, t);

            // Limit movement speed
            var movement = targetPos - _shapeCenter;	
            if (MaxSpeed > 0)
            {
                float maxStep = MaxSpeed * dt;

                if (movement.Length() > maxStep)
                    movement = movement.Normalized() * maxStep;
            }
            _shapeCenter += movement;
        }
        
        if (_unfreezeCountdown >= 0f)
        {
            _unfreezeCountdown -= (float)delta;
            if (_unfreezeCountdown <= 0)
                UnfreezePoints();

        }        
        QueueRedraw();

        if (!_isDraggingSB)
            return;
        
        Solve(SB, (float)delta);
    }

    private void UnfreezePoints()
    {
        foreach (var point in _overlappingPoints)
        {
            point.Frozen = false;
        }
    }
    
    public void Solve(SoftBody sb, float h)
    {
        if (!_isDraggingSB)
            return;
        
        // Freeze Points, ergo no external force changes it, move once points to position of mouse
        // add lerp if physics go haywire
        for (int i = 0; i < _overlappingPoints.Length; i++)
        {
            var p = _overlappingPoints[i];
            Vector2 target = sb.ToLocal(_shapeCenter + _grabOffsets[i]);
            p.Position = target;
            p.Predicted = target;   // keeps joint/area solvers in sync
        }
    }

    public override void _Draw()
    {
        var mousePos = GetGlobalMousePosition();
        
        if (DrawShape)
        {
            switch (Shape)
            {
                case CircleShape2D circle:
                    DrawCircle(_shapeCenter, circle.Radius, new Color(0.2f, 0.2f, 0.8f, 0.5f));
                    break;
            }
			
            DrawLine(mousePos, _shapeCenter, new Color(0.5f, 0.5f, 0.5f));
        }
    }
}