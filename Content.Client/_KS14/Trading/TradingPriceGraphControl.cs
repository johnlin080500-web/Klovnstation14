using System;
using System.Numerics;
using Content.Client.Resources;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
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
/// Chart coordinates are local pixels, never Position (which is relative to parent).
/// A clickable, sized Control with time and price axes, line mode, compact OHLC bars,
/// and real filled candle bodies. Four samples form an OHLC period.
/// </summary>
public sealed class TradingPriceGraphControl : Control
{
    private const float ChartLeft = 56f;
    private const float ChartRight = 16f;
    private const float ChartTop = 18f;
    private const float ChartBottom = 28f;

    private readonly Font _axisFont;
    private int _selectedIndex = -1;

    public int[] History { get; set; } = Array.Empty<int>();
    public int[] HistoryTimes { get; set; } = Array.Empty<int>();
    public int[] EventTimes { get; set; } = Array.Empty<int>();
    public int SamplesToShow { get; set; }
    public TradingChartType ChartType { get; set; }

    public event Action<int, int>? OnSampleSelected;

    public TradingPriceGraphControl()
    {
        // The default Control mouse filter is Ignore. Without Stop, graph clicks
        // travel through the chart and never reach KeyBindDown.
        MouseFilter = MouseFilterMode.Stop;
        var resource = IoCManager.Resolve<IResourceCache>()
            .GetResource<FontResource>("/EngineFonts/NotoSans/NotoSansMono-Regular.ttf");
        _axisFont = new VectorFont(resource, 11);
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled || args.Function != EngineKeyFunctions.UIClick || History.Length == 0)
            return;

        var width = PixelWidth;
        var height = PixelHeight;
        var left = ChartLeft * UIScale;
        var right = width - ChartRight * UIScale;
        var top = ChartTop * UIScale;
        var bottom = height - ChartBottom * UIScale;

        // Screen mouse -> control-local pixels. GlobalPixelPosition should NOT be
        // subtracted from an already scaled virtual coordinate here.
        var local = (UserInterfaceManager.MousePositionScaled.Position - GlobalPosition) * UIScale;
        if (local.X < left || local.X > right || local.Y < top || local.Y > bottom)
            return;

        var count = VisibleCount;
        var start = History.Length - count;
        var firstTime = TimeAt(start);
        var lastTime = TimeAt(History.Length - 1);
        var clickFraction = Math.Clamp((local.X - left) / Math.Max(1, right - left), 0f, 1f);
        var best = start;
        var bestDist = float.MaxValue;
        for (var i = start; i < History.Length; i++)
        {
            var fraction = lastTime == firstTime
                ? (count <= 1 ? 0.5f : (float) (i - start) / (count - 1))
                : (TimeAt(i) - firstTime) / (float) (lastTime - firstTime);
            var d = Math.Abs(fraction - clickFraction);
            if (d >= bestDist)
                continue;
            best = i;
            bestDist = d;
        }

        _selectedIndex = best;
        OnSampleSelected?.Invoke(best, History[best]);
        args.Handle();
    }

    private int VisibleCount => SamplesToShow == 0
        ? History.Length
        : Math.Min(History.Length, SamplesToShow);

    private int TimeAt(int i) => HistoryTimes.Length == History.Length
        ? HistoryTimes[i]
        : i * 15;

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);
        var count = VisibleCount;
        if (count == 0 || PixelWidth < 140 || PixelHeight < 90)
            return;

        var start = History.Length - count;
        var left = ChartLeft * UIScale;
        var right = PixelWidth - ChartRight * UIScale;
        var top = ChartTop * UIScale;
        var bottom = PixelHeight - ChartBottom * UIScale;
        var plotWidth = right - left;
        var plotHeight = bottom - top;
        if (plotWidth <= 0 || plotHeight <= 0)
            return;

        var firstTime = TimeAt(start);
        var lastTime = TimeAt(History.Length - 1);
        var low = int.MaxValue;
        var high = int.MinValue;
        for (var i = start; i < History.Length; i++)
        {
            low = Math.Min(low, History[i]);
            high = Math.Max(high, History[i]);
        }

        var padding = Math.Max(1f, (high - low) * 0.08f);
        var min = low - padding;
        var max = high + padding;
        var range = Math.Max(1f, max - min);

        var grid = Color.FromHex("#314454");
        var label = Color.FromHex("#9aaebe");
        var rising = Color.FromHex("#42c9a1");
        var falling = Color.FromHex("#e06b7b");
        var marker = Color.FromHex("#f2cd73");

        float X(int index)
        {
            var fraction = lastTime == firstTime
                ? (count <= 1 ? 0.5f : (float) (index - start) / (count - 1))
                : (TimeAt(index) - firstTime) / (float) (lastTime - firstTime);
            return left + plotWidth * fraction;
        }

        float Y(float price) => bottom - plotHeight * (price - min) / range;

        for (var tick = 0; tick <= 4; tick++)
        {
            var price = max - range * tick / 4f;
            var y = top + plotHeight * tick / 4f;
            handle.DrawLine(new Vector2(left, y), new Vector2(right, y), grid);
            handle.DrawString(_axisFont, new Vector2(3 * UIScale, y - 6 * UIScale),
                price.ToString("F0"), UIScale, label);
        }

        for (var tick = 0; tick <= 4; tick++)
        {
            var fraction = tick / 4f;
            var x = left + plotWidth * fraction;
            var seconds = firstTime + (int) ((lastTime - firstTime) * fraction);
            handle.DrawLine(new Vector2(x, top), new Vector2(x, bottom), grid);
            handle.DrawString(_axisFont, new Vector2(x - 17 * UIScale, bottom + 7 * UIScale),
                $"t+{seconds / 60:00}:{seconds % 60:00}", UIScale * 0.8f, label);
        }

        // Compact news markers below the plot. No long vertical lines covering prices.
        if (lastTime > firstTime)
        {
            foreach (var eventTime in EventTimes)
            {
                if (eventTime < firstTime || eventTime > lastTime)
                    continue;
                var x = left + plotWidth * (eventTime - firstTime) / (lastTime - (float) firstTime);
                handle.DrawLine(new Vector2(x, bottom - 7 * UIScale),
                    new Vector2(x, bottom), marker);
            }
        }

        if (_selectedIndex >= start && _selectedIndex < History.Length)
        {
            var x = X(_selectedIndex);
            handle.DrawLine(new Vector2(x, top), new Vector2(x, bottom), marker);
            handle.DrawCircle(new Vector2(x, Y(History[_selectedIndex])), 3 * UIScale, marker);
        }

        if (ChartType == TradingChartType.Line)
        {
            var previous = new Vector2(X(start), Y(History[start]));
            for (var i = start + 1; i < History.Length; i++)
            {
                var point = new Vector2(X(i), Y(History[i]));
                handle.DrawLine(previous, point,
                    History[i] >= History[i - 1] ? rising : falling);
                previous = point;
            }
            if (count == 1)
                handle.DrawCircle(previous, 2.5f * UIScale, rising);
            return;
        }

        // Both financial bar and candlestick modes represent OHLC periods.
        // Group four successive quotes (about one minute at 15s intervals).
        var periods = (count + 3) / 4;
        var bodyHalf = Math.Clamp(plotWidth / Math.Max(1, periods) * 0.24f,
            2.5f * UIScale, 8f * UIScale);

        for (var i = start; i < History.Length; i += 4)
        {
            var last = Math.Min(i + 3, History.Length - 1);
            var open = History[i];
            var close = History[last];
            var highest = open;
            var lowest = open;
            for (var k = i + 1; k <= last; k++)
            {
                highest = Math.Max(highest, History[k]);
                lowest = Math.Min(lowest, History[k]);
            }

            var x = (X(i) + X(last)) / 2f;
            var color = close >= open ? rising : falling;
            var highY = Y(highest);
            var lowY = Y(lowest);
            var openY = Y(open);
            var closeY = Y(close);
            handle.DrawLine(new Vector2(x, highY), new Vector2(x, lowY), color);

            if (ChartType == TradingChartType.Bar)
            {
                // Traditional OHLC bars: a small left tick for open, right for close.
                handle.DrawLine(new Vector2(x - bodyHalf, openY),
                    new Vector2(x, openY), color);
                handle.DrawLine(new Vector2(x, closeY),
                    new Vector2(x + bodyHalf, closeY), color);
            }
            else
            {
                // Candlestick: filled coloured BODY, with a minimum 3px height
                // for a flat period. No more open/close outlines posing as candles.
                var center = (openY + closeY) / 2f;
                var halfHeight = Math.Max(1.5f * UIScale, Math.Abs(closeY - openY) / 2f);
                handle.DrawRect(new UIBox2(
                    x - bodyHalf,
                    center - halfHeight,
                    x + bodyHalf,
                    center + halfHeight), color);
            }
        }
    }
}
