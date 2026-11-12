#nullable enable
using System.Collections.Generic;
using Godot;
using TweenComp.Resources.Blueprint;

namespace TweenComp.Resources.Overrides;

// CHECK: be 'readonly record struct' OR 'readonly struct' OR 'readonly record'
public readonly record struct TweenStepOverride(
    NodePath? Property,
    bool? UseExplicitFrom,
    Variant? FromValue,
    Variant? ToValue,
    bool? SetValueRelative,
    
    float? Duration,
    float? Delay,
    
    Curve? CustomCurve,
    bool? BakeCurve,
    Tween.TransitionType? Transition,
    Tween.EaseType? Ease,
    
    TreeOrderAction? TreeOrderAction,
    int? ToTreeIndex,
    
    bool? RunParallelWithPrevious
    // bool? Disable
    // NodePath? TargetOverride
);

public readonly record struct TweenPlaybackOverride(
    float? DelayAnimStart,
    
    TweenLoopMode? LoopMode,
    int? MaxLoopCount,
    CurveModeForLoop? CurveModeForLoop,
    
    Tween.TweenPauseMode? PauseMode,
    OnKillPolicy? OnKillPolicy,
    
    bool? HideTargetOnFinish,
    bool? QueueFreeTargetOnFinish,
    
    Dictionary<int, TweenStepOverride>? Steps
);