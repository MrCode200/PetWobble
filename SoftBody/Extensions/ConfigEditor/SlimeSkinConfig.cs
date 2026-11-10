using Godot;
using SoftBodyNode = PetWobble.SoftBody.SoftBody; // alias: namespace and class share the name "SoftBody"

namespace PetWobble.Config;

/// <summary>
/// Config module for the slime sprite. Add it as a node, assign SB, and add it to ConfigScreen.Targets.
/// It has no [Export] values of its own, so its tab only contains the custom controls below.
/// The imported image is copied to user://skin.png so it survives restarts.
/// </summary>
public partial class SlimeSkinConfig : Node, IConfigurable, IConfigExtras
{
	[Export] public SoftBodyNode SB { get; set; }

	private const string SkinPath = "user://skin.png";

	private Texture2D _default;
	private FileDialog _dialog;
	private TextureRect _preview;

	public string ConfigTitle => "Skin";
	public void OnConfigChanged(string property) { }

	public override void _Ready()
	{
		_default = SB.Texture;
		Callable.From(LoadSaved).CallDeferred();
	}

	private void LoadSaved()
	{
		if (!FileAccess.FileExists(SkinPath)) return;
		var img = Image.LoadFromFile(SkinPath);
		if (img == null || img.IsEmpty()) return;
		SB.SetSkin(ImageTexture.CreateFromImage(img));
	}

	public void BuildConfigUI(Control parent)
	{
		parent.AddChild(new HSeparator());
		parent.AddChild(new Label { Text = "Slime sprite (square images work best)" });

		_preview = new TextureRect
		{
			Texture = SB.Texture,
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(160, 160),
		};
		parent.AddChild(_preview);

		var import = new Button { Text = "Import image…" };
		import.Pressed += () => EnsureDialog().PopupCenteredRatio(0.6f);
		parent.AddChild(import);

		var reset = new Button { Text = "Restore default sprite" };
		reset.Pressed += RestoreDefault;
		parent.AddChild(reset);
	}

	private FileDialog EnsureDialog()
	{
		if (_dialog != null) return _dialog;

		_dialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = ["*.png, *.jpg, *.jpeg, *.webp ; Images"],
			UseNativeDialog = true, // OS file picker (Godot 4.4+, remove this line on older versions)
		};
		_dialog.FileSelected += OnFileSelected;
		AddChild(_dialog);
		return _dialog;
	}

	private void OnFileSelected(string path)
	{
		var img = Image.LoadFromFile(path);
		if (img == null || img.IsEmpty())
		{
			GD.PushWarning($"Could not load image: {path}");
			return;
		}
		img.SavePng(SkinPath);
		SB.SetSkin(ImageTexture.CreateFromImage(img));
		RefreshPreview();
	}

	private void RestoreDefault()
	{
		if (FileAccess.FileExists(SkinPath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(SkinPath));
		SB.SetSkin(_default);
		RefreshPreview();
	}

	private void RefreshPreview()
	{
		if (_preview != null && IsInstanceValid(_preview))
			_preview.Texture = SB.Texture;
	}
}