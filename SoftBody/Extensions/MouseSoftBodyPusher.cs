using System;
using Godot;

namespace PetWobble.SoftBody.Extensions;
 
// TODO: make it soft follow/ Drag variable add to it
public partial class MouseSoftBodyPusher : Node2D
{
	[Export] public SoftBody SoftBody;
	[Export] public Shape2D Shape;
	
	[ExportGroup("Force")] 
	[Export] public float Force = 1f;
	[Export] public float ReferenceSpeed = 8f; // pusher speed at which force is max, if 0 unlimited
	[Export] public Curve SpeedToForceRatioCurve;
	
	[ExportGroup("Follow")]
	[Export] public float LerpSpeed = 5f;
	[Export] public float MaxSpeed = 10f;
	
	[Export] public Curve DistanceToForceRatioCurve;
	
	[ExportGroup( "Debug")]
	[Export] public bool DrawShape;
	
	private Vector2 _shapeCenter;
	
	public override void _Ready()
	{
		DistanceToForceRatioCurve.Bake();
		SpeedToForceRatioCurve.Bake();
		_shapeCenter = GetGlobalMousePosition();
	}
	
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		var mousePos = GetGlobalMousePosition();

		// Smooth follow
		var targetPos = mousePos;
		if (LerpSpeed > 0)
		{
			float t = 1f - Mathf.Exp(-LerpSpeed * dt);
			targetPos = _shapeCenter.Lerp(mousePos, t);
		}

		// Limit movement speed
		var movement = targetPos - _shapeCenter;	
		if (MaxSpeed > 0)
		{
			float maxStep = MaxSpeed * dt;

			if (movement.Length() > maxStep)
				movement = movement.Normalized() * maxStep;
		}
		
		// Velocity of the pusher per second
		float velocityForce = 1;
		if (ReferenceSpeed > 0)
		{
			var pusherV = movement.Length() / dt;
			var speedRatio = ReferenceSpeed <= 0 ?
				1f :
				Mathf.Clamp(pusherV / ReferenceSpeed, 0f, 1f);
			velocityForce = SpeedToForceRatioCurve.SampleBaked(speedRatio) * Force;
			//GD.Print($"Speed: {pusherV}, Ratio: {speedRatio}, Force: {velocityForce}");
		}
		
		_shapeCenter += movement;
		
		// Find Points and apply forces
		var overlaps = SoftBody.SBApi.GetPointsInsideShape(_shapeCenter, Shape);
		foreach (var p in overlaps)
		{
			Vector2 globalPos = SoftBody.ToGlobal(p.Position);
			Vector2 globalDir = globalPos - _shapeCenter;
			if (globalDir.LengthSquared() < 0.000001f) continue;
			
			var ratio = Mathf.Clamp(ShapeCenterToPointDistanceRatio(p, _shapeCenter, Shape), 0f, 1f);
			var force = velocityForce * DistanceToForceRatioCurve.SampleBaked(ratio);
			
			
			SoftBody.SBApi.ApplyForce(p, globalDir.Normalized() * force);
		}
		
		if (DrawShape)
			QueueRedraw();
	}

	public float ShapeCenterToPointDistanceRatio(Point p, Vector2 shapeCenter, Shape2D shape)
	{
		switch (shape)
		{
			case CircleShape2D circle:
				return SoftBody.ToGlobal(p.Position).DistanceTo(shapeCenter) / circle.Radius;
		}
		
		throw new ArgumentException("Unsupported shape type");
	}
	
	public override void _Draw()
	{
		var mousePos = GetGlobalMousePosition();
		
		if (DrawShape)
		{
			switch (Shape)
			{
				case CircleShape2D circle:
					DrawCircle(_shapeCenter, circle.Radius, new Color(0.8f, 0.2f, 0.2f, 0.5f));
					break;
			}
			
			DrawLine(_shapeCenter, mousePos, new Color(0.5f, 0.5f, 0.5f));
		}
	}
}
