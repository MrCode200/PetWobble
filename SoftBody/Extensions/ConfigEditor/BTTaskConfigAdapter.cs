using System;
using System.Collections.Generic;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Behavior tree tasks are Resources and the tree is cloned per BTPlayer at runtime, so the
/// task you see in the editor is NOT the one that runs. Each task registers its live instance
/// here from its own _Setup(), which guarantees the adapter edits the instance that is ticking.
/// Stored weakly so replaced instances (BT restart / reload) can be collected.
/// </summary>
public static class BTTaskRegistry
{
	private static readonly List<WeakReference<BTTask>> Tasks = new();

	public static void Register(BTTask task)
	{
		Prune();
		Tasks.Add(new WeakReference<BTTask>(task));
	}

	/// <summary>
	/// Most recently registered live task whose CustomName or C# class name equals <paramref name="name"/>.
	/// If <paramref name="agent"/> is given, only tasks acting on that agent match (several pets, one tree).
	/// </summary>
	public static BTTask Find(string name, Node agent = null)
	{
		for (int i = Tasks.Count - 1; i >= 0; i--) // newest first: a restarted tree wins over the stale one
		{
			if (!Tasks[i].TryGetTarget(out var t) || !GodotObject.IsInstanceValid(t)) continue;
			if (t.CustomName != name && t.GetType().Name != name) continue;
			if (agent != null && t.Agent != agent) continue;
			return t;
		}
		return null;
	}

	private static void Prune() =>
		Tasks.RemoveAll(w => !w.TryGetTarget(out var t) || !GodotObject.IsInstanceValid(t));
}

/// <summary>
/// Config adapter for one behavior tree task. Add it as a node, set TaskName, and put it into
/// ConfigScreen.Targets: the task's [Export] values then show up as a tab, like any other target.
///
/// The task must call BTTaskRegistry.Register(this) in its _Setup().
/// To expose another task, add another adapter node with a different TaskName.
/// If the task itself implements IConfigurable, its OnConfigChanged is called too.
/// </summary>
public partial class BTTaskConfigAdapter : Node, IConfigurable, IConfigProxy
{
	/// <summary>Tab title. Falls back to the node name.</summary>
	[Export] public string Title { get; set; } = "";

	/// <summary>The task's Custom Name (set in the BT editor) or its C# class name, e.g. "WobbleJumpAction".</summary>
	[Export] public string TaskName { get; set; } = "";

	/// <summary>Optional: only match the task running on this agent node.</summary>
	[Export] public Node Agent { get; set; }

	public string ConfigTitle => string.IsNullOrEmpty(Title) ? Name.ToString() : Title;

	// Looked up on every access, never cached: a re-instantiated tree must not leave us on a stale task.
	public GodotObject ConfigObject => BTTaskRegistry.Find(TaskName, Agent);

	public void OnConfigChanged(string property) =>
		(ConfigObject as IConfigurable)?.OnConfigChanged(property);
}