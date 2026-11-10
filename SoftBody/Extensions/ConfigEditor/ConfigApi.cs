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

    public Variant Value => Target.Get(Name);
}