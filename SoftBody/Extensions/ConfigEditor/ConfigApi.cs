using System;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Optional. Implement on any node listed in ConfigScreen.Targets to get a custom tab title
/// and to be told when one of its exported values was changed in the UI.
/// </summary>
public interface IConfigurable
{
	string ConfigTitle { get; }
	void OnConfigChanged(string property);
}

/// <summary>
/// Optional. Lets a target add completely custom controls (buttons, previews, file pickers)
/// to the bottom of its tab, for things that are not plain [Export] values.
/// </summary>
public interface IConfigExtras
{
	void BuildConfigUI(Control parent);
}

/// <summary>
/// Optional. Implement on a node whose configurable values live on ANOTHER object
/// (a Resource, a behavior tree task, ...). ConfigScreen reads and writes the [Export]
/// values of ConfigObject, while the node itself still provides title, callbacks and extras.
/// Return null while the object is not available (the tab then shows a notice).
/// </summary>
public interface IConfigProxy
{
	GodotObject ConfigObject { get; }
}

/// <summary>Everything an editor factory needs to build a control for one property.</summary>
public sealed class PropContext
{
	public GodotObject Target { get; init; }
	public string Name { get; init; }
	public Variant.Type Type { get; init; }
	public PropertyHint Hint { get; init; }
	public string HintString { get; init; }

	/// <summary>Writes the value to the target, notifies IConfigurable and refreshes row visibility.</summary>
	public Action<Variant> Commit { get; init; }

	/// <summary>Like Commit but without writing: for values edited in place (Godot arrays are shared references).</summary>
	public Action Notify { get; init; }

	/// <summary>The C# type of the member, e.g. Godot.Collections.Array&lt;float&gt;. Null if unknown.</summary>
	public System.Type ClrType { get; init; }

	/// <summary>Set for synthetic contexts (array elements): used instead of reading the target.</summary>
	public Variant? ValueOverride { get; init; }

	public Variant Value => ValueOverride ?? Target.Get(Name);
}