using System;
using System.Globalization;
using System.Linq;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Each method is an "editor factory": it returns a Control for the property, or null if it
/// does not handle that property type. ConfigScreen.EditorFactories is tried in order.
/// To support a new type, write another factory and add it to that list.
/// </summary>
public static class PropertyEditors
{
	// ------------------------------------------------------------------ helpers
	public static SpinBox Spin(double value, double min, double max, double step, bool free, Action<double> onChanged)
	{
		var s = new SpinBox
		{
			MinValue = min, MaxValue = max, Step = step,
			AllowGreater = free, AllowLesser = free,
			Value = value, // set BEFORE connecting the signal so it does not fire on init
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		s.ValueChanged += v => onChanged(v);
		return s;
	}

	public static Label Tag(string text) => new() { Text = text };

	private static bool Dbl(string s, out double d) =>
		double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out d);

	private static (double min, double max, double step, bool bounded, bool free) ParseRange(PropContext c, bool isInt)
	{
		double step = isInt ? 1 : 0.01;
		if (c.Hint == PropertyHint.Range)
		{
			var p = c.HintString.Split(',', StringSplitOptions.TrimEntries);
			bool free = p.Contains("or_greater") || p.Contains("or_less");
			if (p.Length >= 2 && Dbl(p[0], out var min) && Dbl(p[1], out var max))
			{
				if (p.Length >= 3 && Dbl(p[2], out var st) && st > 0) step = st;
				return (min, max, step, true, free);
			}
		}
		return (-1_000_000, 1_000_000, step, false, true);
	}

	// ------------------------------------------------------------------ factories
	public static Control EnumField(PropContext c)
	{
		if (c.Hint != PropertyHint.Enum || c.Type != Variant.Type.Int) return null;

		var ob = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		int next = 0;
		foreach (var item in c.HintString.Split(','))
		{
			var parts = item.Split(':');
			int val = parts.Length > 1 && int.TryParse(parts[1], out var pv) ? pv : next;
			next = val + 1;
			ob.AddItem(parts[0], val); // item id == enum value
		}
		ob.Selected = ob.GetItemIndex(c.Value.AsInt32());
		ob.ItemSelected += idx => c.Commit(ob.GetItemId((int)idx));
		return ob;
	}

	public static Control BoolField(PropContext c)
	{
		if (c.Type != Variant.Type.Bool) return null;
		var cb = new CheckBox { ButtonPressed = c.Value.AsBool() };
		cb.Toggled += on => c.Commit(on);
		return cb;
	}

	public static Control NumberField(PropContext c)
	{
		if (c.Type != Variant.Type.Int && c.Type != Variant.Type.Float) return null;

		bool isInt = c.Type == Variant.Type.Int;
		var (min, max, step, bounded, free) = ParseRange(c, isInt);
		double cur = c.Value.AsDouble();

		Action<double> apply = v =>
			c.Commit(isInt ? Variant.From((long)Math.Round(v)) : Variant.From((float)v));

		// No range hint -> plain unbounded spin box
		if (!bounded)
			return Spin(cur, min, max, step, true, apply);

		// Range hint -> slider + spin box kept in sync
		var box = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var slider = new HSlider
		{
			MinValue = min, MaxValue = max, Step = step, Value = cur,
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
			SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
			CustomMinimumSize = new Vector2(100, 0),
		};
		SpinBox spin = null;
		spin = Spin(cur, min, max, step, free, v => { slider.SetValueNoSignal(v); apply(v); });
		spin.SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd;
		spin.CustomMinimumSize = new Vector2(90, 0);
		slider.ValueChanged += v => { spin.SetValueNoSignal(v); apply(v); };

		box.AddChild(slider);
		box.AddChild(spin);
		return box;
	}

	public static Control Vec2Field(PropContext c)
	{
		if (c.Type != Variant.Type.Vector2) return null;

		var v = c.Value.AsVector2();
		var box = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		SpinBox x = null, y = null;
		void Apply() => c.Commit(new Vector2((float)x.Value, (float)y.Value));
		x = Spin(v.X, -1e6, 1e6, 0.01, true, _ => Apply());
		y = Spin(v.Y, -1e6, 1e6, 0.01, true, _ => Apply());
		box.AddChild(Tag("x")); box.AddChild(x);
		box.AddChild(Tag("y")); box.AddChild(y);
		return box;
	}

	public static Control ColorField(PropContext c)
	{
		if (c.Type != Variant.Type.Color) return null;
		var b = new ColorPickerButton
		{
			Color = c.Value.AsColor(),
			CustomMinimumSize = new Vector2(60, 24),
			SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
		};
		b.ColorChanged += col => c.Commit(col);
		return b;
	}

	public static Control TextField(PropContext c)
	{
		if (c.Type != Variant.Type.String) return null;
		var le = new LineEdit { Text = c.Value.AsString(), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		le.TextSubmitted += t => c.Commit(t);
		le.FocusExited += () => c.Commit(le.Text);
		return le;
	}

	public static Control CurveField(PropContext c)
	{
		if (c.Type != Variant.Type.Object || c.HintString != "Curve") return null;

		var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };

		void Show(Curve cv)
		{
			var ed = new CurveEditor();
			ed.Setup(cv, () => c.Commit(cv));

			var limits = new HBoxContainer();
			limits.AddChild(Tag("min"));
			limits.AddChild(Spin(cv.MinValue, -1e6, 1e6, 0.01, true,
				v => { cv.MinValue = (float)v; ed.QueueRedraw(); c.Commit(cv); }));
			limits.AddChild(Tag("max"));
			limits.AddChild(Spin(cv.MaxValue, -1e6, 1e6, 0.01, true,
				v => { cv.MaxValue = (float)v; ed.QueueRedraw(); c.Commit(cv); }));

			box.AddChild(ed);
			box.AddChild(limits);
		}

		var curve = c.Value.As<Curve>();
		if (curve != null)
		{
			Show(curve);
		}
		else
		{
			var create = new Button { Text = "Create curve" };
			create.Pressed += () =>
			{
				var nc = new Curve();
				nc.AddPoint(new Vector2(0, 0));
				nc.AddPoint(new Vector2(1, 1));
				c.Commit(nc);
				create.QueueFree();
				Show(nc);
			};
			box.AddChild(create);
		}
		return box;
	}

	public static Control ShapeField(PropContext c)
	{
		if (c.Type != Variant.Type.Object || c.HintString != "Shape2D") return null;

		var box = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
		var shape = c.Value.As<Shape2D>();

		switch (shape)
		{
			case CircleShape2D circle:
				box.AddChild(Tag("radius"));
				box.AddChild(Spin(circle.Radius, 0.1, 1e6, 0.5, true,
					v => { circle.Radius = (float)v; c.Commit(circle); }));
				break;

			case RectangleShape2D rect:
			{
				SpinBox w = null, h = null;
				void Apply() { rect.Size = new Vector2((float)w.Value, (float)h.Value); c.Commit(rect); }
				w = Spin(rect.Size.X, 0.1, 1e6, 0.5, true, _ => Apply());
				h = Spin(rect.Size.Y, 0.1, 1e6, 0.5, true, _ => Apply());
				box.AddChild(Tag("w")); box.AddChild(w);
				box.AddChild(Tag("h")); box.AddChild(h);
				break;
			}

			case CapsuleShape2D cap:
			{
				SpinBox r = null, h = null;
				void Apply() { cap.Radius = (float)r.Value; cap.Height = (float)h.Value; c.Commit(cap); }
				r = Spin(cap.Radius, 0.1, 1e6, 0.5, true, _ => Apply());
				h = Spin(cap.Height, 0.1, 1e6, 0.5, true, _ => Apply());
				box.AddChild(Tag("radius")); box.AddChild(r);
				box.AddChild(Tag("height")); box.AddChild(h);
				break;
			}

			default:
				return new Label { Text = shape == null ? "(no shape)" : $"({shape.GetClass()} not editable)" };
		}
		return box;
	}
}