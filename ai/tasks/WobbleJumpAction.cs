#nullable enable
using System;
using Godot;

namespace PetWobble.Const.ai.tasks;

public partial class WobbleJumpAction : BTAction
{
    [ExportGroup("Force")]
    [Export] public float Force = 100;
    [Export] public int NNeighboursApplyForce;
    [Export] public Curve? YForceMultiplier;
    [Export] public Curve? Decay;
    
    [ExportGroup("Duration")]
    [Export] public float MinDuration;
    [Export] public float MaxDuration;
    
    [ExportGroup("Direction")]
    [Export] public float MaxRadianChangePerCall;
    [Export] public float FlipChance = 0.25f;
    [Export] public Vector2 RadianRange = new(0, MathF.PI);
    [Export] public bool UseDirectionChange = true;
    [Export] public float StartingFromRadian = 4.7123889f;
    [Export] public required Curve DirectionChange;
    [Export] public bool UseGlobalDirections;
    
    [ExportGroup("References")]
    [Export] public required NodePath SoftBodyPath;
    private SoftBody.SoftBody _sb = null!;

    private float _targetRadianToApply;
    private float _applyRadian;
    private float _jumpTimeTotal;
    private float _jumpTimeRemaining;
    
    public override void _Setup()
    {
        _sb = SceneRoot.GetNode<SoftBody.SoftBody>(SoftBodyPath);
        if (_sb is null)
            throw new InvalidCastException($"{nameof(SoftBodyPath)} is not a SoftBody Node");

        Decay?.Bake();
        DirectionChange?.Bake();
        YForceMultiplier?.Bake();
        CalcNewDirection();
    }

    public override Status _Tick(double delta)
    {
        // CHECK: what is better using MaxDur as denom or startingTime as denom?
        if (_jumpTimeRemaining <= 0)
        {
            CalcNewDirection();

            if (UseDirectionChange)
                _applyRadian = StartingFromRadian;
            else
                _applyRadian = _targetRadianToApply;
            
            _jumpTimeTotal = MinDuration + GD.Randf() * MaxDuration;
            _jumpTimeRemaining = _jumpTimeTotal;
        }    
        
        var progress = 1 - _jumpTimeRemaining / _jumpTimeTotal;
        MakeJump(progress);
        UpdateDirection(progress);

        _jumpTimeRemaining -= (float)delta;

        return _jumpTimeRemaining > 0 ? Status.Running : Status.Failure;
    }

    private void MakeJump(float progress)
    {
        var force = new Vector2(Mathf.Cos(_applyRadian), Mathf.Sin(_applyRadian))
                    * Force;
        
        if (Decay is not null)
            force *= Decay.SampleBaked(progress);

        if (YForceMultiplier is not null)
            force.Y *= YForceMultiplier.SampleBaked(progress);
        
        // CHECK: use here _applyRadian or target radian for get points
        var points = _sb.SBApi.GetPointsAtRadians(_applyRadian, NNeighboursApplyForce, UseGlobalDirections);
        // foreach (var p in points)
        //     p.Frozen = true;
        
        _sb.SBApi.ApplyForce(points, force);
    }
    
    private void CalcNewDirection()
    {
        float angle = (float)GD.RandRange(
            -MaxRadianChangePerCall * GD.Randf(), 
            MaxRadianChangePerCall * GD.Randf()
        );

        _targetRadianToApply = Mathf.Clamp(_targetRadianToApply + angle, RadianRange.X, RadianRange.Y);

        if (FlipChance <= GD.Randf())
            _targetRadianToApply = RadianRange.X + RadianRange.Y - _targetRadianToApply;
    }

    private void UpdateDirection(float progress)
    {
        if (!UseDirectionChange) return;
        
        var change = _targetRadianToApply - StartingFromRadian;
        _applyRadian = StartingFromRadian + DirectionChange.SampleBaked(progress) * change;
    }
}