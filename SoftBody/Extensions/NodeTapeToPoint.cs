using System;
using System.Linq;
using Godot;
using Godot.Collections;
using PetWobble.Config;

namespace PetWobble.SoftBody.Extensions;

public partial class NodeTapeToPoint : Node2D, IConfigurable, IConfigExtras
{
	[Export] public Node2D Target;
	[Export] public SoftBody SoftBody;
	[Export] public bool IncludeCenterPoint = true;
	[Export] public Array<float> PointDirections;
	[Export] public bool RotateWithSoftBody = true;
	[Export] public bool GlobalDirection;
	[Export] public bool HideNode; // note: shadows CanvasItem.Hide(), consider renaming to HideTarget

	[ExportGroup("Debug")]
	[Export] public bool DrawDebug;

	private const float DebugCircleRadius = 2f;
	private static readonly Color DebugAnchorColor = new(1, 0, 0, 1);
	private static readonly Color DebugAverageColor = new(0, 1, 0, 1);
	private static readonly Color DebugLineColor = new(1, 1, 0, 1);

	private Point _referenceRotationPoint;
	private Point[] _anchorPoints;

	// sprite swapping
	private SkinPicker _picker;
	private Vector2 _defaultScale;

	// ------------------------------------------------------------------ config
	public string ConfigTitle => Name.ToString(); // tab title = node name, so name your nodes ("Eye", "Hat", ...)

	public void OnConfigChanged(string property)
	{
		switch (property)
		{
			case nameof(HideNode):
				ApplyVisibility();
				break;
			case nameof(PointDirections): // anchors depend on the directions -> recompute
				if (SoftBody?.Points != null) OnSoftBodyInitialized();
				break;
		}
	}

	public void BuildConfigUI(Control parent)
	{
		if (_picker == null || Target is not (Sprite2D or Polygon2D))
		{
			parent.AddChild(new Label { Text = "Target must be a Sprite2D or Polygon2D to swap its image." });
			return;
		}
		_picker.BuildUi(parent, $"Image of '{Name}' (scaled to fit the original size)");
	}

	private void ApplyVisibility()
	{
		if (Target != null) Target.Visible = !HideNode;
	}

	private Texture2D GetTexture() => Target switch
	{
		Sprite2D s => s.Texture,
		Polygon2D p => p.Texture,
		_ => null,
	};

	// Sets the texture and rescales the target so the new image fits the original image's footprint.
	private void ApplySkin(Texture2D tex)
	{
		switch (Target)
		{
			case Sprite2D s: s.Texture = tex; break;
			case Polygon2D p: p.Texture = tex; break;
			default: return;
		}

		var def = _picker?.DefaultTexture;
		if (tex == null || def == null || tex == def)
		{
			Target.Scale = _defaultScale;
			return;
		}

		Vector2 d = def.GetSize();
		Vector2 n = tex.GetSize();
		float k = Mathf.Min(d.X / n.X, d.Y / n.Y); // uniform "fit inside"
		Target.Scale = _defaultScale * k;
	}

	// ------------------------------------------------------------------ lifecycle
	public override void _Ready()
	{
		if (Target != null)
		{
			_defaultScale = Target.Scale;
			_picker = new SkinPicker(this, Name.ToString(), GetTexture, ApplySkin);
			Callable.From(_picker.LoadSaved).CallDeferred();
			ApplyVisibility();
		}

		SoftBody.SoftBodyInitialized += OnSoftBodyInitialized;
		if (SoftBody.Points != null) // SoftBody was ready before us -> we missed the signal
			OnSoftBodyInitialized();
	}

	private void OnSoftBodyInitialized()
	{
		_anchorPoints = (PointDirections ?? new Array<float>())
			.SelectMany<float, Point>(radian => SoftBody.SBApi.GetPointsAtRadians(radian))
			.ToArray();

		_referenceRotationPoint = SoftBody.SBApi.GetPointsAtRadians(0)[0];

		// Initialized fires again on every rebuild (point count / radius changed in the config),
		// so unsubscribe first or the handler would run several times per step.
		SoftBody.StepFinished -= OnStepFinished;
		SoftBody.StepFinished += OnStepFinished;
	}

	public override void _ExitTree()
	{
		SoftBody.StepFinished -= OnStepFinished;
		SoftBody.SoftBodyInitialized -= OnSoftBodyInitialized;
	}

	private void SetAnchorPoints()
	{
		_anchorPoints = (PointDirections ?? new Array<float>())
			.SelectMany<float, Point>(
				radian => SoftBody.SBApi.GetPointsAtRadians(
					radian + SoftBody.SBApi.GetGlobalLocalAngleOffset()
					)
				)
			.ToArray();
	}

	private void OnStepFinished()
	{
		if (Target == null || _anchorPoints == null) return;

		if (GlobalDirection)
			SetAnchorPoints();
		
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
			averagePosition += point.Position;

		if (IncludeCenterPoint)
			averagePosition += SoftBody.CenterPoint;

		return SoftBody.ToGlobal(averagePosition / pointCount);
	}

	private float CalculateNewRotation()
	{
		return SoftBody.CenterPoint.AngleToPoint(_referenceRotationPoint.Position);
	}

	public override void _Draw()
	{
		if (!DrawDebug || _anchorPoints == null) return;

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
