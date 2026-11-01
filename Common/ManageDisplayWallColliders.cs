using System.Net.Mime;
using Godot;

namespace PetWobble.Const.Common;

public partial class ManageDisplayWallColliders : Node2D
{
    [Export] public float WallThickness = 20f;
    [Export] public StaticBody2D Up;
    [Export] public StaticBody2D Down;
    [Export] public StaticBody2D Left;
    [Export] public StaticBody2D Right;
    
    private Vector2 _screenSize;
    
    public override void _Ready()
    {
        _screenSize = DisplayServer.ScreenGetSize();
        Down.Position = new Vector2(_screenSize.X / 2, 0);
        Down.GetChild<CollisionShape2D>(0).Shape = 
            new RectangleShape2D { Size = new Vector2(_screenSize.X, WallThickness) };
        
        Up.Position = new Vector2(_screenSize.X / 2, _screenSize.Y);
        Up.GetChild<CollisionShape2D>(0).Shape =    
            new RectangleShape2D { Size = new Vector2(_screenSize.X, WallThickness) };
        
        Left.Position = new Vector2(0, _screenSize.Y / 2);
        Left.GetChild<CollisionShape2D>(0).Shape =
            new RectangleShape2D { Size = new Vector2(WallThickness, _screenSize.Y) };
        
        Right.Position = new Vector2(_screenSize.X, _screenSize.Y / 2);
        Right.GetChild<CollisionShape2D>(0).Shape =
            new RectangleShape2D { Size = new Vector2(WallThickness, _screenSize.Y) };
    }
}