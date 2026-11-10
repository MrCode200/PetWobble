using System;
using Godot;
using PetWobble.Config;

namespace PetWobble.SoftBody;

// Second part of the existing partial class. Adds everything the config screen needs.
public partial class SoftBody : IConfigurable
{
	public string ConfigTitle => "Slime";

	private bool _rebuildQueued;

	/// <summary>
	/// Most exported values are read every substep and just work when changed.
	/// The ones below are baked in at init time, so they need an explicit update.
	/// </summary>
	public void OnConfigChanged(string property)
	{
		if (Points == null) return; // not initialized yet / editor

		switch (property)
		{
			// Change the point layout -> rebuild the blob (resets it to a circle)
			case nameof(PointCount):
			case nameof(Radius):
			case nameof(CircularJoints):
			case nameof(UseSpawnDistanceAsRestLength):
				QueueRebuild();
				break;

			// Baked into the SpringJoint records -> patch them in place, keeps the current shape
			case nameof(JointStiffness):
			case nameof(BendStiffness):
			case nameof(JointRestLength):
				ApplyLiveJointSettings();
				break;

			case nameof(DesiredArea):
			case nameof(CalculateDesiredArea):
				UpdateDesiredArea();
				break;
		}
	}

	private void QueueRebuild()
	{
		if (_rebuildQueued) return; // several changes in one frame -> one rebuild
		_rebuildQueued = true;
		Callable.From(() =>
		{
			_rebuildQueued = false;
			Initialize();
		}).CallDeferred();
	}
	
	// InitJoints adds, per point: [structural joint, bend joint] -> even index = structural, odd = bend
	private void ApplyLiveJointSettings()
	{
		for (int i = 0; i < Joints.Length; i++)
		{
			bool bend = i % 2 == 1;
			Joints[i] = Joints[i] with
			{
				Stiffness = bend ? BendStiffness : JointStiffness,
				RestLength = !bend && !UseSpawnDistanceAsRestLength ? JointRestLength : Joints[i].RestLength,
			};
		}
	}

	// ------------------------------------------------------------------ skin
	public void SetSkin(Texture2D texture)
	{
		Texture = texture;
		RebuildUv();
	}
}