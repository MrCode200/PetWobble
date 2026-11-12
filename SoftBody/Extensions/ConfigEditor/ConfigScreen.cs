using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Config window. One tab per node in <see cref="Targets"/>. The tab content is generated from the
/// node's [Export] properties (groups, ranges, enums and hide/read-only logic from _ValidateProperty
/// are all respected). Node references and collision layers are skipped automatically.
///
/// Add a new manager: put its node into Targets. That's it.
/// </summary>
public partial class ConfigScreen : Window
{
	public delegate Control EditorFactory(PropContext ctx);

	/// <summary>Tried in order, first non-null control wins. Insert your own to support new types.</summary>
	public static readonly List<EditorFactory> EditorFactories = new()
	{
		PropertyEditors.EnumField,
		PropertyEditors.BoolField,
		PropertyEditors.NumberField,
		PropertyEditors.Vec2Field,
		PropertyEditors.ColorField,
		PropertyEditors.TextField,
		PropertyEditors.CurveField,
		PropertyEditors.ShapeField,
	};

	[Export] public Godot.Collections.Array<Node> Targets { get; set; } = new();
	[Export] public string SavePath { get; set; } = "user://slime_config.cfg";
	[Export] public Vector2I WindowSize { get; set; } = new(520, 680);

	private sealed class Section
	{
		public GodotObject Target; // object that actually holds the values (differs from Owner for IConfigProxy)
		public Node Owner;         // the node listed in Targets (title, callbacks, extras)
		public readonly List<(string Name, Control Row)> Rows = new();
	}

	private readonly record struct Prop(
		string Name, string Group, PropertyUsageFlags Usage,
		Variant.Type Type, PropertyHint Hint, string HintString);

	private TabContainer _tabs;
	private readonly List<Section> _sections = new();

	// ------------------------------------------------------------------ lifecycle
	public override void _Ready()
	{
		Title = "Slime Settings";
		MinSize = new Vector2I(380, 300);
		Transient = true;
		Visible = false;
		CloseRequested += Close;

		// Apply saved values once every node finished its own _Ready.
		Callable.From(LoadSettings).CallDeferred();
	}

	/// <summary>Call this from your "blob was left-clicked" code.</summary>
	public void Open()
	{
		int tab = _tabs != null && IsInstanceValid(_tabs) ? _tabs.CurrentTab : 0;
		BuildUi(); // rebuilt on every open so values are never stale
		if (tab >= 0 && tab < _tabs.GetTabCount()) _tabs.CurrentTab = tab;

		if (Visible) GrabFocus();
		else PopupCentered(WindowSize);
	}

	public void Close()
	{
		Hide();
		SaveSettings();
	}

	// ------------------------------------------------------------------ UI building
	private void BuildUi()
	{
		foreach (var child in GetChildren()) child.QueueFree();
		_sections.Clear();

		var bg = new Panel();
		bg.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		AddChild(bg);

		var margin = new MarginContainer();
		margin.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		foreach (var side in new[] { "margin_left", "margin_right", "margin_top", "margin_bottom" })
			margin.AddThemeConstantOverride(side, 8);
		AddChild(margin);

		_tabs = new TabContainer();
		margin.AddChild(_tabs);

		foreach (var t in Targets)
			if (IsInstanceValid(t))
				BuildSection(t);
	}

	private void BuildSection(Node target)
	{
		var section = new Section { Target = Resolve(target), Owner = target };
		_sections.Add(section);

		var scroll = new ScrollContainer
		{
			Name = (target as IConfigurable)?.ConfigTitle ?? target.Name, // becomes the tab title
			HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
		};
		var body = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		scroll.AddChild(body);
		_tabs.AddChild(scroll);

		Container parent = body;
		string lastGroup = null;

		if (section.Target == null)
			body.AddChild(new Label { Text = "Not available right now (target not found or not running yet)." });

		foreach (var p in EnumerateProps(section.Target))
		{
			if (p.Group != lastGroup)
			{
				parent = p.Group == null ? body : MakeGroup(body, p.Group);
				lastGroup = p.Group;
			}

			var ctx = MakeContext(section, p);
			var editor = EditorFactories.Select(f => f(ctx)).FirstOrDefault(e => e != null);
			if (editor == null) continue;

			var row = new HBoxContainer();
			row.AddChild(new Label
			{
				Text = Humanize(p.Name),
				TooltipText = p.Name,
				CustomMinimumSize = new Vector2(180, 0),
				ClipText = true,
			});
			row.AddChild(editor);
			parent.AddChild(row);
			section.Rows.Add((p.Name, row));
		}

		(target as IConfigExtras)?.BuildConfigUI(body);
		Refresh(section);
	}

	private static VBoxContainer MakeGroup(Container parent, string title)
	{
		var btn = new Button
		{
			Text = "▼  " + title,
			ToggleMode = true,
			ButtonPressed = true,
			Flat = true,
			Alignment = HorizontalAlignment.Left,
		};
		var box = new VBoxContainer();
		btn.Toggled += on =>
		{
			box.Visible = on;
			btn.Text = (on ? "▼  " : "▶  ") + title;
		};
		parent.AddChild(btn);
		parent.AddChild(box);
		return box;
	}

	private PropContext MakeContext(Section s, Prop p) => new()
	{
		Target = s.Target,
		Name = p.Name,
		Type = p.Type,
		Hint = p.Hint,
		HintString = p.HintString,
		Commit = v =>
		{
			if (!GodotObject.IsInstanceValid(s.Target)) return;
			s.Target.Set(p.Name, v);
			(s.Owner as IConfigurable)?.OnConfigChanged(p.Name);
			Refresh(s);
		},
	};

	/// <summary>Re-evaluates _ValidateProperty results: hides rows / makes them read-only.</summary>
	private static void Refresh(Section s)
	{
		if (s.Target == null || !GodotObject.IsInstanceValid(s.Target)) return;

		var usage = new Dictionary<string, PropertyUsageFlags>();
		foreach (var d in s.Target.GetPropertyList())
			usage[d["name"].AsString()] = (PropertyUsageFlags)d["usage"].AsInt64();

		foreach (var (name, row) in s.Rows)
		{
			if (!usage.TryGetValue(name, out var u)) continue;
			row.Visible = u.HasFlag(PropertyUsageFlags.Editor);
			SetEditable(row.GetChild(1), !u.HasFlag(PropertyUsageFlags.ReadOnly));
		}
	}

	private static void SetEditable(Node n, bool editable)
	{
		switch (n)
		{
			case BaseButton b: b.Disabled = !editable; break;
			case SpinBox sp: sp.Editable = editable; break;
			case Slider sl: sl.Editable = editable; break;
			case LineEdit le: le.Editable = editable; break;
		}
		foreach (var c in n.GetChildren()) SetEditable(c, editable);
	}

	// ------------------------------------------------------------------ property discovery
	private static HashSet<string> ExportedNames(Type type)
	{
		var set = new HashSet<string>();
		const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public |
		                           BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
		for (var t = type; t != null && t != typeof(GodotObject); t = t.BaseType)
			foreach (var m in t.GetMembers(flags))
				if (Attribute.IsDefined(m, typeof(ExportAttribute)))
					set.Add(m.Name);
		return set;
	}

	/// <summary>
	/// Uses Godot's own property list (correct order, groups, hints, validated usage flags) and
	/// keeps only the members that carry [Export] in C#.
	/// </summary>
	private static IEnumerable<Prop> EnumerateProps(GodotObject target)
	{
		if (target == null || !GodotObject.IsInstanceValid(target)) yield break;

		var exported = ExportedNames(target.GetType());
		string group = null;

		foreach (var d in target.GetPropertyList())
		{
			var usage = (PropertyUsageFlags)d["usage"].AsInt64();
			var name = d["name"].AsString();

			if (usage.HasFlag(PropertyUsageFlags.Category)) { group = null; continue; }
			if (usage.HasFlag(PropertyUsageFlags.Group)) { group = name == "" ? null : name; continue; }
			if (!exported.Contains(name)) continue;

			var type = (Variant.Type)d["type"].AsInt32();
			var hint = (PropertyHint)d["hint"].AsInt32();

			if (hint == PropertyHint.NodeType) continue;                  // node references
			if (hint.ToString().StartsWith("Layers")) continue;           // collision layers / masks
			if (type == Variant.Type.Object && hint != PropertyHint.ResourceType) continue;

			yield return new Prop(name, group, usage, type, hint, d["hint_string"].AsString());
		}
	}

	private static string Humanize(string name) =>
		Regex.Replace(name, "(?<=[a-z0-9])(?=[A-Z])", " ");

	// ------------------------------------------------------------------ persistence
	private static string Key(GodotObject t) => t is Node n ? n.Name.ToString() : t.GetType().Name;
	private static string ResPath(GodotObject t, string prop) => $"user://cfg_{Key(t)}_{prop}.tres";

	/// <summary>The object that holds the values: the node itself, or what its IConfigProxy points to.</summary>
	private static GodotObject Resolve(Node owner) =>
		owner is IConfigProxy proxy ? proxy.ConfigObject : owner;

	public void SaveSettings()
	{
		var cfg = new ConfigFile();
		cfg.Load(SavePath); // keep old values of targets that are unavailable right now (e.g. a BT task not running)

		foreach (var owner in Targets)
		{
			if (!IsInstanceValid(owner)) continue;
			var t = Resolve(owner);
			foreach (var p in EnumerateProps(t)) // yields nothing when t is null
			{
				var v = t.Get(p.Name);
				if (p.Type == Variant.Type.Object)
				{
					if (v.AsGodotObject() is Resource r) ResourceSaver.Save(r, ResPath(owner, p.Name));
				}
				else cfg.SetValue(Key(owner), p.Name, v);
			}
		}
		cfg.Save(SavePath);
	}

	public void LoadSettings()
	{
		var cfg = new ConfigFile();
		if (cfg.Load(SavePath) != Error.Ok) return;

		foreach (var owner in Targets)
		{
			if (!IsInstanceValid(owner)) continue;
			var t = Resolve(owner);
			foreach (var p in EnumerateProps(t)) // yields nothing when t is null
			{
				if (p.Type == Variant.Type.Object)
				{
					var path = ResPath(owner, p.Name);
					if (!ResourceLoader.Exists(path)) continue;
					t.Set(p.Name, ResourceLoader.Load(path));
				}
				else if (cfg.HasSectionKey(Key(owner), p.Name))
					t.Set(p.Name, cfg.GetValue(Key(owner), p.Name));
				else continue;

				(owner as IConfigurable)?.OnConfigChanged(p.Name);
			}
		}
	}
}