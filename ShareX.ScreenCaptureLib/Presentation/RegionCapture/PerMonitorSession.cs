#nullable enable

using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace ShareX.ScreenCaptureLib.Presentation.RegionCapture;

/// <summary>
/// ShareXg: state shared by the per-monitor overlays of one capture. Holds the full virtual-desktop screenshot and a
/// rectangle selection in virtual-screen physical pixels that can span monitors; each overlay draws its own part.
/// </summary>
public sealed class PerMonitorSession : IDisposable
{
    public PerMonitorSession(SKBitmap full, Rectangle virtualBounds)
    {
        Full = full;
        VirtualBounds = virtualBounds;
    }

    public SKBitmap Full { get; }

    public Rectangle VirtualBounds { get; }

    /// <summary>The spanning selection while a drag has left its starting monitor, in virtual-screen pixels.</summary>
    public Rectangle? Span { get; private set; }

    /// <summary>Set when a spanning selection was confirmed; the capture result for the whole session.</summary>
    public AvaloniaRegionCaptureResult? SpanResult { get; private set; }

    internal List<RegionCaptureWindow> Windows { get; } = new();

    /// <summary>The part of <paramref name="span"/> on <paramref name="monitor"/>, in that monitor's local pixels.</summary>
    public static Rectangle LocalPart(Rectangle span, Rectangle monitor)
    {
        Rectangle part = Rectangle.Intersect(span, monitor);
        return part.IsEmpty ? Rectangle.Empty : new Rectangle(part.X - monitor.X, part.Y - monitor.Y, part.Width, part.Height);
    }

    /// <summary>Copies <paramref name="span"/> out of the full virtual-desktop screenshot.</summary>
    public static SKBitmap Crop(SKBitmap full, Rectangle virtualBounds, Rectangle span)
    {
        SKBitmap image = new(span.Width, span.Height, full.ColorType, full.AlphaType);
        using SKCanvas canvas = new(image);
        canvas.DrawBitmap(full, -(span.X - virtualBounds.X), -(span.Y - virtualBounds.Y));
        return image;
    }

    public static Rectangle FromCorners(Point a, Point b) =>
        Rectangle.FromLTRB(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y));

    /// <summary>
    /// One step of a drag, from the global cursor and left-button state (not from pointer events, which stop reaching
    /// the starting overlay once the cursor is over another overlay). Returns true when the drag spans monitors.
    /// </summary>
    public bool Track(Rectangle startMonitor, Point start, Point cursor, bool buttonDown)
    {
        if (!buttonDown)
        {
            if (Span != null)
            {
                CompleteSpan();
                return true;
            }

            return false;
        }

        if (Span == null && startMonitor.Contains(cursor))
        {
            return false;
        }

        UpdateSpan(FromCorners(start, cursor));
        return true;
    }

    internal void UpdateSpan(Rectangle span)
    {
        Span = span;
        foreach (RegionCaptureWindow window in Windows)
        {
            window.ShowSpanPart(span);
        }
    }

    internal void CompleteSpan()
    {
        if (Span is { Width: > 1, Height: > 1 } span)
        {
            span.Intersect(VirtualBounds);
            SpanResult = new AvaloniaRegionCaptureResult(Crop(Full, VirtualBounds, span), span, null, false);
        }

        foreach (RegionCaptureWindow window in Windows.ToArray())
        {
            window.CloseWithoutResult();
        }
    }

    private static readonly string LogPath = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareXg", "span-log.txt");

    /// <summary>ShareXg debug: drag trace, only written while the span-log.txt file exists.</summary>
    internal static void Log(string line)
    {
        try
        {
            if (System.IO.File.Exists(LogPath))
            {
                System.IO.File.AppendAllText(LogPath, $"{DateTime.Now:HH:mm:ss.fff} {line}{Environment.NewLine}");
            }
        }
        catch
        {
        }
    }

    public void Dispose() => Full.Dispose();
}
