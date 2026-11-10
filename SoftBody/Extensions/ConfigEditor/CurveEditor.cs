using System;
using Godot;

namespace PetWobble.Config;

/// <summary>
/// Minimal curve editor. Left click = add / drag a point, right click = delete a point.
/// X domain is fixed to 0..1, Y uses Curve.MinValue..MaxValue.
/// </summary>
public partial class CurveEditor : Control
{
	private const float Pad = 8f;
	private const float HitRadius = 9f;

	private Curve _curve;
	private Action _changed;
	private int _drag = -1;

	public CurveEditor()
	{
		CustomMinimumSize = new Vector2(0, 110);
		FocusMode = FocusModeEnum.Click;
		SizeFlagsHorizontal = SizeFlags.ExpandFill;
	}

	public void Setup(Curve curve, Action changed)
	{
		_curve = curve;
		_changed = changed;
		QueueRedraw();
	}

	private Vector2 PlotSize => Size - new Vector2(Pad * 2, Pad * 2);
	private float YRange => Mathf.Max(_curve.MaxValue - _curve.MinValue, 1e-6f);

	private Vector2 ToScreen(Vector2 p)
	{
		var a = PlotSize;
		return new Vector2(
			Pad + p.X * a.X,
			Pad + (1f - (p.Y - _curve.MinValue) / YRange) * a.Y);
	}

	private Vector2 FromScreen(Vector2 s)
	{
		var a = PlotSize;
		float x = Mathf.Clamp((s.X - Pad) / Mathf.Max(a.X, 1f), 0f, 1f);
		float t = Mathf.Clamp(1f - (s.Y - Pad) / Mathf.Max(a.Y, 1f), 0f, 1f);
		return new Vector2(x, _curve.MinValue + t * YRange);
	}

	private int Pick(Vector2 screen)
	{
		for (int i = 0; i < _curve.PointCount; i++)
			if (ToScreen(_curve.GetPointPosition(i)).DistanceTo(screen) <= HitRadius)
				return i;
		return -1;
	}

	private void Notify()
	{
		QueueRedraw();
		_changed?.Invoke();
	}

	public override void _GuiInput(InputEvent e)
	{
		if (_curve == null) return;

		if (e is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.Left)
			{
				if (mb.Pressed)
				{
					_drag = Pick(mb.Position);
					if (_drag < 0)
					{
						_drag = _curve.AddPoint(FromScreen(mb.Position), 0, 0,
							Curve.TangentMode.Linear, Curve.TangentMode.Linear);
						Notify();
					}
				}
				else _drag = -1;
				AcceptEvent();
			}
			else if (mb.ButtonIndex == MouseButton.Right && mb.Pressed)
			{
				int i = Pick(mb.Position);
				if (i >= 0 && _curve.PointCount > 2)
				{
					_curve.RemovePoint(i);
					Notify();
				}
				AcceptEvent();
			}
		}
		else if (e is InputEventMouseMotion mm && _drag >= 0)
		{
			var p = FromScreen(mm.Position);
			_drag = _curve.SetPointOffset(_drag, p.X); // may re-order, returns the new index
			_curve.SetPointValue(_drag, p.Y);
			Notify();
			AcceptEvent();
		}
	}

	public override void _Draw()
	{
		var rect = new Rect2(Vector2.Zero, Size);
		DrawRect(rect, new Color(0, 0, 0, 0.35f));
		DrawRect(rect, new Color(1, 1, 1, 0.25f), false);
		if (_curve == null) return;

		// grid
		for (int i = 1; i < 4; i++)
		{
			float gx = Pad + PlotSize.X * i / 4f;
			float gy = Pad + PlotSize.Y * i / 4f;
			DrawLine(new Vector2(gx, Pad), new Vector2(gx, Pad + PlotSize.Y), new Color(1, 1, 1, 0.08f));
			DrawLine(new Vector2(Pad, gy), new Vector2(Pad + PlotSize.X, gy), new Color(1, 1, 1, 0.08f));
		}

		const int samples = 64;
		var pts = new Vector2[samples + 1];
		for (int i = 0; i <= samples; i++)
		{
			float x = i / (float)samples;
			pts[i] = ToScreen(new Vector2(x, _curve.Sample(x)));
		}
		DrawPolyline(pts, new Color(0.4f, 0.8f, 1f), 2f, true);

		for (int i = 0; i < _curve.PointCount; i++)
			DrawCircle(ToScreen(_curve.GetPointPosition(i)), 4f, i == _drag ? Colors.White : new Color(1f, 0.75f, 0.3f));
	}
}