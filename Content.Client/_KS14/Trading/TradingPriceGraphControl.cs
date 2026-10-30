using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;

namespace Content.Client._KS14.Trading;

/// <summary>
/// Draws the actual recorded exchange prices, with a selectable history window.
/// Samples arrive from the authoritative trading server every 15 seconds.
/// </summary>
public sealed class TradingPriceGraphControl : Control
{
    public int[] History { get; set; } = Array.Empty<int>();
    public int SamplesToShow { get; set; } // 0 = all samples

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var count = SamplesToShow == 0 ? History.Length : Math.Min(History.Length, SamplesToShow);
        if (count == 0)
            return;

        var w = Size.X * UIScale;
        var h = Size.Y * UIScale;
        if (w < 30 || h < 30)
            return;

        var start = History.Length - count;
        var min = int.MaxValue;
        var max = int.MinValue;
        for (var i = start; i < History.Length; i++)
        {
            min = Math.Min(min, History[i]);
            max = Math.Max(max, History[i]);
        }

        // Keep a flat price series visible instead of dividing by zero.
        if (min == max)
        {
            min -= 1;
            max += 1;
        }

        var left = Position + new Vector2(10f * UIScale, 12f * UIScale);
        var right = Position + new Vector2(w - 10f * UIScale, h - 12f * UIScale);
        var gridColor = Color.FromHex("#334356");
        var lineColor = Color.FromHex("#60d9ad");
        for (var n = 0; n <= 4; n++)
        {
            var y = left.Y + (right.Y - left.Y) * n / 4f;
            handle.DrawLine(new Vector2(left.X, y), new Vector2(right.X, y), gridColor);
        }

        var previous = Vector2.Zero;
        for (var i = 0; i < count; i++)
        {
            var value = History[start + i];
            var x = count == 1 ? (left.X + right.X) / 2f
                : left.X + (right.X - left.X) * i / (count - 1f);
            var y = right.Y - (right.Y - left.Y) * (value - min) / (max - (float) min);
            var point = new Vector2(x, y);
            if (i != 0)
                handle.DrawLine(previous, point, lineColor);
            else if (count == 1)
                handle.DrawCircle(point, 3f * UIScale, lineColor);
            previous = point;
        }
    }
}
