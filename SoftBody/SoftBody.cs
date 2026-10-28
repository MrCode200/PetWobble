using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Godot;
using Color = Godot.Color;
using Vector2 = Godot.Vector2;

namespace PetWobble.Const;

// I need to use texture mapped mesh for the slime
// A polygon 2d seems to work well with it as it allows to morph textures based on points

// if perf to low change to  record struct
public sealed class Point
{ 
	// CHECK: Prediction vs Position Usage...
	public Vector2 Position;          // committed position (end of last substep)
	public Vector2 Predicted;         // working position during a substep
	public Vector2 Velocity;
	public Vector2 PreSolveVelocity;  // velocity before constraints/collision (impact speed) / starting velocity 
	public Vector2 ContactNormal;
	public bool InContact;
	
	public float DistanceTo(Point point) => (point.Position - Position).Length();

	public override string ToString() => $"Point(Position: {Position}, Velocity: {Velocity})";
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
	// Used as collision skin: points are kept this far away from walls
	[Export] public float PointRadius = 0.5f;
	[Export] public float Radius = 90.0f;
	// If 0, will be calculated from the initial points
	[Export] public float DesiredArea = 0f;
	// 0..1: fraction of the area error corrected per solver iteration
	[Export(PropertyHint.Range, "0,1,0.01")] public float AreaPressureCoefficient = 0.1f;

	[ExportGroup("Physics")]
	[Export] public Vector2 Gravity = Vector2.Down;
	// Uses velocity *= exp(-Damping * deltaTime) 
	[Export] public float Damping = 0.5f;
	[Export] public float Substeps = 4;
	[Export] public int MaxCollisionResolveIterations = 5;

	[ExportGroup("Collisions")]
	[Export] public bool ScreenIsWall = true;
	[Export(PropertyHint.Layers2DPhysics)] public uint CollisionMask = 1;
	[Export] public bool RemoveNormalUponCollision = true;
	[Export(PropertyHint.Range, "0,1,0.01")] public float Friction = 0.02f;
	[Export] public float RestitutionThreshold = 30f;
	[Export] public float Restitution = 0.5f;
	
	[ExportGroup("Joints")] 
	[Export] public float JointRestLength = 10;
	[Export] public bool UseSpawnDistanceAsRestLength;
	[Export] public float JointStiffness = 0.5f;

	// Each Point being connected to the next one (first connected to last)
	[Export] public bool CircularJoints;

	[ExportGroup("Visuals")] 
	[Export] public Polygon2D BodyPolygon;
	[Export] public Texture2D BodyTexture;
	
	[ExportGroup("Debug")]
	[Export] public bool ShowVisuals = true;
	[Export] public bool DrawPoints = true;
	[Export] public bool DrawJoints = true;
	[Export] public float JointWidth = -1f;
	[Export] public bool DrawVelocity = true;

	private List<Action<SoftBody, float>> _customSolvers = [];
	private List<Action<SoftBody, float>> _oneStepSolvers = [];
	public Point[] Points;
	public SpringJoint[] Joints; 
	private Vector2[] _areaGradients;
	private float _desiredArea;
	private bool _reverseIterStep;

	private Vector2[] _verts;

	public override void _Ready()
	{
		InitPoints();
		InitJoints();
		InitBody();
		_areaGradients = new Vector2[Points.Length];
		_desiredArea = DesiredArea == 0 ? Mathf.Abs(SignedArea()) : DesiredArea;
		GD.Print("_desiredArea: " + _desiredArea);
	}
	
	private void InitPoints()
	{
		Points = new Point[PointCount];

		for (var i = 0; i < PointCount; i++)
		{
			var point =  new Point();
			
			float angle = Mathf.Tau / PointCount * i;
			Vector2 direction = new(Mathf.Cos(angle), Mathf.Sin(angle));

			Points[i] = new Point
			{
				Position = direction * Radius,
				Predicted = direction * Radius,
				Velocity = Vector2.Zero
			};
			GD.Print(point);
		}
	}

	private void InitJoints()
	{
		if (!CircularJoints)
		{
			Joints = [];
			return;
		}

		Joints = new SpringJoint[Points.Length];
		var prevPoint = Points[^1];
		for (int i = 0; i < Points.Length; i++)
		{
			var p = Points[i];
			var restLength = UseSpawnDistanceAsRestLength ? p.DistanceTo(prevPoint) : JointRestLength;
 
			Joints[i] = new SpringJoint
			{
				PointA = prevPoint,
				PointB = p,
				RestLength = restLength
			};
			prevPoint = p;
			GD.Print(Joints[i]);
		}
	}
	
	private void InitBody()
	{
		int n = Points.Length;
		_verts = new Vector2[n + 1]; // + 1 for center body
		UpdateBodyVertices();
		
		Vector2 texSize = BodyTexture.GetSize();
		var uv = new Vector2[n + 1];
		for (int i = 0; i < n; i++)
			uv[i] = (Points[i].Position / (Radius * 2) + new Vector2(0.5f, 0.5f)) * texSize;
		uv[n] = 0.5f * texSize;

		var tris = new Godot.Collections.Array();
		for (int i = 0; i < n; i++)
			tris.Add(new[] { n, i, (i + 1) % n });

		BodyPolygon.Texture = BodyTexture;
		BodyPolygon.UV = uv;
		BodyPolygon.Polygons = tris;
		// BodyPolygon.SetTextureFilter(TextureFilterEnum.Nearest);
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
		_verts[n] = c / n;

		BodyPolygon.Polygon = _verts;
	}

	public override void _PhysicsProcess(double delta)
	{
		float h = (float)delta / Substeps;
 
		for (int s = 0; s < Substeps; s++)
			Step(h);

		if (DrawJoints || DrawPoints || DrawVelocity)
			QueueRedraw();
		if (ShowVisuals != BodyPolygon.Visible)
			BodyPolygon.Visible = ShowVisuals;
	}
	
	private void Step(float h)
	{
		_reverseIterStep = !_reverseIterStep;
		
		// 1. Integrate & Predict
		float damp = Mathf.Exp(-Damping * h);
		foreach (var p in Points)
		{
			p.Velocity += Gravity * h;
			p.Velocity *= damp;
			p.PreSolveVelocity = p.Velocity;
			p.Predicted = p.Position + p.Velocity * h;
			p.InContact = false;
		}
		
		foreach (var solver in _customSolvers)
		{
			solver(this, h);
		}
		
		foreach (var solver in _oneStepSolvers)
		{
			solver(this, h);
		}
		
		// 2. Soft constraints
		SolveJoints(_reverseIterStep);
		SolveArea();
		
		// 3. Hard Constraint: Solve Collision
		SolveCollisions(_reverseIterStep);
		
		// 4. Updated Velocity from position
		UpdateVelocities(h);
		
		// 5. Update Visuals
		UpdateBodyVertices();
		
		_oneStepSolvers.Clear();
	}
	
	private void SolveJoints(bool reverse = false)
	{
		int n = Joints.Length;
		for (var i = 0; i < n; i++)
		{
			var j = Joints[reverse ? n - i - 1 : i];
			
			// Relative From Point A
			var dVec = j.PointB.Predicted - j.PointA.Predicted; // DistanceVector
			var d = dVec.Length(); 
			if (d < 0.000001f)
				continue;

			Vector2 direction = dVec.Normalized();
			float stretch = (d - j.RestLength) * 0.5f;
			Vector2 correction = direction * stretch * JointStiffness;
			
			j.PointA.Predicted += correction;
			j.PointB.Predicted -= correction;
		}
		
		// Jacobi accumulation (make it Jacobi averaging for more stable simulation)
		/*
		Vector2[] corrections = new Vector2[_joints.Length * 2];
		
		for (var i = 0; i < _joints.Length; i++)
		{
			var j = _joints[i];
			
			// Relative From Point A
			var dVec = j.PointB.Predicted - j.PointA.Predicted; // DistanceVector
			var d = dVec.Length(); 
			if (d < 0.000001f)
				continue;

			Vector2 direction = dVec.Normalized();
			float stretch = (d - j.RestLength) * 0.5f;
			Vector2 correction = direction * stretch * JointStiffness;
			
			corrections[i * 2] = correction;
			corrections[i * 2 + 1] = -correction;
		}

		for (var i = 0; i < _joints.Length; i++)
		{
			var j = _joints[i];
			
			j.PointA.Predicted += corrections[i * 2];
			j.PointB.Predicted += corrections[i * 2 + 1];		
		}*/
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
			Points[i].Predicted += _areaGradients[i] * s;
	}
	
	private float SignedArea()
	{
		float sum = 0;
		for (int i = 0; i < Points.Length; i++)
		{
			Vector2 a = Points[i].Predicted;
			Vector2 b = Points[(i + 1) % Points.Length].Predicted;
			sum += a.X * b.Y - b.X * a.Y;
		}
		return sum * 0.5f;
	}
	
	private void SolveCollisions(bool reverse = false)
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
			}
		}
	}

	private void UpdateVelocities(float h)
	{
		foreach (var p in Points)
		{
			Vector2 v = (p.Predicted - p.Position) / h;

			if (p.InContact && RemoveNormalUponCollision)
			{
				Vector2 n = p.ContactNormal;

				float vn = v.Dot(n);
				if (vn < 0f)
					v -= n * vn;

				Vector2 vt = v - n * v.Dot(n);
				v = vt * (1f - Friction);
			}
			else if (p.InContact)
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
				
				v = vt + n * newVn;
			}
			
			p.Velocity = v;
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
		foreach (var p in Points)
		{
			if (DrawPoints)
			{
				var pColor = p.InContact 
					? new Color(0.8f, 0.0f, 0f) 
					: new Color(0.5f, 0.5f, 0.7f);
				DrawCircle(
					p.Position,
					4.0f,
					pColor);
			}

			if (DrawVelocity)
			{
				Vector2 velocity = p.Velocity;
				if (velocity == Vector2.Zero)
					continue;
	
				DrawLine(
					p.Position,
					p.Position + velocity,
					new Color(0.5f, 0.5f, 0.5f));
			}
		}
		
		if (DrawJoints)
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
			
				DrawLine(
					j.PointA.Position,
					j.PointB.Position, jColor,
					JointWidth
				);
			}
		}
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
