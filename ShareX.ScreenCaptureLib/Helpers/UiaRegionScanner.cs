#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Automation;

namespace ShareX.ScreenCaptureLib
{
    /// <summary>
    /// ShareXg: finds panels inside windows that have no child HWNDs (Electron/Chromium apps such as Claude desktop,
    /// VS Code, Slack, browsers) through UI Automation, so region capture can snap to a sidebar or chat pane.
    /// </summary>
    internal static class UiaRegionScanner
    {
        private const int MinWidth = 80, MinHeight = 60, MaxDepth = 12, MaxElements = 4000;

        private static readonly HashSet<ControlType> PanelTypes = new()
        {
            ControlType.Pane, ControlType.Group, ControlType.Document, ControlType.List, ControlType.Custom,
            ControlType.Tree, ControlType.ToolBar, ControlType.Tab, ControlType.Table, ControlType.DataGrid,
            ControlType.Edit, ControlType.Image, ControlType.MenuBar, ControlType.StatusBar, ControlType.Window
        };

        public static List<Rectangle> GetPanels(IntPtr hwnd, Rectangle windowRect, int budgetMs)
        {
            List<Rectangle> found = new();
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                TreeWalker walker = TreeWalker.ControlViewWalker;
                Stack<(AutomationElement el, int depth)> stack = new();
                stack.Push((root, 0));
                int visited = 0;

                while (stack.Count > 0 && sw.ElapsedMilliseconds < budgetMs && visited++ < MaxElements)
                {
                    (AutomationElement el, int depth) = stack.Pop();
                    System.Windows.Rect r = el.Current.BoundingRectangle;
                    if (r.IsEmpty || el.Current.IsOffscreen) continue;
                    Rectangle rect = Rectangle.Intersect(new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height), windowRect);
                    if (rect.Width < MinWidth || rect.Height < MinHeight) continue;

                    if (depth > 0 && PanelTypes.Contains(el.Current.ControlType) && !found.Contains(rect) && rect != windowRect)
                    {
                        found.Add(rect);
                    }

                    if (depth >= MaxDepth) continue;
                    for (AutomationElement? child = walker.GetFirstChild(el); child != null; child = walker.GetNextSibling(child))
                    {
                        stack.Push((child, depth + 1));
                    }
                }
            }
            catch
            {
            }

            // Smallest first: hovering picks the innermost panel under the cursor.
            return found.OrderBy(rc => rc.Width * rc.Height).ToList();
        }
    }
}
