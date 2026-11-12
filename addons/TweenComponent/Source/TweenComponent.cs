#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using TweenComp.Resources.Blueprint;
using Godot;
using Godot.Collections;
using TweenComp.Resources.Overrides;

namespace TweenComp.Source;
// CHECK: should Tween return Result Type upon failure why ... upon success the tween
// ANSWER: no need,as long as i dont need it , don't implement
[GlobalClass]
[Icon("res://addons/TweenComponent/Assets/tween-component-icon.png")]
[Tool]
public partial class TweenComponent : Node
{
	[Export] public required CanvasItem Target = null!;
	[Export] public required Godot.Collections.Dictionary<StringName, TweenBlueprint> TweenBlueprints = [];
	
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

	public Tween? ActiveTween;
	public TweenBlueprint? ActiveTweenBlueprint;

	// Cleanup
	private readonly System.Collections.Generic.Dictionary<TweenBlueprint, List<Action>> _unsubscribersForTrigger = new();

	// Runtime/Building
	private readonly System.Collections.Generic.Dictionary<TweenBlueprint, System.Collections.Generic.Dictionary<int, Node>> _targetOverrides = new();
	private readonly Queue<TweenBlueprint> _tweenBpQueue = new();
	private readonly object _lock = new();

	// Starting values for JumpToStartingValues policy
	private Variant[]? _startingValues;
	
	// Signals
	[Signal] public delegate void StartedPlayingTweenEventHandler(TweenBlueprint blueprint);
	[Signal] public delegate void FinishedPlayingTweenEventHandler(TweenBlueprint blueprint);
	

	public override void _ValidateProperty(Dictionary property)
	{
		var propertyName = property["name"].AsString();

		var disable = false;
		if (propertyName != nameof(Disable))
		{
			disable = propertyName switch
			{
				_ => Disable
			};
		}

		if (disable)
			property["usage"] = (int)(property["usage"].AsInt32() | (long)PropertyUsageFlags.ReadOnly);
	}

	// ----------------------------------------------- SETUP METHODS --------------------------------------------------

	#region Setup Methods

	public override void _Ready()
	{
		if (Engine.IsEditorHint() || Disable)
			return;

		foreach (var (id, bp) in TweenBlueprints)
		{
			RegisterTween(id, bp);
		}
	}

	public override void _ExitTree()
	{
		if (Engine.IsEditorHint() || Disable)
			return;

		_tweenBpQueue.Clear();
		
		foreach (var kvp in _unsubscribersForTrigger)
		{
			foreach (var unsub in kvp.Value)
				unsub();
		}
		_unsubscribersForTrigger.Clear();
	}

	/// <summary>
	/// Resolves target node overrides for each step in a tween blueprint.
	/// </summary>
	/// <param name="bp">The tween blueprint to resolve targets for.</param>
	/// <remarks>
	/// For each step with a TargetOverride, attempts to find the node at the specified path
	/// and stores it in the _targetOverrides dictionary for use during tween playback.
	/// </remarks>
	private void ResolveStepTargets(TweenBlueprint bp)
	{
		_targetOverrides[bp] = new System.Collections.Generic.Dictionary<int, Node>();
		
		for (int i = 0; i < bp.Steps.Count; i++)
		{
			var step = bp.Steps[i];
			if (string.IsNullOrEmpty(step.TargetOverride)) continue;
			
			var target = GetNodeOrNull<Node>(step.TargetOverride);
			if (target == null)
			{
				GD.Print("Target not found, NodePath: {TargetOverride} for {Property}, skipping override",
					step.TargetOverride, step.Property);
				continue;
			}
				
			_targetOverrides[bp][i] = target;
		}
	}
	
	/// <summary>
	/// Bakes custom curves for steps that have BakeCurve enabled.
	/// </summary>
	/// <param name="bp">The tween blueprint containing steps with curves to bake.</param>
	/// <remarks>
	/// Baking a curve pre-calculates its values for improved performance during tween playback.
	/// </remarks>
	private void BakeCurves(TweenBlueprint bp)
	{
		foreach (var step in bp.Steps)
		{
			if (step.CustomCurve != null && step.BakeCurve)
			{
				step.CustomCurve.Bake();
			}
		}
	}

	/// <summary>
	/// Subscribes to trigger events defined in the tween blueprint.
	/// </summary>
	/// <param name="bp">The tween blueprint to subscribe triggers for.</param>
	/// <remarks>
	/// Sets up event handlers for various triggers (Ready, Hide, Show, MouseEnter, MouseExit, CustomSignal)
	/// and stores unsubscribe functions for cleanup when the blueprint is unregistered.
	/// </remarks>
	private void SubToTriggerEvents(TweenBlueprint bp)
	{
		if (!_unsubscribersForTrigger.ContainsKey(bp))
			_unsubscribersForTrigger[bp] = [];

		void PlayTweenWrapper() => PlayTween(bp);

		// Ready
		if (bp.Triggers.Contains(TweenTrigger.Ready))
		{
			if (!Target.IsAncestorOf(this))
				GD.Print("Target is not a child of this node, while Trigger contains Ready");

			Target.Ready += PlayTweenWrapper;
			_unsubscribersForTrigger[bp].Add(() => Target.Ready -= PlayTweenWrapper);
		}

		// Hide
		if (bp.Triggers.Contains(TweenTrigger.Hide))
		{
			Target.Hidden += PlayTweenWrapper;
			_unsubscribersForTrigger[bp].Add(() => Target.Hidden -= PlayTweenWrapper);
		}

		// Show
		if (bp.Triggers.Contains(TweenTrigger.Show))
		{
			void RunOnShowWrapper() => RunOnShow(bp);
			Target.VisibilityChanged += RunOnShowWrapper;
			_unsubscribersForTrigger[bp].Add(() => Target.VisibilityChanged -= RunOnShowWrapper);
		}

		// MouseEnter
		if (bp.Triggers.Contains(TweenTrigger.MouseEnter))
		{
			switch (Target)
			{
				case CollisionObject2D co:
					co.MouseEntered += PlayTweenWrapper;
					_unsubscribersForTrigger[bp].Add(() => co.MouseEntered -= PlayTweenWrapper);
					break;
				case Control c:
					c.MouseEntered += PlayTweenWrapper;
					_unsubscribersForTrigger[bp].Add(() => c.MouseEntered -= PlayTweenWrapper);
					break;
				default:
					GD.Print("Target type {Type} does not support MouseEnter", Target.GetType());
					break;
			}
		}

		// MouseExit
		if (bp.Triggers.Contains(TweenTrigger.MouseExit))
		{
			switch (Target)
			{
				case CollisionObject2D co:
					co.MouseExited += PlayTweenWrapper;
					_unsubscribersForTrigger[bp].Add(() => co.MouseExited -= PlayTweenWrapper);
					break;
				case Control c:
					c.MouseExited += PlayTweenWrapper;
					_unsubscribersForTrigger[bp].Add(() => c.MouseExited -= PlayTweenWrapper);
					break;
				default:
					GD.Print("Target type {Type} does not support MouseExit", Target.GetType());
					break;
			}
		}

		// Custom Signals
		if (bp.Triggers.Contains(TweenTrigger.CustomSignal))
		{
			foreach (var signalData in bp.CustomSignals)
			{
				var emitter = signalData.EmitterPath == null
					? Target
					: GetNodeOrNull<Node>(signalData.EmitterPath);

				if (emitter == null)
				{
					GD.Print("CustomSignalEmitter not found NodePath: {EmitterPath}", signalData.EmitterPath);
					continue;
				}

				if (!emitter.HasSignal(signalData.CustomSignalName))
				{
					GD.Print("CustomSignalEmitter doesn't contain {SignalName}", signalData.CustomSignalName);
					continue;
				}

				signalData.Emitter = emitter;
				var callable = Callable.From(PlayTweenWrapper);
				emitter.Connect(signalData.CustomSignalName, callable);
				_unsubscribersForTrigger[bp].Add(() => emitter.Disconnect(signalData.CustomSignalName, callable));
			}
		}
	}

	private void RunOnShow(TweenBlueprint bp)
	{
		if (Target.IsVisible())
			PlayTween(bp);
	}

	#endregion

	// ---------------------------------------------------- API -------------------------------------------------------
	#region API
	
	// ---------- Tween Registration ----------
	/// <summary>
	/// Registers a tween blueprint with the given identifier.
	/// </summary>
	/// <param name="identifier">The unique identifier for the tween blueprint.</param>
	/// <param name="bp">The tween blueprint to register.</param>
	/// <returns>True if registration succeeded, false if an identifier with the same name already exists.</returns>
	/// <remarks>
	/// Resolves step targets, bakes curves, and subscribes to trigger events for the blueprint.
	/// </remarks>
	public bool RegisterTween(string identifier, TweenBlueprint bp)
	{
		if (_targetOverrides.ContainsKey(bp))
			return false;

		TweenBlueprints[identifier] = bp;
		ResolveStepTargets(bp);
		BakeCurves(bp);
		SubToTriggerEvents(bp);
		return true;
	}
	
	/// <summary>
	/// Unregisters a tween blueprint by its identifier.
	/// </summary>
	/// <param name="identifier">The identifier of the tween blueprint to unregister.</param>
	/// <returns>True if unregistration succeeded, false if the identifier was not found.</returns>
	/// <remarks>
	/// Removes the blueprint, cleans up target overrides, and unsubscribes from all trigger events.
	/// </remarks>
	public bool UnregisterTween(string identifier)
	{
		if (!TweenBlueprints.TryGetValue(identifier, out var bp))
			return false;	
		
		_targetOverrides.Remove(bp);
		TweenBlueprints.Remove(identifier); 
		
		if (!_unsubscribersForTrigger.TryGetValue(bp, out var unsubscribers))
			return true;// as may only contain manual as trigger

		foreach (var unsub in unsubscribers)
		{
			try
			{
				unsub();
			}
			catch (Exception e)
			{
				GD.Print(e, "{ThisNodeName}: Failed to unsubscribe from trigger for blueprint {Blueprint}", 
					GetName(), bp.ResourceName);
			}
		}

		_unsubscribersForTrigger.Remove(bp);
		
		return true;
	}
	
	// ---------- Tween Playback ----------
	/// <summary>
	/// Plays a tween by its blueprint identifier.
	/// </summary>
	/// <param name="blueprintIdentifier">The identifier of the tween blueprint to play.</param>
	/// <param name="overrideData">Optional playback override data to modify tween behavior.</param>
	/// <returns>The created Tween instance, or null if the blueprint was not found or playback was rejected.</returns>
	/// <remarks>
	/// This is a convenience overload that looks up the blueprint by identifier before playing.
	/// </remarks>
	public Tween? PlayTween(StringName blueprintIdentifier, TweenPlaybackOverride? overrideData = null)
	{
		if (!TweenBlueprints.TryGetValue(blueprintIdentifier, out var bp))
			return null;
		
		return PlayTween(bp, overrideData);
	}

	/// <summary>
	/// Plays a tween from the given blueprint.
	/// </summary>
	/// <param name="bp">The tween blueprint to play.</param>
	/// <param name="overrideData">Optional playback override data to modify tween behavior.</param>
	/// <returns>The created Tween instance, or null if playback was rejected or disabled.</returns>
	/// <remarks>
	/// Handles conflict resolution based on WhenRequestedPolicy, SelfConflictPolicy, and OtherConflictPolicy.
	/// May queue the tween if policies allow, or reject the request based on configuration.
	/// </remarks>
	public Tween? PlayTween(TweenBlueprint bp, TweenPlaybackOverride? overrideData = null)
	{
		if (Disable || bp.Disable)
			return null;
		if (!_targetOverrides.TryGetValue(bp, out _))
		{
			GD.Print("{ThisNodeName}: Target override not found for blueprint '{BlueprintName}' : {Blueprint}, " +
			         "blueprint is probably not registered", 
			GetName(), TweenBlueprints.FirstOrDefault(x => x.Value == bp).Key, bp);
			return null;
		}

		// Apply overrides if provided
		var blueprintToPlay = overrideData.HasValue ? bp.CloneWithOverride(overrideData.Value) : bp;

		if (ActiveTweenBlueprint is not null && blueprintToPlay.WhenRequestedPolicy != TweenWhenRequestedPolicy.ForcePlay)
		{
			if (blueprintToPlay.WhenRequestedPolicy == TweenWhenRequestedPolicy.DropRequest)
				return null;

			var activePolicy = ActiveTweenBlueprint == blueprintToPlay
				? ActiveTweenBlueprint.SelfConflictPolicy
				: ActiveTweenBlueprint.OtherConflictPolicy;

			if (activePolicy == TweenConflictPolicy.AllowRequest)
			{
				// continue
			}
			else if (
				blueprintToPlay.WhenRequestedPolicy == TweenWhenRequestedPolicy.ForceQueue ||
				(blueprintToPlay.WhenRequestedPolicy == TweenWhenRequestedPolicy.QueueIfAllowed
				 && activePolicy == TweenConflictPolicy.AllowQueue)
				)
			{
				if (ActiveTweenBlueprint.MaxQueueCount != -1
					&& _tweenBpQueue.Count >= ActiveTweenBlueprint.MaxQueueCount)
				{
					if (ActiveTweenBlueprint.QueueOverflowPolicy == QueueOverflowPolicy.DropOldest
					    || blueprintToPlay.WhenRequestedPolicy == TweenWhenRequestedPolicy.ForceQueue)
						_tweenBpQueue.Dequeue();
					else if (ActiveTweenBlueprint.QueueOverflowPolicy == QueueOverflowPolicy.RejectNewest)
						return null;
				}
				_tweenBpQueue.Enqueue(blueprintToPlay);
				return null;
			}
			else // if (activePolicy == TweenConflictPolicy.RejectRequest)
			{
				return null;
			}
		}

		lock (_lock)
		{
			try
			{
				BuildTween(blueprintToPlay);
				EmitSignal(SignalName.StartedPlayingTween);
				return ActiveTween;
			}
			catch (Exception e)
			{
				GD.Print(e, "{ThisNodeName}: Failed to build tween for blueprint '{BlueprintName}' : {Blueprint}", 
					GetName(), TweenBlueprints.FirstOrDefault(x => x.Value == bp).Key, bp);
				return null;
			}
		}
	}
	
	// ---------- Tween Queue ----------
	/// <summary>
	/// Removes all blueprints from the internal queue without playing them.
	/// </summary>
	public void ClearTweenQueue() => _tweenBpQueue.Clear();

	#endregion

	// ---------------------------------------------- BUILDER METHODS -------------------------------------------------
	#region Builder Methods	
	
	/// <summary>
	/// Builds and configures a Tween instance from the given blueprint.
	/// </summary>
	/// <param name="bp">The tween blueprint to build from.</param>
	/// <returns>The configured Tween instance.</returns>
	/// <remarks>
	/// Clears any active tween, creates a new Tween, sets pause mode, builds forward tweeners,
	/// optionally builds backward tweeners for ping-pong loops, and adds post-finish subscriptions.
	/// </remarks>
	private Tween BuildTween(TweenBlueprint bp)
	{
		TryClearActiveTween();
		ActiveTweenBlueprint = bp;
		ActiveTween = CreateTween();
		
		/* Removed due to unnecessary async?
		// Starting Delay
		if (data.DelayAnimStart > 0)
			await ToSignal(
				GetTree().CreateTimer(data.DelayAnimStart),
				SceneTreeTimer.SignalName.Timeout
			);
		*/
		
		// Pause Mode
		ActiveTween.SetPauseMode(bp.PauseMode);
		
		// Building Tweeners
		var (fromValues, toValues) = BuildForwardTween(bp);
		
		// Save starting values and targets for JumpToStartingValues policy
		if (bp.OnKillPolicy == OnKillPolicy.JumpToStartingValues)
			_startingValues = fromValues;
		
		switch (bp.LoopMode)
		{
			case TweenLoopMode.PingPong:
				BuildBackwardTween(bp, fromValues, toValues);
				ActiveTween.SetLoops(bp.MaxLoopCount);
				break;
			case TweenLoopMode.Loop:
				ActiveTween.SetLoops(bp.MaxLoopCount);
				break;
		}
		
		AddPostFinishSubs(bp);
		
		return ActiveTween;
	}

	/// <summary>
	/// Attempts to clear the active tween based on the OnKillPolicy.
	/// </summary>
	/// <remarks>
	/// Stops the active tween and applies the appropriate policy:
	/// <list type="bullet">
	///     <item>
	///         <term>Ignore</term>
	///         <description>Does nothing</description>
	///     </item>
	///     <item>
	///         <term>JumpToFinalValues</term>
	///         <description>Jumps to the end of the tween</description>
	///     </item>
	///     <item>
	///         <term>JumpToStartingValues</term>
	///         <description>Restores the original starting values</description>
	///     </item>
	/// </list>
	/// </remarks>
	private void TryClearActiveTween()
	{
		if (ActiveTween == null || !ActiveTween.IsRunning())
		{
			EmitSignal(SignalName.FinishedPlayingTween);
			return;
		}		
		ActiveTween.Stop();
		switch (ActiveTweenBlueprint!.OnKillPolicy)
		{
			case OnKillPolicy.Ignore:
				break;
			case OnKillPolicy.JumpToFinalValues: // CHECK: do the finished signals get called before active tween is killed?
				if (ActiveTween.CustomStep(ActiveTweenBlueprint.GetDuration() + 1))
					GD.Print("Failed to restart jump to final value, tweeners still remain");
				break;
			case OnKillPolicy.JumpToStartingValues:
				RestoreStartingValues();
				break;
		}
		
		EmitSignal(SignalName.FinishedPlayingTween);
		ActiveTween?.Kill();
		ActiveTween = null; 
		ActiveTweenBlueprint = null;
	}
	
	/// <summary>
	/// Builds the forward tweeners for all steps in the blueprint.
	/// </summary>
	/// <param name="bp">The tween blueprint to build from.</param>
	/// <returns>A tuple containing arrays of from-values and to-values for each step.</returns>
	/// <remarks>
	/// Iterates through each step, creating tweeners for property animations, tree order actions,
	/// and applying transitions, easing, custom curves, delays, and relative value settings.
	/// </remarks>
	private (Variant[] fromValues, Variant[] toValues) BuildForwardTween(TweenBlueprint bp)
	{
		var count = bp.Steps.Count;
		var fromValues = new Variant[count];
		var toValues = new Variant[count];
		var resolvedTargets = _targetOverrides[bp];
		
		for (var i = 0; i < count; i++)
		{
			var step = bp.Steps[i];
			if (step.Disable)
				continue;
			
			var stepTarget = resolvedTargets.GetValueOrDefault(i, Target);
			
			// Parallel
			ActiveTween!.SetParallel(step.RunParallelWithPrevious && i > 0);
			
			// TreeOrderAction
			if (step.TreeOrderAction != TreeOrderAction.None)
			{
				step.FromTreeIndex = stepTarget.GetIndex();
				ActiveTween.TweenCallback(Callable.From(() =>
				{
					switch (step.TreeOrderAction)
					{
						case TreeOrderAction.MoveToFront:
							stepTarget.GetParent().MoveChild(stepTarget, -1);
							break;

						case TreeOrderAction.MoveToBack:
							stepTarget.GetParent().MoveChild(stepTarget, 0);
							break;

						case TreeOrderAction.MoveToIndex:
							stepTarget.GetParent().MoveChild(stepTarget, step.ToTreeIndex);
							break;
					}
				}));
			}	
			
			// Following Configurations need Property
			if (step.Property.IsEmpty)
				continue;
			
			// Property values
			fromValues[i] = step.UseExplicitFrom
				? step.FromValue
				: stepTarget.Get(new StringName(step.Property));
			toValues[i] = step.ToValue;

			var forward = ActiveTween
				.TweenProperty(stepTarget, step.Property, step.ToValue, step.Duration);

			if (step.CustomCurve is null)
				forward.SetTrans(step.Transition)
					.SetEase(step.Ease);
			else
				forward.SetCustomInterpolator(Callable.From((float v) => 
					step.BakeCurve ? 
						step.CustomCurve!.SampleBaked(v) :
						step.CustomCurve!.Sample(v)));

			if (step.UseExplicitFrom)
				forward.From(step.FromValue);
			if (step.Delay > 0)
				forward.SetDelay(step.Delay);
			if (step.SetValueRelative)
				forward.AsRelative();
		}
		
		return (fromValues, toValues);
	}

	/// <summary>
	/// Builds backward tweeners for ping-pong loop mode.
	/// </summary>
	/// <param name="bp">The tween blueprint to build from.</param>
	/// <param name="fromValues">Array of starting values from the forward tween.</param>
	/// <param name="toValues">Array of target values from the forward tween.</param>
	/// <remarks>
	/// Iterates through steps in reverse order, creating tweeners that animate back to the starting values.
	/// Handles curve mirroring based on CurveModeForLoop configuration.
	/// </remarks>
	private void BuildBackwardTween(TweenBlueprint bp, Variant[] fromValues, Variant[] toValues)
	{
		var count = bp.Steps.Count;
		for (var i = count - 1; i >= 0; i--)
		{
			var step = bp.Steps[i];
			if (step.Disable)
				continue;
			
			var stepTarget = _targetOverrides[bp].GetValueOrDefault(i, Target);
			var prevStep = i < count - 1 ? bp.Steps[i + 1] : null;
			
			// Parallel
			ActiveTween!.SetParallel(prevStep?.RunParallelWithPrevious ?? false);
			
			// TreeOrderAction
			if (step.TreeOrderAction != TreeOrderAction.None)
			{
				ActiveTween!.TweenCallback(Callable.From(() =>
				{
					stepTarget.GetParent().MoveChild(stepTarget, step.FromTreeIndex);
				}));
			}
			
			// Following Configurations need Property
			if (step.Property.IsEmpty)
				continue;
			
			var newToValue = step.SetValueRelative ? Negate(toValues[i]) : fromValues[i];
			var backward = ActiveTween.TweenProperty(stepTarget, step.Property, newToValue, step.Duration);
			
			if (step.CustomCurve is null)
				backward.SetTrans(step.Transition)
					.SetEase(step.Ease);
			else
				backward.SetCustomInterpolator(Callable.From((float v) =>
				{
					if (bp.CurveModeForLoop == CurveModeForLoop.Mirror)
						v = 1 - v;
						
					var sample = step.BakeCurve 
						? step.CustomCurve!.SampleBaked(v) 
						: step.CustomCurve!.Sample(v);
						
					return bp.CurveModeForLoop == CurveModeForLoop.Mirror 
						? 1 - sample 
						: sample;
				}));

			if (step.SetValueRelative)
				backward.AsRelative();
			else
				backward.From(toValues[i]);

			if (prevStep?.Delay > 0)
				backward.SetDelay(prevStep.Delay);
		}
	}
	
	/// <summary>
	/// Negates a Variant value for use in relative ping-pong animations.
	/// </summary>
	/// <param name="value">The Variant value to negate.</param>
	/// <returns>The negated Variant value.</returns>
	/// <remarks>
	/// Supports numeric types (Int, Float), Bool, Vector2, Vector2I, Vector3, Vector3I, and Color.
	/// Falls back to UnsupportedNegateValue for unsupported types.
	/// </remarks>
	private Variant Negate(Variant value)
	{
		return value.VariantType switch
		{
			Variant.Type.Int => Variant.From(-value.AsInt64()),
			Variant.Type.Float => Variant.From(-value.AsDouble()),
			Variant.Type.Bool => Variant.From(!value.AsBool()),
			Variant.Type.Vector2 => Variant.From(-value.AsVector2()),
			Variant.Type.Vector2I => Variant.From(-value.AsVector2I()),
			Variant.Type.Vector3 => Variant.From(-value.AsVector3()),
			Variant.Type.Vector3I => Variant.From(-value.AsVector3I()),
			Variant.Type.Color => Variant.From(-value.AsColor()),
			_ => UnsupportedNegateValue(value)
		};
	}
	
	private Variant UnsupportedNegateValue(Variant value)
	{
		GD.Print("{ThisNodeName}: PingPong build " +
		         "Negate is not supported for Variant type {VariantType}, using original value", 
			GetName(), value.VariantType);
		return value;
	}
	
	/// <summary>
	/// Adds post-finish subscriptions to the tween.
	/// </summary>
	/// <param name="bp">The tween blueprint containing finish actions.</param>
	/// <remarks>
	/// Sets up a handler for the tween's Finished event that:
	/// <list type="bullet">
	///     <item>
	///         <term>HideTargetOnFinish: </term>
	///         <description>Hides the target if enabled</description>
	///     </item>
	///     <item>
	///         <term>QueueFreeTargetOnFinish: </term>
	///         <description>QueueFrees the target and this component if enabled</description>
	///     </item>
	///     <item>
	///         <term>ClearActiveTween:	</term>
	///         <description>Clears the active tween</description>
	///     </item>
	///     <item>
	///         <term>QueueProcessing: </term>
	///         <description>Plays the next queued tween if any</description>
	///     </item>
	/// </list>
	/// </remarks>
	private void AddPostFinishSubs(TweenBlueprint bp)
	{
		ActiveTween!.Finished += () =>
		{
			if (bp.HideTargetOnFinish)
				Target.SetVisible(false);
			if (bp.QueueFreeTargetOnFinish)
			{
				Target.QueueFree();
				TryClearActiveTween();
				QueueFree();
			}
			TryClearActiveTween();
			if (_tweenBpQueue.TryDequeue(out var queuedBp))
				PlayTween(queuedBp);
		};
	}	 
	
	/// <summary>
	/// Restores the target properties to their starting values.
	/// </summary>
	/// <remarks>
	/// Used when OnKillPolicy is JumpToStartingValues. Iterates through all steps
	/// and restores each property to its original value stored in _startingValues.
	/// </remarks>
	private void RestoreStartingValues()
	{
		var resolvedTargets = _targetOverrides[ActiveTweenBlueprint!];

		for (var i = 0; i < _startingValues!.Length; i++)
		{
			var step = ActiveTweenBlueprint!.Steps[i];
			if (step.Disable || step.Property.IsEmpty)
				continue;

			var target = resolvedTargets.GetValueOrDefault(i, Target);
			target.Set(new StringName(step.Property), _startingValues[i]);
		}

		_startingValues = null;
	}

	#endregion
}
