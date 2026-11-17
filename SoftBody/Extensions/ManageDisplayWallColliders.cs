using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mime;
using Godot;
using Godot.Collections;
using PetWobble.Config;

namespace PetWobble.Const.Common;

public partial class ManageDisplayWallColliders : Node2D, IConfigurable
{
    [Export] public SoftBody.SoftBody SoftBody;
    [Export] public float WallThickness = 20f;
    [Export] public float ScreenIsNeighbourTolerance = 4f;
    [Export] public Array<int> ScreenBlacklist = [];

    private string _wallGroupName = "DisplayWall";

    public string ConfigTitle => "Walls";

    public void OnConfigChanged(string p)
    {
        if (p is nameof(WallThickness) or
            nameof(ScreenIsNeighbourTolerance) or 
            nameof (ScreenBlacklist)) 
            RebuildWalls();
    }

    public override void _Ready()
    {
        RebuildWalls();
    }

    private enum Edge
    {
        Top,
        Bottom,
        Left,
        Right
    }

    private void RebuildWalls()
    {
        foreach (var child in GetChildren())
        {
            if (child is StaticBody2D body && body.IsInGroup(_wallGroupName))
            {
                body.QueueFree();
            }
        }

        int screenCount = DisplayServer.GetScreenCount();
        var screens = new List<Rect2>();

        for (int i = 0; i < screenCount; i++)
        {
            if (ScreenBlacklist.Contains(i))
            {
                GD.Print($"Screen ID {i} Blacklisted, skipping ...");
                continue;
            }        
            
            screens.Add(new Rect2(
                position: DisplayServer.ScreenGetPosition(i),
                size: DisplayServer.ScreenGetSize(i)
            ));
        }

        var foundScreens = screens.ToArray();
        foreach (var screen in foundScreens)
        {
            var edges = GetEdgesWithoutNeighbours(screen, foundScreens, ScreenIsNeighbourTolerance);
            GD.Print($"Screen: {screen} detected no neighbour edges {string.Join(", ", edges)}");
            BuildScreenWalls(screen, edges);
        }
        
        // Sets Wobble position to the center of one active screen
        // This makes sure that it never spawns out of bound, thus falling to its death into the infinite void and dark abyss full of the unknown
        SoftBody.Initialize();
        SoftBody.Position = screens[0].GetCenter();
    }

    private Edge[] GetEdgesWithoutNeighbours(Rect2 screen, Rect2[] screens, float tolerance)
    {
        var edgesWithNeighbours = new HashSet<Edge>();

        foreach (var other in screens)
        {
            if (other == screen)
                continue;

            if (!edgesWithNeighbours.Contains(Edge.Bottom) &&
                Mathf.Abs(other.Position.Y - screen.End.Y) <= tolerance &&
                Mathf.Abs(other.Position.X - screen.Position.X) <= tolerance)
            {
                edgesWithNeighbours.Add(Edge.Bottom);
                continue;
            }

            if (!edgesWithNeighbours.Contains(Edge.Top) &&
                Mathf.Abs(other.End.Y - screen.Position.Y) <= tolerance &&
                Mathf.Abs(other.Position.X - screen.Position.X) <= tolerance)
            {
                edgesWithNeighbours.Add(Edge.Top);
                continue;
            }

            if (!edgesWithNeighbours.Contains(Edge.Right) &&
                Mathf.Abs(other.Position.X - screen.End.X) <= tolerance &&
                Mathf.Abs(other.Position.Y - screen.Position.Y) <= tolerance)
            {
                edgesWithNeighbours.Add(Edge.Right);
                continue;
            }

            if (!edgesWithNeighbours.Contains(Edge.Left) &&
                Mathf.Abs(other.End.X - screen.Position.X) <= tolerance &&
                Mathf.Abs(other.Position.Y - screen.Position.Y) <= tolerance)
            {
                edgesWithNeighbours.Add(Edge.Left);
                continue;
            }

            if (edgesWithNeighbours.Count == 4)
                break;
        }

        var allEdges = new[]
        {
            Edge.Top, Edge.Bottom, Edge.Right, Edge.Left
        };

        return allEdges.Where(e => !edgesWithNeighbours.Contains(e)).ToArray();
    }

    private void BuildScreenWalls(Rect2 screen, Edge[] edges)
    {
        foreach (var edge in edges)
        {
            var wall = new StaticBody2D();
            wall.AddToGroup(_wallGroupName);

            var shape = new CollisionShape2D();
            shape.Shape = new RectangleShape2D
            {
                Size = new Vector2(
                    edge is Edge.Bottom or Edge.Top ? screen.Size.X : screen.Size.Y,
                    WallThickness
                )
            };
            wall.AddChild(shape);

            // Pos starts top left corner
            wall.Position = edge switch
            {
                Edge.Top => screen.Position + new Vector2(screen.Size.X / 2, 0),
                Edge.Bottom => screen.End - new Vector2(screen.Size.X / 2, 0),
                Edge.Left => screen.Position + new Vector2(0, screen.Size.Y / 2),
                Edge.Right => screen.End - new Vector2(0, screen.Size.Y / 2),
            };

            if (edge is Edge.Right or Edge.Left)
                wall.Rotate(Mathf.Pi / 2); // Rotate 90 Degrees
            
            AddChild(wall);
        }
    }
}