using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using Godot;
using Environment = System.Environment;

namespace PetWobble.SoftBody.Extensions;

// TODO:
// make windows native so that other objects can be seen,
// as MousePassthrough makes only area inside polygon visible
public partial class ClickThroughWindow : Node2D
{
	[Export] public Area2D[] ClickableAreas;
	[Export(PropertyHint.Layers2DPhysics)] public uint ClickableMask;
	[Export] public bool Disable;
	
	private bool _passthrough;
	private IntPtr _hwndPtr;
	private bool _is64Bit;
	private bool _initSuccess;

	private const int GwlExStyle = -20;
	
	private const long WsExTransparent = 0x00000020L;
	private const long WsExLayered = 0x00080000L; 
	
	private const uint SwpNoSize = 0x0001;
	private const uint SwpNoMove = 0x0002;
	private const uint SwpNoZOrder = 0x0004;
	private const uint SwpNoActivate = 0x0010;
	private const uint SwpFrameChanged = 0x0020;
	private const uint RefreshFrameFlag = SwpNoSize | SwpNoMove | SwpNoZOrder | SwpNoActivate | SwpFrameChanged;
 
	// Supports 64bit
	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr GetWindowLongPtrW(
		IntPtr hWnd,
		int nIndex
	);

	// Supports 64 bit
	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowLongPtrW(
		IntPtr hWnd,
		int nIndex,
		IntPtr value
	); 
	
	// Supports 32 bit
	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr GetWindowLongW(
		IntPtr hWnd,
		int nIndex
	);

	// Supports 32 bit
	[DllImport("user32.dll", SetLastError = true)]
	private static extern IntPtr SetWindowLongW(
		IntPtr hWnd,
		int nIndex,
		IntPtr value
	);
	
	[DllImport("user32.dll")]
	private static extern bool SetWindowPos(
		IntPtr hWnd,
		IntPtr hWndInsertAfter,
		int x, int y, int width, int height, 
		uint flags);

	
	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int X;
		public int Y;
	}
	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetCursorPos(out POINT lpPoint);
	
	public override void _Ready()
	{
		GD.Print($"WindowTrans: {GetWindow().MousePassthrough}");
		long nativeHandle =
			DisplayServer.WindowGetNativeHandle(
				DisplayServer.HandleType.WindowHandle,
				GetWindow().GetWindowId()
			);
		_hwndPtr = new IntPtr(nativeHandle);
		_is64Bit = Environment.Is64BitProcess;
		
		Marshal.SetLastPInvokeError(0);
		long style = _is64Bit 
			? GetWindowLongPtrW(_hwndPtr, GwlExStyle)
			: GetWindowLongW(_hwndPtr, GwlExStyle);
		
		if (Marshal.GetLastPInvokeError() != 0)
		{
			GD.Print("Error getting style GetWindowLongPtrW: GwlExStyle");
			return;
		}

		var newStyle = new IntPtr(style | WsExLayered);
		
		if (_is64Bit) 
			SetWindowLongPtrW(_hwndPtr, GwlExStyle, newStyle);
		else 
			SetWindowLongW(_hwndPtr, GwlExStyle, newStyle);
		
		if (Marshal.GetLastPInvokeError() != 0)
		{
			GD.Print("Error setting style SetWindowLongPtrW: style | WsExLayered");
			return;
		}

		_initSuccess = true;
	}
	
	public override void _Process(double delta)
	{
		if (Disable || !_initSuccess)
			return;
		
		//Vector2 viewportPos = (DisplayServer.MouseGetPosition() - GetWindow().Position); // screen -> window pixels
		//var mousePos = GetGlobalMousePosition(); //GetViewport().CanvasTransform.AffineInverse() * viewportPos;               // window -> world

		POINT pos;
		if (!GetCursorPos(out pos))
			GD.Print("Failed to GetCursorPos");

		var mousePos = new Vector2(pos.X, pos.Y);
		//GD.Print($"{mousePos} vs {GetGlobalMousePosition()}");
		
		SetPassthrough(!IsMouseOverAreas(mousePos));
	}
	
	private bool IsMouseOverAreas(Vector2 mousePos)
	{
		var space = GetWorld2D().DirectSpaceState;
		var param = new PhysicsPointQueryParameters2D
		{
			Position = mousePos,
			CollisionMask = ClickableMask,
			CollideWithAreas = true
		};
		
		var hits = space.IntersectPoint(param);
		
		foreach (var hit in hits)
		{
			var collider = hit["collider"].As<Area2D>();

			if (ClickableAreas.Any(area => collider == area))
			{
				GD.Print("Mouse over Clickable Area");
				return true;
			}
		}

		return false;
	}
	
	private void SetPassthrough(bool passthrough)
	{
		if (_passthrough == passthrough)
			return;
		
		GD.Print($"Passthrough toggled: {passthrough}");
		_passthrough = passthrough;
		
		Marshal.SetLastPInvokeError(0);
		long style = _is64Bit 
			? GetWindowLongPtrW(_hwndPtr, GwlExStyle)
			: GetWindowLongW(_hwndPtr, GwlExStyle);
		
		if (Marshal.GetLastPInvokeError() != 0)
		{
			GD.Print("Error getting style GetWindowLongPtrW: GwlExStyle");
			return;
		}		
		
		var newStyle = new IntPtr(_passthrough
			? style | WsExTransparent
			: style & ~WsExTransparent);
		
		if (_is64Bit) 
			SetWindowLongPtrW(_hwndPtr, GwlExStyle, newStyle);
		else 
			SetWindowLongW(_hwndPtr, GwlExStyle, newStyle);
		
		if (Marshal.GetLastPInvokeError() != 0)
			GD.Print("Error setting style SetWindowLongPtrW: style | WsExTransparent");

		var success = SetWindowPos(
			_hwndPtr, IntPtr.Zero,
			0, 0, 0, 0,
			RefreshFrameFlag);
		if (!success)
				GD.Print("Failed to SetWindowPos : RefreshFrameFlag");
	}

	// private Vector2[] GetWindowPolygon()
	// {
	//     var canvasXform = GetViewport().GetCanvasTransform();
	//
	//     return SB.Polygon
	//         .Take(SB.Polygon.Length - 1)
	//         .Select(p => canvasXform * SB.ToGlobal(p))
	//         .ToArray();
	// }
}
