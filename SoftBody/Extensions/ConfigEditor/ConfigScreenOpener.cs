using System.Threading;
using Godot;
using PetWobble.Const.Common;
using PetWobble.SoftBody.Extensions;

namespace PetWobble.Config;

public partial class ConfigScreenOpener : Node
{
    //[Export] public ClickThroughWindow ClickThroughWindow;
    [Export] public MouseInteractionHandler MIH;
    [Export] public ConfigScreen ConfigScreen;

    public override void _Ready()
    {
        GetTree().CreateTimer(1.0).Timeout += () =>
        {
            OS.Alert("You wouldn't dare to open me through right clicking... \n" +
                     "or even worse smash me against a WALL (￣ε(#￣)☆╰╮o(￣皿￣///)... \n" +
                     "Would you  ʅ ( `․ ⤙ ․`) ʃ???");
        };
        
        MIH.RightClicked += OnRightClick;
    }

    private void OnRightClick(InputEvent @event, Vector2 pressPos)
    {   
        ConfigScreen.Open();
        //ClickThroughWindow.SetPassthrough(false);
        //ClickThroughWindow.Disable = true;
    }
}