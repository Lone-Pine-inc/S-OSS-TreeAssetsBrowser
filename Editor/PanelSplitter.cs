using Editor;
using Sandbox;
using System;

namespace GeneralGame.Editor;

public class PanelSplitter : Widget
{
    public const float Thickness = 6f;
    private const float MinPanelWidth = 150f;

    private readonly Widget _left;
    private readonly Widget _right;

    // Called when a drag finishes, so the owner can persist the new sizing
    public Action OnResized;

    private bool _isDragging;
    private bool _isHovered;
    private float _dragStartScreenX;
    private float _leftStartWidth;
    private float _rightStartWidth;

    public PanelSplitter(Widget parent, Widget left, Widget right) : base(parent)
    {
        _left = left;
        _right = right;

        FixedWidth = Thickness;
        Cursor = CursorShape.SizeH;
        SetSizeMode(SizeMode.Default, SizeMode.CanGrow);
    }

    protected override void OnPaint()
    {
        var rect = LocalRect;

        var color = _isDragging || _isHovered
            ? Theme.Primary.WithAlpha(0.6f)
            : Color.White.WithAlpha(0.08f);

        Paint.SetBrush(color);
        var line = new Rect(rect.Center.x - 1, rect.Top + 2, 2, rect.Height - 4);
        Paint.DrawRect(line, 1);
    }

    protected override void OnMouseEnter()
    {
        _isHovered = true;
        Update();
    }

    protected override void OnMouseLeave()
    {
        _isHovered = false;
        Update();
    }

    protected override void OnMousePress(MouseEvent e)
    {
        base.OnMousePress(e);

        if (e.LeftMouseButton)
        {
            _isDragging = true;
            _dragStartScreenX = e.ScreenPosition.x;
            _leftStartWidth = _left.Width;
            _rightStartWidth = _right.Width;
            e.Accepted = true;
        }
    }

    protected override void OnMouseMove(MouseEvent e)
    {
        base.OnMouseMove(e);

        if (!_isDragging)
            return;

        var delta = e.ScreenPosition.x - _dragStartScreenX;

        // Keep both neighbours above the minimum width
        var minDelta = MinPanelWidth - _leftStartWidth;
        var maxDelta = _rightStartWidth - MinPanelWidth;
        delta = Math.Clamp(delta, minDelta, maxDelta);

        _left.FixedWidth = _leftStartWidth + delta;
    }

    protected override void OnMouseReleased(MouseEvent e)
    {
        base.OnMouseReleased(e);

        if (_isDragging)
        {
            _isDragging = false;
            OnResized?.Invoke();
        }

        Update();
    }
}
