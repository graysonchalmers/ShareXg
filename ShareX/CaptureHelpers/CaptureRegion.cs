#region License Information (GPL v3)

/*
    ShareX - A program that allows you to take screenshots and share any file type
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using ShareX.HelpersLib;
using ShareX.ScreenCaptureLib;
using ShareX.ScreenCaptureLib.Presentation.RegionCapture;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Drawing;
using System.Threading.Tasks;
using Bitmap = SkiaSharp.SKBitmap;

namespace ShareX
{
    public class CaptureRegion : CaptureBase
    {
        // Lets the region capture window disappear and the window's title bar finish fading to its active color, which
        // Windows starts when the region capture window closes. Shorter waits failed on Windows 11 because the two white
        // background captures differed.
        private const int TransparentCaptureDelay = 300;

        protected override TaskMetadata Execute(TaskSettings taskSettings)
        {
            return null;
        }

        protected override async Task<TaskMetadata> ExecuteAsync(TaskSettings taskSettings)
        {
            return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
        }

        protected async Task<TaskMetadata> ExecuteRegionCaptureAvaloniaAsync(TaskSettings taskSettings)
        {
            Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
            screenshot.CaptureCursor = false;

            bool activeMonitorMode = taskSettings.CaptureSettings.RegionCaptureOptions.ActiveMonitorMode;
            if (activeMonitorMode && taskSettings.CaptureSettings.RegionCaptureOptions.PerMonitorOverlay)
            {
                return await ExecutePerMonitorAsync(taskSettings, screenshot);
            }

            Rectangle screenBounds = activeMonitorMode
                ? CaptureHelpers.GetActiveScreenBounds()
                : CaptureHelpers.GetScreenBounds();

            // The capture is already a new bitmap owned by this request, so it is handed over without a copy.
            SKBitmap frozenScreenshot = activeMonitorMode
                ? screenshot.CaptureActiveMonitor()
                : screenshot.CaptureFullscreen();

            SKBitmap cursorBitmap = null;
            Point cursorPosition = Point.Empty;

            if (taskSettings.CaptureSettings.ShowCursor)
            {
                CursorData cursorData = new CursorData();
                if (cursorData.IsVisible)
                {
                    cursorBitmap = cursorData.ToBitmap(frozenScreenshot, screenBounds.Location);
                    cursorPosition = new Point(
                        cursorData.DrawPosition.X - screenBounds.X,
                        cursorData.DrawPosition.Y - screenBounds.Y);
                }
            }

            AvaloniaRegionCaptureRequest request = new AvaloniaRegionCaptureRequest
            {
                Screenshot = frozenScreenshot,
                // Screen captures are opaque: WindowsImageInterop.Capture and HDR correction set alpha to 255.
                ScreenshotIsOpaque = true,
                ScreenBounds = screenBounds,
                RegionCaptureOptions = taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                ImageEditorOptions = taskSettings.ToolsSettingsReference.ImageEditorOptions,
                EnableAnnotations = !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation,
                AllowMultipleRegions = true,
                CursorBitmap = cursorBitmap,
                CursorPosition = cursorPosition,
                FollowCursorMonitor = activeMonitorMode
            };

            ScreenCaptureLib.Presentation.RegionCapture.RegionCaptureWindow.MonitorSwitchRequested = false;
            AvaloniaRegionCaptureResult result = await RegionCaptureIntegration.CaptureAsync(request);
            if (result == null)
            {
                // ShareXg: the cursor moved to another monitor; capture again there.
                if (ScreenCaptureLib.Presentation.RegionCapture.RegionCaptureWindow.MonitorSwitchRequested && activeMonitorMode)
                {
                    ScreenCaptureLib.Presentation.RegionCapture.RegionCaptureWindow.MonitorSwitchRequested = false;
                    return await ExecuteRegionCaptureAvaloniaAsync(taskSettings);
                }

                return null;
            }

            return await BuildMetadataAsync(taskSettings, result);
        }

        // ShareXg: one overlay per monitor, all open at once (RegionCaptureOptions.PerMonitorOverlay).
        private async Task<TaskMetadata> ExecutePerMonitorAsync(TaskSettings taskSettings, Screenshot screenshot)
        {
            Rectangle virtualBounds = CaptureHelpers.GetScreenBounds();
            Point cursor = CaptureHelpers.GetCursorPosition();
            List<AvaloniaRegionCaptureRequest> requests = new();

            using (Bitmap full = screenshot.CaptureFullscreen())
            {
                CursorData cursorData = taskSettings.CaptureSettings.ShowCursor ? new CursorData() : null;

                foreach (Rectangle bounds in DesktopScreen.AllScreens.Select(x => x.Bounds))
                {
                    bool primary = bounds.Contains(cursor);
                    Bitmap slice = SliceMonitor(full, virtualBounds, bounds);

                    Bitmap cursorBitmap = null;
                    Point cursorPosition = Point.Empty;
                    if (primary && cursorData != null && cursorData.IsVisible)
                    {
                        cursorBitmap = cursorData.ToBitmap(slice, bounds.Location);
                        cursorPosition = new Point(cursorData.DrawPosition.X - bounds.X, cursorData.DrawPosition.Y - bounds.Y);
                    }

                    requests.Add(new AvaloniaRegionCaptureRequest
                    {
                        Screenshot = slice,
                        ScreenshotIsOpaque = true,
                        ScreenBounds = bounds,
                        RegionCaptureOptions = taskSettings.CaptureSettingsReference.RegionCaptureOptions,
                        ImageEditorOptions = taskSettings.ToolsSettingsReference.ImageEditorOptions,
                        EnableAnnotations = !taskSettings.AdvancedSettings.RegionCaptureDisableAnnotation,
                        AllowMultipleRegions = true,
                        CursorBitmap = cursorBitmap,
                        CursorPosition = cursorPosition,
                        IsPrimaryOverlay = primary
                    });
                }
            }

            AvaloniaRegionCaptureResult result = await RegionCaptureIntegration.CaptureMultiAsync(requests);
            return result == null ? null : await BuildMetadataAsync(taskSettings, result);
        }

        /// <summary>ShareXg: an independent copy of one monitor's pixels (each overlay disposes its own).</summary>
        internal static Bitmap SliceMonitor(Bitmap full, Rectangle virtualBounds, Rectangle bounds)
        {
            Bitmap slice = new Bitmap(bounds.Width, bounds.Height, full.ColorType, full.AlphaType);
            using (SKCanvas canvas = new SKCanvas(slice))
            {
                canvas.DrawBitmap(full, -(bounds.X - virtualBounds.X), -(bounds.Y - virtualBounds.Y));
            }

            return slice;
        }

        private async Task<TaskMetadata> BuildMetadataAsync(TaskSettings taskSettings, AvaloniaRegionCaptureResult result)
        {
            using (result.Image)
            {
                Bitmap output = null;

                if (result.WindowHandle != IntPtr.Zero && taskSettings.CaptureSettings.CaptureTransparent)
                {
                    output = await CaptureWindowTransparentAsync(taskSettings, result);
                }

                output ??= result.Image.Copy();
                TaskMetadata metadata = new TaskMetadata(output);

                if (result.ImageModified)
                {
                    AllowAnnotation = false;
                }

                if (result.WindowInfo != null)
                {
                    metadata.UpdateInfo(result.WindowInfo);
                }

                return metadata;
            }
        }

        /// <summary>
        /// When a detected window was captured whole, gives it the rounded corners and, with "Capture window with
        /// shadow", the shadow that window capture has with "Capture window with transparency". The pixels inside the
        /// window, annotations included, still come from the region capture. Returns null if that isn't possible.
        /// </summary>
        private static async Task<Bitmap> CaptureWindowTransparentAsync(TaskSettings taskSettings, AvaloniaRegionCaptureResult result)
        {
            await Task.Delay(TransparentCaptureDelay);

            try
            {
                Screenshot screenshot = TaskHelpers.GetScreenshot(taskSettings);
                screenshot.CaptureCursor = false;
                return screenshot.CaptureWindowTransparent(result.WindowHandle, result.Image, result.ScreenRectangle);
            }
            catch (Exception e)
            {
                DebugHelper.WriteException(e, "Transparent window capture failed.");
                return null;
            }
        }
    }
}
