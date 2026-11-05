using Godot;

namespace PetWobble.SoftBody.Extensions;
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
        
        Vector2 windowSize = DisplayServer.WindowGetSize();
        Vector2 windowCenter = windowSize * 0.5f;

        // Current canvas/world position of the soft body's center
        Vector2 bodyCenter = SB.ToGlobal(SB.CenterPoint);
        
        var target = bodyCenter - windowCenter;
        
        DisplayServer.WindowSetPosition((Vector2I)target);
        
        GetViewport().CanvasTransform = new Transform2D(0f, -target);
        
        // Vector2 bodyCenter = SB.ToGlobal(SB.CenterPoint);
        //
        // var target = bodyCenter - windowCenter;
        //
        // DisplayServer.WindowSetPosition((Vector2I)(target - SB.Position));
        // SB.Position -= target;
    }
}