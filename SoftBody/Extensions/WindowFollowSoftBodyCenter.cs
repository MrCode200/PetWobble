using Godot;

namespace PetWobble.Const;

public partial class WindowFollowSoftBodyCenter : Node2D
{ 
    [Export] public SoftBody SB;
    [Export] public bool Disable = false;

    private Vector2 _windowSize;

    public override void _Ready()
    {
        _windowSize = DisplayServer.WindowGetSize();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Disable) return;
        
        var glbCenter = SB.ToGlobal(SB.CenterPoint);
        var target = glbCenter - _windowSize * 0.5f;
        
        DisplayServer.WindowSetPosition((Vector2I)target);
        
        GetViewport().CanvasTransform = new Transform2D(0f, -target);
    }
}