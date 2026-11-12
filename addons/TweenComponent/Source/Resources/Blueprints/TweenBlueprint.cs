#nullable enable
using System;
using System.Linq;
using Godot;
using Godot.Collections;
using TweenComp.Resources.Overrides;

namespace TweenComp.Resources.Blueprint;

public enum TweenTrigger
{
    Manual,
    Ready,
    Show,
    Hide,
    MouseEnter,
    MouseExit,
    CustomSignal
}

public enum TweenLoopMode
{
    None,
    Loop,
    PingPong,
    // ReverseOnRetrigger
}

public enum CurveModeForLoop
{
    None,
    Mirror
}

/// <summary>
/// Defines how a tween treats other tweens that request to play while it is active.
/// </summary>
public enum TweenConflictPolicy
{
    /// <summary>
    /// Reject other tween requests. Protects this tween from interruption or queuing.
    /// </summary>
    RejectRequest,
    
    /// <summary>
    /// Allow other tweens to interrupt this tween. The requesting tween's policy determines the outcome.
    /// </summary>
    AllowRequest,
    
    /// <summary>
    /// Allow other tweens to queue behind this tween. The requesting tween must also be willing to queue.
    /// </summary>
    AllowQueue
}

/// <summary>
/// Defines how a tween wants to be treated when it is requested but cannot play immediately.
/// </summary>
public enum TweenWhenRequestedPolicy
{
    /// <summary>
    /// Drops the request if the TweenComponent is already playing another tween.
    /// </summary>
    DropRequest,
    
    /// <summary>
    /// Try to interrupt the active tween. Respects the active tween's OtherConflictPolicy.
    /// If the active tween rejects or only allows queuing, this request will fail.
    /// </summary>
    TryInterrupt,
    
    /// <summary>
    /// Force interrupt the active tween, ignoring the active tween's OtherConflictPolicy.
    /// The active tween will be stopped regardless of its settings.
    /// </summary>
    ForcePlay,
    
    /// <summary>
    /// Queue the request if the active tween allows queuing. If the active tween rejects queuing,
    /// this request will fail.
    /// </summary>
    QueueIfAllowed,
    
    /// <summary>
    /// Always queue the request, even if the active tween's OtherConflictPolicy is set to Reject.
    /// This bypasses the active tween's queue permission.
    /// </summary>
    ForceQueue
}

public enum QueueOverflowPolicy
{
    DropOldest,
    RejectNewest
}

public enum OnKillPolicy
{
    Ignore,
    JumpToStartingValues,
    JumpToFinalValues
}

[GlobalClass]
[Tool]
public partial class TweenBlueprint : Resource
{
    private Array<TweenTrigger> _triggers = [TweenTrigger.Ready];

    [Export] public Array<TweenStep> Steps = [new()];

    [Export]
    public Array<TweenTrigger> Triggers
    {
        get => _triggers;
        set
        {
            _triggers = value;
            NotifyPropertyListChanged();
        }
    }

    [Export] public Array<CustomSignalData> CustomSignals = [];

    [ExportGroup("Looping")] private TweenLoopMode _loopMode = TweenLoopMode.None;

    [Export]
    public TweenLoopMode LoopMode
    {
        get => _loopMode;
        set
        {
            _loopMode = value;
            NotifyPropertyListChanged();
        }
    }

    [Export(PropertyHint.Range, "0, 99, 1")] public int MaxLoopCount;
    [Export] public CurveModeForLoop CurveModeForLoop = CurveModeForLoop.Mirror;

    [ExportGroup("PlayBackBehaviour")]
    private TweenConflictPolicy _selfConflictPolicy = TweenConflictPolicy.AllowRequest;

    [Export]
    public TweenConflictPolicy SelfConflictPolicy
    {
        get => _selfConflictPolicy;
        set
        {
            _selfConflictPolicy = value;
            NotifyPropertyListChanged();
        }
    }

    private TweenConflictPolicy _otherConflictPolicy = TweenConflictPolicy.AllowRequest;

    [Export]
    public TweenConflictPolicy OtherConflictPolicy
    {
        get => _otherConflictPolicy;
        set
        {
            _otherConflictPolicy = value;
            NotifyPropertyListChanged();
        }
    }

    private TweenWhenRequestedPolicy _whenRequestedPolicy = TweenWhenRequestedPolicy.TryInterrupt;

    [Export]
    public TweenWhenRequestedPolicy WhenRequestedPolicy
    {
        get => _whenRequestedPolicy;
        set
        {
            _whenRequestedPolicy = value;
            NotifyPropertyListChanged();
        }
    }

    [Export] public int MaxQueueCount = -1;
    [Export] public QueueOverflowPolicy QueueOverflowPolicy = QueueOverflowPolicy.RejectNewest;
    [Export] public OnKillPolicy OnKillPolicy = OnKillPolicy.JumpToStartingValues;
    [Export] public Tween.TweenPauseMode PauseMode = Tween.TweenPauseMode.Bound;

    
    [ExportGroup("Post-Finish")] 
    
    [Export] public bool HideTargetOnFinish;
    [Export] public bool QueueFreeTargetOnFinish;

    [ExportGroup("")] // [Export] public float DelayAnimStart;

    private bool _disable;

    [Export]
    public bool Disable
    {
        get => _disable;
        set
        {
            _disable = value;
            NotifyPropertyListChanged();
        }
    }


    public override void _ValidateProperty(Dictionary property)
    {
        var propertyName = property["name"].AsString();

        var show = propertyName switch
        {
            nameof(CustomSignals) => Triggers.Contains(TweenTrigger.CustomSignal),
            nameof(MaxLoopCount) => LoopMode != TweenLoopMode.None,
            nameof(MaxQueueCount) or nameof(QueueOverflowPolicy) => 
                SelfConflictPolicy == TweenConflictPolicy.AllowQueue || 
                WhenRequestedPolicy == TweenWhenRequestedPolicy.QueueIfAllowed ||
                WhenRequestedPolicy == TweenWhenRequestedPolicy.ForceQueue,
            nameof(OnKillPolicy) => 
                SelfConflictPolicy == TweenConflictPolicy.RejectRequest ||
                WhenRequestedPolicy == TweenWhenRequestedPolicy.TryInterrupt ||
                WhenRequestedPolicy == TweenWhenRequestedPolicy.ForcePlay,
            nameof(CurveModeForLoop) => LoopMode == TweenLoopMode.PingPong,

            _ => true
        };

        if (!show)
        {
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
            return;
        }
        
        var disable = propertyName switch
        {
            nameof(Disable) => false,
            nameof(CurveModeForLoop) => !Steps.Any(x => x.CustomCurve is not null),
            _ => Disable
        };
        
        if (disable)
            property["usage"] = (int)(property["usage"].AsInt32() | (long)PropertyUsageFlags.ReadOnly);
    }
    
    // ----------------------------------------------------- HELPERS --------------------------------------------------

    public float GetDuration()
    {
        return Steps.Sum(x => x.Duration + x.Delay);
    }

    /// <summary>
    /// Creates a clone of this blueprint with override data applied.
    /// The original blueprint remains unchanged.
    /// </summary>
    public TweenBlueprint CloneWithOverride(TweenPlaybackOverride overrideData)
    {
        var clone = Duplicate() as TweenBlueprint;
        if (clone == null)
            throw new InvalidOperationException("Failed to duplicate TweenBlueprint");

        // Apply playback-level overrides
        if (overrideData.LoopMode.HasValue)
            clone.LoopMode = overrideData.LoopMode.Value;
        if (overrideData.MaxLoopCount.HasValue)
            clone.MaxLoopCount = overrideData.MaxLoopCount.Value;
        if (overrideData.CurveModeForLoop.HasValue)
            clone.CurveModeForLoop = overrideData.CurveModeForLoop.Value;
        if (overrideData.PauseMode.HasValue)
            clone.PauseMode = overrideData.PauseMode.Value;
        if (overrideData.OnKillPolicy.HasValue)
            clone.OnKillPolicy = overrideData.OnKillPolicy.Value;
        if (overrideData.HideTargetOnFinish.HasValue)
            clone.HideTargetOnFinish = overrideData.HideTargetOnFinish.Value;
        if (overrideData.QueueFreeTargetOnFinish.HasValue)
            clone.QueueFreeTargetOnFinish = overrideData.QueueFreeTargetOnFinish.Value;

        // Apply step-level overrides

        for (int i = 0; i < clone.Steps.Count; i++)
        {
            if (overrideData.Steps is not null && overrideData.Steps.TryGetValue(i, out var stepOverride))
            {
                var step = clone.Steps[i];
                if (step.Duplicate() is not TweenStep stepClone)
                    throw new InvalidOperationException($"Failed to duplicate TweenStep at index {i}");

                if (stepOverride.Property != null)
                    stepClone.Property = stepOverride.Property;
                if (stepOverride.UseExplicitFrom.HasValue)
                    stepClone.UseExplicitFrom = stepOverride.UseExplicitFrom.Value;
                if (stepOverride.FromValue.HasValue)
                    stepClone.FromValue = stepOverride.FromValue.Value;
                if (stepOverride.ToValue.HasValue)
                    stepClone.ToValue = stepOverride.ToValue.Value;
                if (stepOverride.SetValueRelative.HasValue)
                    stepClone.SetValueRelative = stepOverride.SetValueRelative.Value;
                if (stepOverride.Duration.HasValue)
                    stepClone.Duration = stepOverride.Duration.Value;
                if (stepOverride.Delay.HasValue)
                    stepClone.Delay = stepOverride.Delay.Value;
                if (stepOverride.CustomCurve != null)
                    stepClone.CustomCurve = stepOverride.CustomCurve;
                if (stepOverride.BakeCurve.HasValue)
                    stepClone.BakeCurve = stepOverride.BakeCurve.Value;
                if (stepOverride.Transition.HasValue)
                    stepClone.Transition = stepOverride.Transition.Value;
                if (stepOverride.Ease.HasValue)
                    stepClone.Ease = stepOverride.Ease.Value;
                if (stepOverride.TreeOrderAction.HasValue)
                    stepClone.TreeOrderAction = stepOverride.TreeOrderAction.Value;
                if (stepOverride.ToTreeIndex.HasValue)
                    stepClone.ToTreeIndex = stepOverride.ToTreeIndex.Value;
                if (stepOverride.RunParallelWithPrevious.HasValue)
                    stepClone.RunParallelWithPrevious = stepOverride.RunParallelWithPrevious.Value;

                clone.Steps[i] = stepClone;
            }
            else
                clone.Steps[i] = Steps[i].Duplicate() as TweenStep
                                 ?? throw new InvalidOperationException("Failed to duplicate TweenStep");
        }

        return clone;
    }
}