using System;
using Godot;

namespace PetWobble.Const;
 
// TODO: make it soft follow/ Drag variable add to it
public partial class MouseSoftBodyPusher : Node2D
{
	[Export] public SoftBody SoftBody;
	[Export] public Shape2D Shape;
	[Export] public float Force = 1f;
	[Export] public float ShapeSpeed = 5f;
	[Export] public float MaxSpeed = 10f;
	
	[Export] public Curve DistanceToForceRatioCurve;
	
	[ExportGroup( "Debug")]
	[Export] public bool DrawShape;
	
	private SoftBodyApi _sbapi;
	private Vector2 _shapeCenter;
	
	public override void _Ready()
	{
		_sbapi = new(SoftBody);
		DistanceToForceRatioCurve.Bake();
		_shapeCenter = GetGlobalMousePosition();
	}
	
	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;
		var mousePos = GetGlobalMousePosition();

		// Smooth follow
		float t = 1f - Mathf.Exp(-ShapeSpeed * dt);
		var targetPos = _shapeCenter.Lerp(mousePos, t);

		// Limit movement speed
		var movement = targetPos - _shapeCenter;	
		if (MaxSpeed < 0)
		{
			float maxStep = MaxSpeed * dt;

			if (movement.Length() > maxStep)
				movement = movement.Normalized() * maxStep;
		}
		_shapeCenter += movement;
		
		
		// Find Points and apply forces
		var overlaps = _sbapi.GetPoints(_shapeCenter, Shape);
		foreach (var p in overlaps)
		{
			Vector2 globalPos = SoftBody.ToGlobal(p.Position);
			Vector2 globalDir = globalPos - _shapeCenter;
			if (globalDir.LengthSquared() < 0.000001f) continue;
			
			var ratio = Mathf.Clamp(ShapeCenterToPointDistanceRatio(p, _shapeCenter, Shape), 0f, 1f);
			var force = Force * DistanceToForceRatioCurve.SampleBaked(ratio);
			
			_sbapi.ApplyForce(p, globalDir.Normalized(), force);
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
					DrawCircle(_shapeCenter, circle.Radius, new Color(0.2f, 0.2f, 0.8f));
					break;
			}
			
			DrawLine(_shapeCenter, mousePos, new Color(0.5f, 0.5f, 0.5f));
		}
	}
}
