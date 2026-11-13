using Godot;
using PetWobble.Const.Common;

namespace PetWobble.Config;

public partial class ConfigScreenOpener : Node
{
    [Export] public MouseInteractionHandler MIH;
    [Export] public ConfigScreen ConfigScreen;

    private const string SeenPath = "user://intro_seen.cfg";

    public override void _Ready()
    {
        if (!HasSeenIntro())
            GetTree().CreateTimer(1.0).Timeout += ShowIntro;

        MIH.RightClicked += OnRightClick;
    }

    private static bool HasSeenIntro()
    {
        var cfg = new ConfigFile();
        return cfg.Load(SeenPath) == Error.Ok
               && cfg.GetValue("intro", "seen", false).AsBool();
    }

    private static void ShowIntro()
    {
        // Save first: OS.Alert blocks until the player closes it
        var cfg = new ConfigFile();
        cfg.SetValue("intro", "seen", true);
        cfg.Save(SeenPath);

        OS.Alert("You wouldn't dare to open me through right clicking... \n" +
                 "or even worse smash me against a WALL (￣ε(#￣)☆╰╮o(￣皿￣///)... \n" +
                 "Would you  ʅ ( `․ ⤙ ․`) ʃ???");
    }

    private void OnRightClick(InputEvent @event, Vector2 pressPos)
    {
        ConfigScreen.Open();
    }
}