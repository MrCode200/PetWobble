#nullable enable
using Godot;

namespace TweenComp.Resources.Blueprint;

[GlobalClass]
public partial class CustomSignalData : Resource
{
    /// <summary>
    /// Path to the node that emits the signal. If null or empty, the target node is used.
    /// </summary>
    [Export] public NodePath? EmitterPath;
    
    /// <summary>
    /// Name of the signal to connect to.
    /// </summary>
    [Export] public required StringName CustomSignalName;
    
    /// <summary>
    /// Resolved emitter node (runtime only, not exported).
    /// </summary>
    public Node? Emitter;
}
