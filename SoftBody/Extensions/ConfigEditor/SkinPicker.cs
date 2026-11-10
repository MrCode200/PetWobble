using System;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Reusable "import / restore image" control block for any node that shows a texture.
/// The owner supplies how to read and write its texture. The imported image is copied to
/// user://skin_{key}.png so it survives restarts.
/// Plain C# class: construct it in the owner's _Ready (the default texture is captured then).
/// </summary>
public sealed class SkinPicker
{
	private readonly Node _owner;
	private readonly string _savePath;
	private readonly Func<Texture2D> _get;
	private readonly Action<Texture2D> _set;
	private readonly Texture2D _default;

	private FileDialog _dialog;
	private TextureRect _preview;

	public SkinPicker(Node owner, string key, Func<Texture2D> get, Action<Texture2D> set)
	{
		_owner = owner;
		_savePath = $"user://skin_{key}.png";
		_get = get;
		_set = set;
		_default = get();
	}

	public Texture2D DefaultTexture => _default;

	/// <summary>Call deferred after _Ready to re-apply a previously imported image.</summary>
	public void LoadSaved()
	{
		if (!FileAccess.FileExists(_savePath)) return;
		var img = Image.LoadFromFile(_savePath);
		if (img == null || img.IsEmpty()) return;
		_set(ImageTexture.CreateFromImage(img));
	}

	public void BuildUi(Control parent, string title)
	{
		parent.AddChild(new HSeparator());
		parent.AddChild(new Label { Text = title });

		_preview = new TextureRect
		{
			Texture = _get(),
			ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
			StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
			CustomMinimumSize = new Vector2(120, 120),
		};
		parent.AddChild(_preview);

		var import = new Button { Text = "Import image…" };
		import.Pressed += () => EnsureDialog().PopupCenteredRatio(0.6f);
		parent.AddChild(import);

		var reset = new Button { Text = "Restore default image" };
		reset.Pressed += RestoreDefault;
		parent.AddChild(reset);
	}

	private FileDialog EnsureDialog()
	{
		if (_dialog != null && GodotObject.IsInstanceValid(_dialog)) return _dialog;

		_dialog = new FileDialog
		{
			FileMode = FileDialog.FileModeEnum.OpenFile,
			Access = FileDialog.AccessEnum.Filesystem,
			Filters = ["*.png, *.jpg, *.jpeg, *.webp ; Images"],
			UseNativeDialog = true, // Godot 4.4+, remove on older versions
		};
		_dialog.FileSelected += OnFileSelected;
		_owner.AddChild(_dialog);
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
		img.SavePng(_savePath);
		_set(ImageTexture.CreateFromImage(img));
		RefreshPreview();
	}

	private void RestoreDefault()
	{
		if (FileAccess.FileExists(_savePath))
			DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(_savePath));
		_set(_default);
		RefreshPreview();
	}

	private void RefreshPreview()
	{
		if (_preview != null && GodotObject.IsInstanceValid(_preview))
			_preview.Texture = _get();
	}
}