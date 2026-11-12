#nullable enable
using Godot;
using Godot.Collections;

namespace TweenComp.Resources.Blueprint;

public enum TreeOrderAction
{
    None,
    MoveToFront,
    MoveToBack,
    MoveToIndex
}

[GlobalClass]
[Tool]
public partial class TweenStep : Resource
{
    [ExportGroup("Property")]
    private NodePath _property = new();
    [Export]
    public NodePath Property
    {
        get => _property;
        set
        {
            _property = value;
            NotifyPropertyListChanged();
        }
    }
    private bool _useExplicitFrom;
    [Export] public bool UseExplicitFrom
    {
        get => _useExplicitFrom;
        set
        {
            _useExplicitFrom = value;
            NotifyPropertyListChanged();
        }
    }
    
    private Variant _fromValue;
    [Export]
    public Variant FromValue
    {
        get => _fromValue;
        set
        {
            _fromValue = value;
            NotifyPropertyListChanged();
        }
    }
    [Export] public Variant ToValue;
    private bool _setValueRelative;
    [Export]
    public bool SetValueRelative
    {
        get => _setValueRelative;
        set
        {
            _setValueRelative = value;
            NotifyPropertyListChanged();
        }
    }
    
    [ExportGroup("Timing")]
    [Export(PropertyHint.Range, "0.001,10,,or_greater")] public float Duration = 0.3f;
    [Export(PropertyHint.Range, "0,10,,or_greater")] public float Delay;

    private Curve? _customCurve;

    [Export]
    public Curve? CustomCurve
    {
        get => _customCurve;
        set
        {
            _customCurve = value;
            NotifyPropertyListChanged();
        }
    }

    [Export] public bool BakeCurve = true;
    [Export] public Tween.TransitionType Transition = Tween.TransitionType.Linear;
    [Export] public Tween.EaseType Ease = Tween.EaseType.InOut;
    
    [ExportGroup("Other")]

    private TreeOrderAction _treeOrderAction = TreeOrderAction.None;

    [Export]
    public TreeOrderAction TreeOrderAction
    {
        get => _treeOrderAction;
        set
        {
            _treeOrderAction = value;
            NotifyPropertyListChanged();
        }
    }
    public int FromTreeIndex;
    [Export] public int ToTreeIndex;
    
    private bool _runParallelWithPrevious;
    [Export] public bool RunParallelWithPrevious
    {
        get => _runParallelWithPrevious;
        set
        {
            _runParallelWithPrevious = value;
            NotifyPropertyListChanged();
        }
    }
    [Export] public NodePath TargetOverride;

    [ExportGroup("")] 
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

        bool show = propertyName switch
        {
            nameof(FromValue) => UseExplicitFrom,
            nameof(Delay) => !RunParallelWithPrevious,
            nameof(BakeCurve) => CustomCurve is not null,
            nameof(ToTreeIndex) => TreeOrderAction == TreeOrderAction.MoveToIndex,
            _ => true
        };
        
        if (!show)
        {
            property["usage"] = (int)PropertyUsageFlags.NoEditor;
            return;
        }

        var disable = false;
        if (!Property.IsEmpty && propertyName != nameof(Disable))
        {
            disable = propertyName switch
            {
                nameof(Transition) or nameof(Ease) => CustomCurve is not null,
                nameof(UseExplicitFrom) or nameof(SetValueRelative) => !(UseExplicitFrom && SetValueRelative),
                
                _ => Disable
            };
        }
        
        if (disable)
            property["usage"] = (int)(property["usage"].AsInt32() | (long)PropertyUsageFlags.ReadOnly);
    }
}