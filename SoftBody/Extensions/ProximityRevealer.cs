using Godot;
using System;
using PetWobble.SoftBody;

public partial class ProximityRevealer : Node
{
	[Export] public float RevealDistance = 200;
	[Export] public float FadeWidth = 40;
	
	[ExportGroup("Dependencies")]
	[Export] public SoftBody SoftBody;
	[Export] public CanvasItem ToBeFadedIn;
	[Export] public Shader ProximityRevealShader;

	private ShaderMaterial _material;
	public override void _Ready()
	{
		_material = new ShaderMaterial
		{
			Shader = ProximityRevealShader
		};
		
		_material.SetShaderParameter(
			"reveal_distance",
			RevealDistance);
		_material.SetShaderParameter(
			"FadeWidth",
			FadeWidth);
		
		ToBeFadedIn.Material = _material;
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		_material.SetShaderParameter(
			"target_position",
			SoftBody.ToGlobal(SoftBody.CenterPoint));
	}
}