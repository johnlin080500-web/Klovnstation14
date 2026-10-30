using System;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared.Input;

namespace Content.Client._KS14.Trading;

public enum TradingChartType : byte
{
    Line,
    Bar,
    Candle
}

/// <summary>
/// Real recorded server prices. X is elapsed sample time (15 seconds per point);
/// Y is price in spesos. Bars show price, candles group four consecutive
/// observations into open/high/low/close. Click to inspect a price point.
/// </summary>
public sealed class TradingPriceGraphControl : Control
{
    public int[] History { get; set; } = Array.Empty<int>();
    public int SamplesToShow { get; set; }
    public TradingChartType ChartType { get; set; }
    public event Action<int, int>? OnSampleSelected;

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick || History.Length == 0)
            return;

        var count = SamplesToShow == 0 ? History.Length : Math.Min(History.Length, SamplesToShow);
        if (count == 0)
            return;

        var width = Size.X * UIScale;
        var cursor = (UserInterfaceManager.MousePositionScaled.Position * UIScale) - GlobalPixelPosition;
        var t = Math.Clamp((cursor.X - 10f * UIScale) / Math.Max(1, width - 20f * UIScale), 0, 1);
        var index = Math.Clamp((int) Math.Round(t * (count - 1)), 0, count - 1);
        var selected = History.Length - count + index;
        OnSampleSelected?.Invoke(selected, History[selected]);
        args.Handle();
    }

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
        if (min == max)
        {
            min -= 1;
            max += 1;
        }

        var left = Position + new Vector2(10 * UIScale, 12 * UIScale);
        var right = Position + new Vector2(w - 10 * UIScale, h - 12 * UIScale);
        var grid = Color.FromHex("#334356");
        var rising = Color.FromHex("#60d9ad");
        var falling = Color.FromHex("#f16f79");
        for (var n = 0; n <= 4; n++)
        {
            var y = left.Y + (right.Y - left.Y) * n / 4f;
            handle.DrawLine(new Vector2(left.X, y), new Vector2(right.X, y), grid);
        }

        Vector2 Point(int index, int value)
        {
            var x = count == 1 ? (left.X + right.X) / 2f
                : left.X + (right.X - left.X) * index / (count - 1f);
            var y = right.Y - (right.Y - left.Y) * (value - min) / (max - (float) min);
            return new Vector2(x, y);
        }

        if (ChartType == TradingChartType.Candle)
        {
            for (var i = 0; i < count; i += 4)
            {
                var end = Math.Min(i + 4, count);
                var open = History[start + i];
                var close = History[start + end - 1];
                var high = int.MinValue;
                var low = int.MaxValue;
                for (var k = i; k < end; k++)
                {
                    high = Math.Max(high, History[start + k]);
                    low = Math.Min(low, History[start + k]);
                }

                var x = Point(i + (end - i - 1) / 2, open).X;
                var color = close >= open ? rising : falling;
                handle.DrawLine(new Vector2(x, Point(i, high).Y),
                    new Vector2(x, Point(i, low).Y), color);
                var bar = Math.Max(2f, (right.X - left.X) / Math.Max(count, 2) * 2f);
                var y1 = Point(i, open).Y;
                var y2 = Point(i, close).Y;
                handle.DrawLine(new Vector2(x - bar, y1), new Vector2(x + bar, y1), color);
                handle.DrawLine(new Vector2(x - bar, y2), new Vector2(x + bar, y2), color);
                handle.DrawLine(new Vector2(x - bar, y1), new Vector2(x - bar, y2), color);
                handle.DrawLine(new Vector2(x + bar, y1), new Vector2(x + bar, y2), color);
            }
            return;
        }

        Vector2 previous = default;
        for (var i = 0; i < count; i++)
        {
            var point = Point(i, History[start + i]);
            var color = i == 0 || History[start + i] >= History[start + i - 1] ? rising : falling;
            if (ChartType == TradingChartType.Bar)
                handle.DrawLine(new Vector2(point.X, right.Y), point, color);
            else if (i != 0)
                handle.DrawLine(previous, point, color);
            else if (count == 1)
                handle.DrawCircle(point, 3f * UIScale, color);
            previous = point;
        }
    }
}
