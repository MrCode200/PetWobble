using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Godot.Collections;
using Color = Godot.Color;
using Vector2 = Godot.Vector2;

namespace PetWobble.SoftBody;

// if perf to low change to  record struct
// Point could be a class as now; record struct would copy large-ish vectors on every access and may not help performance.
public sealed class Point
{ 
	public int Index { get; init; }
	
	public Vector2 Position;          // committed position (end of last substep)
	public Vector2 Predicted;         // working position during a substep
	public Vector2 Velocity;
	public Vector2 PreSolveVelocity;  // velocity before constraints/collision (impact speed) / starting velocity 
	public Vector2 ContactNormal;
	public bool InContact;
	
	// DEBUG DRAW VAR
	public bool InContactThisFrame;
	public Vector2 CollisionImpulse;
	public Vector2 SelfCollisionImpulse;

	private bool _frozen;
	public bool Frozen
	{
		get => _frozen;
		set
		{
			_frozen = value;
			if (!value) return;
			Velocity = Vector2.Zero;
			PreSolveVelocity = Vector2.Zero;
			Predicted = Position;
			InContact = false;
		}
	}	
	public float DistanceTo(Point point) => (point.Position - Position).Length();

	public Vector2 Displacement => Predicted - Position;

	public override string ToString() => $"Point(Position: {Position}, Velocity: {Velocity})";
}

public record struct SpringJoint
{
	public Point PointA { get; init; }
	public Point PointB { get; init; }
	public float RestLength { get; init; }
	public float Stiffness { get; init; }

	public Vector2 Direction => (PointB.Position - PointA.Position);

	public Vector2 Normal =>
		new (-(PointB.Position.Y - PointA.Position.Y), PointB.Position.X - PointA.Position.X);
	public float Lenght => Direction.Length();
	public bool CollidedThisFrame { get; set; }
}

[Tool]
[GlobalClass]
public partial class SoftBody : Polygon2D
{
	[ExportGroup("Points&Blob")] 
	[Export] public int PointCount = 32;
	// Used as collision skin: points are kept this far away from walls
	[Export] public float PointRadius = 0.5f;
	[Export] public float Radius = 90.0f;
	// If 0, will be calculated from the initial points
	[Export] public float DesiredArea;
	[Export] public bool CalculateDesiredArea = true;
	
	// 0..1: fraction of the area error corrected per solver iteration
	[Export(PropertyHint.Range, "0,1,0.01")] public float AreaPressureCoefficient = 0.1f;

	[ExportGroup("Physics")]
	[Export] public Vector2 Gravity = Vector2.Down;
	[Export] public float MaxSpeed = 100f;
	// Uses velocity *= exp(-Damping * deltaTime) 
	[Export] public float Damping = 0.5f;
	[Export(PropertyHint.Range, "1,64,1")]
	public int Substeps = 4;
	private bool _useDynamicSubsteps;
	[Export] public bool UseDynamicSubsteps
	{
		get => _useDynamicSubsteps;
		set
		{
			_useDynamicSubsteps = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public int MaxSubsteps = 24;
	[Export] public int MaxCollisionResolveIterations = 4;

	[ExportGroup("Collisions")] 
	[Export] public bool DisableSelfCollision = true;
	[Export(PropertyHint.Layers2DPhysics)] public uint CollisionMask = 1;
	private bool _useRestitution = true;
	[Export] public bool UseRestitution
	{
		get => _useRestitution;
		set
		{
			_useRestitution = value;
			NotifyPropertyListChanged();
		}
	}
	[Export(PropertyHint.Range, "0,1,0.01")] public float Friction = 0.02f;
	[Export] public float RestitutionThreshold = 30f;
	[Export] public float Restitution = 0.5f;
	[Export] public CollisionPolygon2D CollidablePolygon2D;
	[Export] public float PolygonPointOffset; // By how much each point should be moved more from the center.
	
	[ExportGroup("Joints")] 
	[Export] public float JointRestLength = 10;
	private bool _useSpawnDistanceAsRestLength;
	[Export] public bool UseSpawnDistanceAsRestLength
	{
		get => _useSpawnDistanceAsRestLength;
		set
		{
			_useSpawnDistanceAsRestLength = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public float JointStiffness = 0.5f;
	[Export] public float BendStiffness = 0.5f;
	// Each Point being connected to the next one (first connected to last)
	[Export] public bool CircularJoints;
	// If true, use jacobi averaging instead of accumulation
	[Export] public bool UseJointJacobiAveraging = true;

	[ExportGroup("Air")]
	private float _airDrag = 0.003f;
	[Export(PropertyHint.Range, "0,0.1,0.0000001")] public float AirDrag
	{
		get => _airDrag;
		set
		{
			_airDrag = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public float RearDragMultiplier = 0.2f;
	[Export] public Vector2 Wind = Vector2.Zero;
	// Air Density where x = relative distance from screen bottom and y = air density
	[Export] public Curve AirDensityCurve;
	
	[ExportGroup("Debug")]
	[Export] public bool ShowVisuals = true;

	private bool _drawCollidables = false;
	[Export]
	public bool DrawCollidables
	{
		get => _drawCollidables;
		set
		{
			_drawCollidables = value;
			GetTree().DebugCollisionsHint = value;
		}
	}
	[Export] public bool DrawPoints = false;
	[Export] public bool DrawCenterPoint = false;
	
	private bool _drawJoints = true;
	[Export] public bool DrawJoints
	{
		get => _drawJoints;
		set
		{
			_drawJoints = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public bool ShowCollidedJoints = true;
	[Export] public bool DrawCollisionImpulses = true;
	[Export] public bool DrawSelfCollisionImpulses = true;
	[Export] public float SelfCollisionArrowSizeMultiplier = 1;
	[Export] public float LineWidths = -1f;
	private bool _drawVelocity = false;
	[Export] public bool DrawVelocity
	{
		get => _drawVelocity;
		set
		{
			_drawVelocity = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public float ArrowVelocitySizeMultiplier = 1f;

	
	private bool _drawDrag = true;
	[Export] public bool DrawDrag
	{
		get => _drawDrag;
		set
		{
			_drawDrag = value;
			NotifyPropertyListChanged();
		}
	}
	[Export] public float ArrowDragSizeMultiplier = 1f;
	[Export] public float MaxDebugLineLength = 50f;
	
	[Signal] public delegate void SoftBodyInitializedEventHandler();
	[Signal] public delegate void StepFinishedEventHandler();

	private SoftBodyApi _sbapi;

	public SoftBodyApi SBApi
	{
		get
		{
			_sbapi ??= new SoftBodyApi(this);
			return _sbapi;
		}
	}
	
	private List<Action<SoftBody, float>> _customSolvers = [];
	private List<Action<SoftBody, float>> _oneStepSolvers = [];
	public Point[] Points;
	public SpringJoint[] Joints;
	public int SubstepsRemaining;
	
	private List<SpringJoint> _jointList = [];
	private Vector2[] _areaGradients;
	private float _desiredArea;
	private bool _reverseIterStep;
	private Vector2[] _jointCorrections;
	private int[] _jointCorrectionCounts;
	
	private Vector2I _screenSize;

	private Vector2[] _verts;
	private Vector2 _centerPoint;
	

	public Vector2 CenterPoint => _centerPoint;
	
	public override void _ValidateProperty(Dictionary property)
	{
		var propertyName = property["name"].AsString();

		var show = true;
		switch (propertyName)
		{
			case nameof(Substeps):
				if (UseDynamicSubsteps) show = false;
				break;
			case nameof(MaxSubsteps):
				if (!UseDynamicSubsteps) show = false;
				break;
			case nameof(RestitutionThreshold):
			case nameof(Restitution):
				if (!UseRestitution) show = false;
				break;
		}
        
		if (!show)
		{
			property["usage"] = (int)PropertyUsageFlags.NoEditor;
			return;
		}
        
		var (disable, hint) = propertyName switch
		{
			nameof(JointRestLength) => (UseSpawnDistanceAsRestLength,
				"Disabled when UseSpawnDistanceAsRestLength is true."),
			nameof(RearDragMultiplier) => (AirDrag == 0f,
				"Disabled when AirDrag is 0."),
			nameof(ArrowVelocitySizeMultiplier) => (!DrawVelocity,
				"Disabled when DrawVelocity is false."),
			nameof(ArrowDragSizeMultiplier) => (!DrawDrag,
				"Disabled when DrawDrag is false."),
			nameof(ShowCollidedJoints) => (!DrawJoints,
					"Disabled when DrawJoints is false."),
			_ => (false, "")
		};

		if (!string.IsNullOrEmpty(hint))
			property["hint_text"] = hint;
        
		if (disable)
			property["usage"] = (int)(property["usage"].AsInt32() | (long)PropertyUsageFlags.ReadOnly);
	}

	public override void _Ready()
	{
		if (Engine.IsEditorHint())
			return;
		
		_screenSize = DisplayServer.ScreenGetSize();
		AirDensityCurve.Bake();
		Initialize();
	}
	
	private void Initialize()
	{
		_jointList.Clear(); // InitJoints appends to this list, so it must be reset on rebuild
		InitPoints();
		InitJoints();
		InitBody();
		_areaGradients = new Vector2[Points.Length];
		UpdateDesiredArea();
		EmitSignalSoftBodyInitialized(); // lets the dragger etc. drop references to the old points
	}
	
	private void UpdateDesiredArea()
	{
		int n = Points.Length;
		// Area of the regular n-gon the blob spawns as
		_desiredArea = CalculateDesiredArea
			? 0.5f * n * Radius * Radius * MathF.Sin(Mathf.Tau / n)
			: DesiredArea;
	}

	
	private void InitPoints()
	{
		Points = new Point[PointCount];
		
		for (var i = 0; i < PointCount; i++)
		{
			float angle = Mathf.Tau / PointCount * i;
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

			Points[i] = new Point
			{
				Index = i,
				Position = direction * Radius,
				Predicted = direction * Radius,
				Velocity = Vector2.Zero
			};
			//GD.Print(Points[i]);
		}
	}

	private void InitJoints()
	{
		if (!CircularJoints)
		{
			Joints = [];
			return;
		}
		
		var n = Points.Length;

		Joints = new SpringJoint[n];
		_jointCorrections = new Vector2[n];
		_jointCorrectionCounts = new int[n];

		for (int i = 0; i < n; i++)
		{
			var p = Points[i];
			var prev = Points[(i - 1 + n) % n];
			AddJoint(p, prev, JointStiffness, UseSpawnDistanceAsRestLength ? null : JointRestLength);
			AddJoint(p, Points[(i + 2) % n], BendStiffness);
		}
		
		Joints = _jointList.ToArray();
		//GD.Print(Joints);
	}
	
	private void AddJoint(Point a, Point b, float stiffness, float? rest = null) 
		=> _jointList.Add(new SpringJoint
		{
			PointA = a, PointB = b, Stiffness = stiffness,
			RestLength = rest ?? a.DistanceTo(b)
		}); 
	
	private void InitBody()
	{
		int n = Points.Length;
		_verts = new Vector2[n + 1]; // + 1 for center body
		UpdateBodyVertices();
		
		RebuildUv();

		var tris = new Godot.Collections.Array();
		for (int i = 0; i < n; i++)
			tris.Add(new[] { n, i, (i + 1) % n });

		Polygons = tris;
		// BodyPolygon.SetTextureFilter(TextureFilterEnum.Nearest);
	}

	private void RebuildUv()
	{
		if (Points == null || Texture == null) return; // Initialize() will do it later

		int n = Points.Length;
		Vector2 texSize = Texture.GetSize();
		var uv = new Vector2[n + 1];
		for (int i = 0; i < n; i++)
		{
			float angle = Mathf.Tau / n * i;
			var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			uv[i] = (dir * 0.5f + new Vector2(0.5f, 0.5f)) * texSize;
		}
		uv[n] = 0.5f * texSize;
		UV = uv;
	}
	
	private void UpdateBodyVertices()
	{
		int n = Points.Length;
		Vector2 c = Vector2.Zero;
		
		for (int i = 0; i < n; i++)
		{
			_verts[i] = Points[i].Position;
			c += Points[i].Position;
		}
		_centerPoint = c / n;
		
		CollidablePolygon2D.Polygon = _verts[..n]
			.Select(p => p + _centerPoint.DirectionTo(p).Normalized() * PolygonPointOffset)
			.ToArray();
		
		_verts[n] = _centerPoint;

		Polygon = _verts;
	}

	public override void _PhysicsProcess(double delta)
	{
		if (Engine.IsEditorHint())
			return;
		 
		// Debug per Draw Call Resets
		foreach (var p in Points)
		{
			p.InContactThisFrame = false;
			p.CollisionImpulse = Vector2.Zero;
			p.SelfCollisionImpulse = Vector2.Zero;
		}

		for (int k = 0; k < Joints.Length; k++)
		{
			Joints[k].CollidedThisFrame = false;
		}
		
		if (UseDynamicSubsteps)
		{
			var topSpeed = Points.Max(p => p.Velocity.Length());
			var maxRelDisplacement = topSpeed * 2 * (float)delta;
			var dynamicStep = (int)MathF.Ceiling(maxRelDisplacement / PointRadius);
			Substeps = Math.Clamp(dynamicStep, 1, MaxSubsteps);
		}
		float h = (float)delta / Substeps;

		for (SubstepsRemaining = Substeps;
		     SubstepsRemaining > 0; 
		     SubstepsRemaining--)
		{
			Step(h);
		}
		
		_oneStepSolvers.Clear();
		EmitSignal(nameof(StepFinished));

		QueueRedraw();
		if (ShowVisuals && Color.A.Equals(0))
			Color = new Color(1, 1, 1, 1);
		else if (!ShowVisuals && Color.A.Equals(1))
			Color = new Color(1, 1, 1, 0);
	}
	
	private void Step(float h)
	{
		_reverseIterStep = !_reverseIterStep;
		
		// 1. Integrate & Predict
		float damp = Mathf.Exp(-Damping * h);
		foreach (var p in Points)
		{
			if (p.Frozen)
			{
				p.Predicted = p.Position;
				p.InContact = false;
				continue;
			}
			
			p.Velocity += Gravity * h;
			p.Velocity *= damp;
		}
		
		ApplyAirDrag(h);

		foreach (var p in Points)
		{
			if (p.Frozen) continue;
			p.PreSolveVelocity = p.Velocity;
			p.Predicted = p.Position + p.Velocity * h;
			p.InContact = false;
		}
		
		// 2.A Custom Solvers
		foreach (var solver in _customSolvers)
		{
			solver(this, h);
		}
		
		foreach (var solver in _oneStepSolvers)
		{
			solver(this, h);
		}
		
		// 2.B Soft constraints
		SolveJoints(_reverseIterStep);
		SolveArea();
		
		// 3. Hard Constraint: Solve Collision
		if (!DisableSelfCollision)
			SolveSelfCollisions(_reverseIterStep);
		SolveEnvironmentCollisions(_reverseIterStep);
		
		
		// 4. Updated Velocity from position
		UpdateVelocities(h);
		
		// 5. Update Visuals
		UpdateBodyVertices();
	}

	private void ApplyAirDrag(float h)
	{
		// HOW AIR DRAG WORKS:
		// 1. Shear Stress (Friction Drag) = Tangential Force to the objects surface, 
		// they are caused by the frictional forces
		
		// 2. Pressure Stresses (Pressure Drag) = Normal Force to the objects surface
		// they are caused by how pressure is distributed around the object
		// ---
		// Pressure Drag is caused by the difference in pressure between the front and back
		// of the object moving direction (Force seperation)
		// 
		
		int n = Points.Length;
		for (int i = 0; i < n; i++)
		{
			var p = Points[i];
			if (p.Frozen)
				continue;

			// NOTE: uses .Position as its at the velocity calculations step;
			var prev = Points[(i - 1 + n) % n].Position;
			var next = Points[(i + 1) % n].Position;
			
			var t = next - prev;
			var segLen = t.Length() * 0.5f;
			if (segLen < 0.000001f) continue;
			
			var normal = new Vector2(t.Y, -t.X).Normalized();
			var vRel = p.Velocity - Wind;
			
			float vn = vRel.Dot(normal);
			// Velocity is not aligned with the outward direction of the point
			// Thus no air is hitting that point
			var relHeight = 1f - ToGlobal(p.Position).Y / _screenSize.Y;
			var drag = (vn > 0 ? AirDrag : AirDrag * RearDragMultiplier) 
			           * AirDensityCurve.SampleBaked(relHeight);
			
			
			// Clamp so that drag may never reverse the velocity of the obj
			var f = Mathf.Min(1f, drag * segLen * Mathf.Abs(vn) * h);
			// taking the normalVec * shadowStrength(here vn) and subtracting it from the velocity
			// rmvs all the vector direction against the vec
			// the clamp prevents drag from reversing the normal component of the relative velocity in one substep
			p.Velocity += -normal * vn * f;
		}
	}

	private void SolveJoints(bool reverse = false)
	{
		if (Joints.Length == 0)
			return;
		
		// calculate corrections
		int n = Joints.Length;
		for (var i = 0; i < n; i++)
		{
			var j = Joints[reverse ? n - i - 1 : i];
			
			var a = j.PointA;
			var b = j.PointB;			
			
			// Relative From Point A
			var dVec = b.Predicted - a.Predicted; // DistanceVector
			var d = dVec.Length(); 
			if (d < 0.000001f)
				continue;

			Vector2 direction = dVec.Normalized();
			float stretch = d - j.RestLength;
			
			if (a.Frozen && b.Frozen)
				continue;
			
			if (!a.Frozen && !b.Frozen)
				stretch *= 0.5f;
			
			Vector2 correction = direction * stretch * j.Stiffness;
			
			if (!a.Frozen)
			{
				_jointCorrections[a.Index] += correction;
				_jointCorrectionCounts[a.Index]++;
			}

			if (!b.Frozen)
			{
				_jointCorrections[b.Index] -= correction;
				_jointCorrectionCounts[b.Index]++;
			}
		}
		
		// jacobi accumulation vs averaging option in export
		for (int i = 0; i < Points.Length; i++)
		{
			int count = _jointCorrectionCounts[i];
			if (count == 0 || Points[i].Frozen)
				continue;

			var correction = UseJointJacobiAveraging 
				? _jointCorrections[i] / count 
				: _jointCorrections[i];
			
			Points[i].Predicted += correction;
			
			_jointCorrections[i] = Vector2.Zero;
			_jointCorrectionCounts[i] = 0;
		}
	}
	
	private void SolveArea()
	{
		if (_desiredArea < 0.000001f)
			return;

		float area = SignedArea();
		if (!float.IsFinite(area))
			return;

		int n = Points.Length;
		float denom = 0; // demoniator / Nenner
		for (int i = 0; i < n; i++)
		{
			if (Points[i].Frozen)
				continue;
			
			Vector2 prev = Points[(i - 1 + n) % n].Predicted;
			Vector2 next = Points[(i + 1) % n].Predicted;
			
			// Get Vector2 diff = (prev - next)
			// Mirror at y axis (-y)
			// Mirror at origin, where direction is the normal: (x, -y) -> (-y, x)
			
			// Why Gradient is not Normalized!:
			// 1. The longer the distance between the neighbours, the stronger this point influences the area, 
			// thus they should receive larger corrections
			// 2. 
			Vector2 gradient = 0.5f * new Vector2(next.Y - prev.Y, prev.X - next.X); // CHECK: why not normalized?
			_areaGradients[i] = gradient;

			denom += gradient.LengthSquared();
		}
		
		if (denom < 0.000001f)
			return;
		
		// Why division by denom!:
		// 1. The larger the areaError, the larger should the correction be, 
		// the smaller the denom/change is, relative to the error, thus changes the influence of the gradients...
		float s = (_desiredArea - area) / denom * AreaPressureCoefficient;
		for (int i = 0; i < n; i++)
		{
			if (Points[i].Frozen)
				continue;
			Points[i].Predicted += _areaGradients[i] * s;
		}
	}

	private float SignedArea(bool usePredicted = true)
	{
		float sum = 0;
		for (int i = 0; i < Points.Length; i++)
		{
			Vector2 a = usePredicted 
				? Points[i].Predicted 
				: Points[i].Position;
			
			Vector2 b = usePredicted ? 
				Points[(i + 1) % Points.Length].Predicted 
				: Points[(i + 1) % Points.Length].Position;
			
			sum += a.X * b.Y - b.X * a.Y;
		}
		return sum * 0.5f;
	}

	private void SolveSelfCollisions(bool reverse = false)
	{
		// TODO: for more efficient collision checks implement a collision map (cell map)
		var n = Points.Length;
		for (int i = 0; i < n; i++)
		{
			var p = Points[reverse ? n - i - 1 : i];

			bool isAntiBentJoint = true; // Using this we check collision only with the outer layer
			
			for (int k = 0; k < Joints.Length; k++)
			{
				var j = Joints[k];
				isAntiBentJoint = !isAntiBentJoint;
				if (isAntiBentJoint || p == j.PointA || p == j.PointB)
					continue; // Ignore collision with its own joints

				if (!SegmentsIntersect(
					    p.Position, p.Predicted,
					    j.PointA.Predicted, j.PointB.Predicted,
					    out var intersectCoords,
					    out var collisionNormalB
					    ))
					continue;

				var remaining = p.Predicted - intersectCoords;
				//var slide = remaining - collisionNormalB * collisionNormalB.Dot(remaining);

				if (DrawCollisionImpulses)
					p.SelfCollisionImpulse += remaining.Length() * collisionNormalB;
				if (ShowCollidedJoints)
					j.CollidedThisFrame = true;
				
				p.Predicted = intersectCoords + (collisionNormalB * PointRadius);
				j.PointA.Predicted -= (collisionNormalB * PointRadius / 2) * remaining.Length();
				j.PointB.Predicted -= (collisionNormalB * PointRadius / 2) * remaining.Length();
				//GD.Print($"Intersect Found: {p}");
			}
		}
	}

	
	public static bool SegmentsIntersect(
		Vector2 aStart, Vector2 aEnd,
		Vector2 bStart, Vector2 bEnd,
		out Vector2 intersectCoords,
		out Vector2 collisionNormalB)
	{
		var segA = (aEnd - aStart);
		var segB = bEnd - bStart;
		var segA1B1 = (bStart - aStart);
		// The sign of the cross product changes if the b vector is on the other side
		// Thats why we/I can check using the cross product if there ws an intersection
		
		// To determine if the cross happened in the joint lenght
		// We check the relative area between the points, if they are under 0 or above 1 then 
		// the vectors have crossed outside the joint lenght ;))
		
		// denom is the bottom num of a division; here symoblizes total area
		var denom = segA.Cross(segB);
		if (Mathf.Abs(denom) < 0.00001f)
		{
			intersectCoords = Vector2.Zero;
			collisionNormalB = Vector2.Zero;
			return false; // Is this correct? if here is collinear then area 0 but actually still intersecting
		}
		
		var overlapSegA = segA1B1.Cross(segB) / denom;
		var overlapSegB = segA1B1.Cross(segA) / denom;
		
		intersectCoords = aStart + segA * overlapSegA;
		collisionNormalB = new Vector2(-segB.Y, segB.X).Normalized();
		if (segA.Dot(collisionNormalB) > 0)
			collisionNormalB = -collisionNormalB;
		
		return overlapSegA is >= 0 and <= 1 && 
		       overlapSegB is >= 0 and <= 1;
		// intersect
	}
	
	private void SolveEnvironmentCollisions(bool reverse = false)
	{
		var count = Points.Length;
		for (int k = 0; k < count; k++)
		{
			var p = Points[reverse ? count - k - 1 : k]; 
			
			for (int i = 0; i < MaxCollisionResolveIterations; i++)
			{
				if (!TryGetCollisionInfo(
						fromLocal: p.Position, p.Predicted, 
						out Vector2 hitPos, out Vector2 normal))
					break;
				
				// TODO: try make the slide be in the reflected direction
				Vector2 remaining = p.Predicted - hitPos;
				// Slide is the remaining distance, assuming the wall
				// takes in all momentum perpendicularly to its normal after collision
				Vector2 slide = remaining - normal * remaining.Dot(normal);
				
				p.Predicted = hitPos + normal * PointRadius + slide;
				p.ContactNormal = normal;
				p.InContact = true;
				p.InContactThisFrame = true;
			}
		}
	}

	private void UpdateVelocities(float h)
	{
		foreach (var p in Points)
		{
			Vector2 v = (p.Predicted - p.Position) / h;

			if (p.Frozen)
			{
				p.Velocity = v.LimitLength(MaxSpeed);
				p.Position = p.Predicted;
				continue;
			}			
			
			if (p.InContact && UseRestitution)
			{
				Vector2 n = p.ContactNormal;
				// vnImpact < 0, means velocity and normal (which is perpendicular to the wall) are pointing in opposite directions
				// thus our point is moving into the wall
				float vnImpact = p.PreSolveVelocity.Dot(n); 
				float vn = v.Dot(n);
				Vector2 vt = (v - n * vn) * (1 - Friction); // Velocity after the momentum taken by the wall

				float newVn = Mathf.Max(vn, 0f); // Removes the velocity that is pointing into the wall
				if (vnImpact < -RestitutionThreshold)
					newVn = Mathf.Max(vn, -vnImpact * Restitution);

				if (DrawCollisionImpulses)
					p.CollisionImpulse += n * (newVn - vn);
				
				v = vt + n * newVn;
			}
			else if (p.InContact)
			{
				Vector2 n = p.ContactNormal;
				
				float vn = v.Dot(n);
				if (vn < 0f)
				{
					if (DrawCollisionImpulses)
						p.CollisionImpulse += n * -vn;
					v -= n * vn;
				}
				
				Vector2 vt = v - n * v.Dot(n);
				// MathF.Pow(target, power), where after Substeps, Friction reaches target
				var substepFriction = MathF.Pow(1f - Friction, h);
				v = vt * (1f - substepFriction);
			}

			p.Velocity = v.LimitLength(MaxSpeed);
			p.Position = p.Predicted;
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
		
		if (fromGlobal.DistanceSquaredTo(toGlobal) < 0.0000001f) return false;

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
			CollideWithBodies = true,
			CollisionMask = CollisionMask
		};

		var hit = space.IntersectRay(parameters);
		if (hit.Count == 0)
			return false;

		Vector2 globalNormal = hit["normal"].AsVector2();
		if (globalNormal.LengthSquared() < 0.000001f)
			return false; // started inside a collider: no usable normal
 
		collisionLocalPos = ToLocal(hit["position"].AsVector2());
		collisionLocalNormal = ToLocalDirection(globalNormal).Normalized();

		return true;
	}

	private Vector2 ToLocalDirection(Vector2 globalDirection)
	{
		return ToLocal(globalDirection) - ToLocal(Vector2.Zero);
	}

	/*private static Vector2 ReflectVelocity(
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
	}*/

	public override void _Draw()
	{
		if (DrawPoints || DrawVelocity || DrawDrag || DrawCollisionImpulses || DrawSelfCollisionImpulses)
		{
			var n = Points?.Length ?? 0;
			for (int i = 0; i < n; i++)
			{
				var p = Points[i];

				if (DrawPoints)
				{
					var pColor = p.InContactThisFrame
						? new Color(0.8f, 0.0f, 0f)
						: new Color(0.5f, 0.5f, 0.7f);

					if (!p.Frozen)
						
						DrawCircle(
							p.Position,
							4.0f,
							pColor);
					else
					{
						DrawLine(
							p.Position + new Vector2(-1, -1) * PointRadius * 4,
							p.Position + new Vector2(1, 1) * PointRadius * 4,
							Colors.Black, LineWidths);
						DrawLine(
							p.Position + new Vector2(1, -1) * PointRadius * 4,
							p.Position + new Vector2(-1, 1) * PointRadius * 4,
							Colors.Black, LineWidths);
					}
				}

				if (DrawVelocity)
				{
					Vector2 velocity = p.Velocity;
					if (velocity == Vector2.Zero)
						continue;

					var velLength = velocity.Length();
					var velRatio = Mathf.Min(velLength / MaxSpeed, 1f) * ArrowVelocitySizeMultiplier;
					var scaledVel = velocity.Normalized() * velRatio * MaxDebugLineLength;

					DrawArrow(
						p.Position,
						p.Position + scaledVel,
						new Color(0.5f, 0.5f, 0.5f),
						LineWidths);
				}

				if (DrawDrag)
				{
					if (p.Frozen)
						continue;

					var prev = Points[(i - 1 + n) % n].Position;
					var next = Points[(i + 1) % n].Position;

					var t = next - prev;
					var segLen = t.Length() * 0.5f;
					if (segLen < 0.000001f) continue;

					var normal = new Vector2(t.Y, -t.X).Normalized();
					float vn = (p.Velocity - Wind).Dot(normal);
					if (Mathf.Abs(vn) < 0.0001f) continue;

					var relHeight = 1f - ToGlobal(p.Position).Y / _screenSize.Y;
					var dragCoeff = (vn > 0 ? AirDrag : AirDrag * RearDragMultiplier)
					                * AirDensityCurve.SampleBaked(relHeight);

					float h = 1f / Engine.PhysicsTicksPerSecond / Substeps; // same h as the sim
					float raw = dragCoeff * segLen * Mathf.Abs(vn) * h; // before clamping
					float factor = Mathf.Min(1f, raw) * ArrowDragSizeMultiplier; // 0..1, what is actually applied

					// Length: 0..MaxDebugLineLength, proportional to the applied fraction
					// Color: green (weak) -> red (strong)
					Color col = new Color(0.3f, 0.8f, 0.3f).Lerp(new Color(0.9f, 0.15f, 0.1f), factor);
					var start = p.Position + normal * PointRadius;
					Vector2 tip = p.Position + normal * (factor * MaxDebugLineLength);

					var pushesInward = vn > 0;
					DrawArrow(start, tip, col, LineWidths, headAtStart: pushesInward);

					// Marker where the clamp kicked in: drag is stronger than what is applied
					if (raw * ArrowDragSizeMultiplier > 1f)
						DrawCircle(tip, 2f, Colors.White);
				}

				if (DrawCollisionImpulses)
				{
					DrawImpulseArrow(p, p.CollisionImpulse, 1);
				}

				if (DrawSelfCollisionImpulses)
				{
					DrawImpulseArrow(p, p.SelfCollisionImpulse, SelfCollisionArrowSizeMultiplier);
				}
			}
		}
		
		if (DrawJoints && Joints is not null)
		{
			foreach (var j in Joints)
			{
				float distance = j.PointA.Position.DistanceTo(j.PointB.Position);
				float ratio = distance / j.RestLength;
				float deviation = Mathf.Abs(ratio - 1.0f);
			
				// Interpolate from blue (ideal) to red (compressed/stretched)
				// deviation 0 = blue, deviation 0.5+ = red
				float t = Mathf.Clamp(deviation * 2.0f, 0f, 1f);
				Color jColor = new Color(
					t,           // red increases with deviation
					0.2f * (1 - t), // green decreases
					1.0f - t     // blue decreases
				);
			
				var jointVec = j.PointB.Position - j.PointA.Position;
				var jointLength = jointVec.Length();
				var normalizedJoint = jointLength > MaxDebugLineLength 
					? jointVec.Normalized() * MaxDebugLineLength 
					: jointVec;
				
				DrawLine(
					j.PointA.Position,
					j.PointA.Position + normalizedJoint, jColor,
					(ShowCollidedJoints && j.CollidedThisFrame) ? LineWidths * 100f : LineWidths
				);
			}
		}
		
		if (DrawCenterPoint)
		{
			DrawCircle(CenterPoint, 4f, new Color(1f, 0.5f, 0.5f));
		}
	}

	private void DrawImpulseArrow(Point p, Vector2 impulse, float arrowSizeMultiplier)
	{
		if (impulse == Vector2.Zero)
			return;
					
		var factor = Mathf.Min(1f, impulse.Length()) * arrowSizeMultiplier;
		Color clr = new Color(0.3f, 0.8f, 0.3f).Lerp(new Color(0.9f, 0.15f, 0.1f), factor);

		if (impulse.Length() > MaxDebugLineLength)
		{
			impulse = p.CollisionImpulse.Normalized() * MaxDebugLineLength;
			DrawCircle(p.Position + impulse, 2f, Colors.White);
		}					
					
		DrawArrow(p.Position, p.Position + impulse,
			clr,
			LineWidths);
	}
	
	private void DrawArrow(Vector2 from, Vector2 to, Color color, float width,
		float headLength = 6f, float headAngle = 0.5f, bool headAtStart = false)
	{
		Vector2 d = to - from;
		float len = d.Length();
		if (len < 0.001f)
			return;

		Vector2 dir = d / len;
		float head = Mathf.Min(headLength, len * 0.5f);   // short arrows get a proportionally small head

		DrawLine(from, to, color, width);

		Vector2 tip = headAtStart ? from : to;
		Vector2 back = headAtStart ? dir : -dir;
		DrawLine(tip, tip + back.Rotated(headAngle) * head, color, width);
		DrawLine(tip, tip + back.Rotated(-headAngle) * head, color, width);
	}
	
	// --------------------------------------------------- API --------------------------------------------------------
	public void AddCustomSolver(Action<SoftBody, float> solver)
	{
		_customSolvers.Add(solver);
	}
	
	public void RemoveCustomSolver(Action<SoftBody, float> solver)
	{
		_customSolvers.Remove(solver);
	}
	
	public void AddOneStepSolver(Action<SoftBody, float> solver)
	{
		_oneStepSolvers.Add(solver);
	}
}
