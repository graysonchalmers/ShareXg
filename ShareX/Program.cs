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

#nullable enable

using ShareX.AvaloniaUI.Integration;
using ShareX.HelpersLib;
using ShareX.ImageEditor.Integration;
using System;
using System.Diagnostics;
using System.Threading.Tasks;

#if MicrosoftStore
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;
#endif

namespace ShareX;

internal static class Program
{
    private static Stopwatch _startTimer = null!;

    [STAThread]
    private static void Main(string[] args)
    {
        ApplicationDiagnostics.Initialize();
        _startTimer = Stopwatch.StartNew();
        ApplicationCommandLine.Initialize(args);

#if STEAM
        if (ApplicationCommandLine.TryHandleUninstall())
        {
            return;
        }
#endif

        SystemOptions.UpdateSystemOptions();
        PersonalPathManager.Initialize();
        DebugHelper.Init(AppPaths.LogsFilePath);

        StartupOptions.IsAdmin = Helpers.IsAdministrator();
        StartupOptions.MultiInstance = ApplicationCommandLine.IsCommandPresent("multi", "m");

        using (SingleInstanceManager singleInstanceManager = new(
            ApplicationInfo.MutexName, ApplicationInfo.PipeName, !StartupOptions.MultiInstance, args))
        {
            if (!singleInstanceManager.IsSingleInstance || singleInstanceManager.IsFirstInstance)
            {
                singleInstanceManager.ArgumentsReceived += SingleInstanceCommandRouter.ArgumentsReceived;
                Run(args);
            }
        }

        ApplicationLifecycle.RestartIfRequested();
        DebugHelper.Flush();
    }

    private static void Run(string[] args)
    {
        DebugHelper.WriteLine("ShareX starting.");
        DebugHelper.WriteLine("Version: " + ApplicationInfo.VersionText);
        DebugHelper.WriteLine("Build: " + ApplicationInfo.Build);
        DebugHelper.WriteLine("Command line: " + Environment.CommandLine);
        DebugHelper.WriteLine("Personal path: " + AppPaths.PersonalFolder);
        if (!string.IsNullOrEmpty(PersonalPathManager.DetectionMethod))
        {
            DebugHelper.WriteLine("Personal path detection method: " + PersonalPathManager.DetectionMethod);
        }
        DebugHelper.WriteLine("Operating system: " + Helpers.GetOperatingSystemProductName(true));
        DebugHelper.WriteLine(".NET version: " + Environment.Version);
        DebugHelper.WriteLine("Running as elevated process: " + StartupOptions.IsAdmin);

        StartupOptions.SilentRun = ApplicationCommandLine.IsCommandPresent("silent", "s");
#if MicrosoftStore
        StartupOptions.SilentRun = StartupOptions.SilentRun || AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;
#endif
        StartupOptions.IgnoreHotkeyWarning = ApplicationCommandLine.IsCommandPresent("NoHotkeys");

        PersonalPathManager.CreateApplicationFolders();
        FileTypeRegistration.RegisterMissingExtensions();
        ApplicationDiagnostics.WriteStartupFlags();

        DebugHelper.WriteLine("Avalonia application initializing.");
        AvaloniaBootstrapper.Initialize(args, StartApplicationAsync, ApplicationLifecycle.OnAvaloniaStopped);
        ApplicationDiagnostics.InitializeAvalonia();

        SettingManager.LoadInitialSettings();
        ApplicationState.UpdateManager = new ShareXUpdateManager();
        LanguageHelper.ChangeLanguage(ApplicationState.Settings.Language);
        CleanupManager.CleanupAsync();

        DebugHelper.WriteLine("Avalonia application starting.");
        AvaloniaBootstrapper.Run();

        ApplicationLifecycle.CloseSequence();
    }

    private static async Task StartApplicationAsync()
    {
        ImageEditorIntegration.Initialize();

        // ShareXg proof harness: render the welcome dialog off-screen to a PNG and exit.
        string? renderPath = ApplicationCommandLine.GetParameterText("RenderWelcome");
        if (!string.IsNullOrEmpty(renderPath))
        {
            await StartScreenWindow.RenderToPngAsync(renderPath);
            Environment.Exit(0);
        }

        // ShareXg proof harness: render the markup editor (fresh default options) and its More list off-screen.
        string? editorDir = ApplicationCommandLine.GetParameterText("RenderEditor");
        if (!string.IsNullOrEmpty(editorDir))
        {
            System.IO.Directory.CreateDirectory(editorDir);
            string log = System.IO.Path.Combine(editorDir, "render-log.txt");
            void Crumb(string m) => System.IO.File.AppendAllText(log, DateTime.Now.ToString("HH:mm:ss ") + m + Environment.NewLine);
            _ = Task.Run(async () => { await Task.Delay(45000); Crumb("watchdog kill"); Environment.Exit(3); });
            try
            {
            ShareX.ImageEditor.Integration.ImageEditorOptions editorOptions = new() { RememberWindowState = false, AutoSizeWindow = false, ShowExitConfirmation = false };
            Crumb("options");
            ShareX.ImageEditor.Presentation.Views.EditorWindow editor = new(editorOptions) { Width = 1100, Height = 640 };
            using SkiaSharp.SKBitmap sample = new(900, 480);
            using (SkiaSharp.SKCanvas canvas = new(sample)) { canvas.Clear(new SkiaSharp.SKColor(60, 70, 90)); }
            Crumb("ctor");
            editor.LoadImage(sample);
            Crumb("loaded");
            await ProofRender.RenderAsync(editor, System.IO.Path.Combine(editorDir, "editor.png"));
            if (editor.DataContext is ShareX.ImageEditor.Presentation.ViewModels.MainViewModel vm)
            {
                Avalonia.Controls.StackPanel list = new() { Margin = new Avalonia.Thickness(12), Spacing = 4 };
                list.Children.Add(new Avalonia.Controls.TextBlock { Text = "More ▾", FontWeight = Avalonia.Media.FontWeight.Bold });
                foreach (var item in vm.HiddenToolbarItems) list.Children.Add(new Avalonia.Controls.TextBlock { Text = "  " + item.Name });
                list.Children.Add(new Avalonia.Controls.TextBlock { Text = $"Shadow default: {(editorOptions.Shadow ? "ON" : "off")}" });
                Avalonia.Controls.Window moreWindow = new() { Content = list, SizeToContent = Avalonia.Controls.SizeToContent.WidthAndHeight };
                await ProofRender.RenderAsync(moreWindow, System.IO.Path.Combine(editorDir, "editor-more.png"));
            }
            Crumb("done");
            }
            catch (Exception ex) { Crumb("error " + ex); }
            Environment.Exit(0);
        }

        // ShareXg proof harness: per-monitor region overlays. For each monitor, prints the overlay bounds and scaling and
        // writes its slice of a synthetic desktop (no real screen content) at 1:1 physical pixels. Nothing is shown on screen.
        // ShareXg live-test harness: open the region overlays, then exit after the given seconds (default 12).
        string? testOverlay = ApplicationCommandLine.GetParameterText("TestRegionOverlay");
        if (ApplicationCommandLine.IsCommandPresent("TestRegionOverlay"))
        {
            int seconds = int.TryParse(testOverlay, out int s) && s > 0 ? s : 12;
            _ = Task.Run(async () => { await Task.Delay(seconds * 1000); Environment.Exit(0); });
            await CaptureRegion.RunTestCaptureAsync();
            Environment.Exit(0);
        }

        string? perMonitorDir = ApplicationCommandLine.GetParameterText("RenderPerMonitor");
        if (!string.IsNullOrEmpty(perMonitorDir))
        {
            System.IO.Directory.CreateDirectory(perMonitorDir);
            System.Collections.Generic.List<string> lines = new();
            System.Drawing.Rectangle virtualBounds = CaptureHelpers.GetScreenBounds();
            lines.Add($"virtual {virtualBounds}");
            Avalonia.Controls.Window probe = new();
            DesktopScreen[] screens = DesktopScreen.AllScreens;
            using SkiaSharp.SKBitmap desktop = new(virtualBounds.Width, virtualBounds.Height);
            using (SkiaSharp.SKCanvas canvas = new(desktop))
            {
                canvas.Clear(new SkiaSharp.SKColor(40, 44, 52));
                using SkiaSharp.SKPaint grid = new() { Color = new SkiaSharp.SKColor(90, 100, 120), StrokeWidth = 1 };
                for (int x = 0; x < virtualBounds.Width; x += 100) canvas.DrawLine(x, 0, x, virtualBounds.Height, grid);
                for (int y = 0; y < virtualBounds.Height; y += 100) canvas.DrawLine(0, y, virtualBounds.Width, y, grid);
                using SkiaSharp.SKPaint edge = new() { Color = new SkiaSharp.SKColor(255, 140, 0), StrokeWidth = 8, IsStroke = true };
                using SkiaSharp.SKFont font = new() { Size = 64 };
                using SkiaSharp.SKPaint text = new() { Color = SkiaSharp.SKColors.White };
                for (int i = 0; i < screens.Length; i++)
                {
                    System.Drawing.Rectangle b = screens[i].Bounds;
                    float ox = b.X - virtualBounds.X, oy = b.Y - virtualBounds.Y;
                    canvas.DrawRect(ox + 4, oy + 4, b.Width - 8, b.Height - 8, edge);
                    canvas.DrawText($"Monitor {i + 1}  {b.Width}x{b.Height} @ {b.X},{b.Y}", ox + 60, oy + 120, font, text);
                }
            }

            for (int i = 0; i < screens.Length; i++)
            {
                System.Drawing.Rectangle b = screens[i].Bounds;
                double scaling = probe.Screens.ScreenFromPoint(new Avalonia.PixelPoint(b.X + b.Width / 2, b.Y + b.Height / 2))?.Scaling ?? 1;
                using SkiaSharp.SKBitmap slice = CaptureRegion.SliceMonitor(desktop, virtualBounds, b);
                bool primary = b.Contains(CaptureHelpers.GetCursorPosition());
                lines.Add($"monitor {i + 1}: bounds {b}, scaling {scaling:0.##} ({scaling * 96:0} dpi), overlay {b.Width / scaling:0.#}x{b.Height / scaling:0.#} DIP, slice {slice.Width}x{slice.Height} px, cursor here {primary}");
                using System.IO.FileStream fs = System.IO.File.Create(System.IO.Path.Combine(perMonitorDir, $"monitor-{i + 1}.png"));
                slice.Encode(fs, SkiaSharp.SKEncodedImageFormat.Png, 100);
            }

            System.IO.File.WriteAllLines(System.IO.Path.Combine(perMonitorDir, "monitors.txt"), lines);
            Environment.Exit(0);
        }

        if (ApplicationState.Settings.ShowStartScreen && !StartupOptions.Sandbox)
        {
            DebugHelper.WriteLine("Start screen opening.");
            StartScreenWindow startScreen = new();
            await startScreen.ShowAsync();
            DebugHelper.WriteLine("Start screen closed.");
        }

        // ShareXg: take PrtSc from Snipping Tool while running; restored on exit or crash.
        if (!StartupOptions.Sandbox && ApplicationState.Settings.TakeOverPrintScreen) PrintScreenGuard.Engage();

        DebugHelper.WriteLine("Hotkey host init started.");
        MainForm hotkeyHost = new();
        ApplicationLifecycle.AttachHost(hotkeyHost);
        hotkeyHost.Initialize();

        await ApplicationSettingsRuntime.InitializeAsync(hotkeyHost);

        bool showMainWindow = !(StartupOptions.SilentRun || ApplicationState.Settings.SilentRun) ||
            !ApplicationState.Settings.ShowTray;
        MainWindowIntegration.Initialize(hotkeyHost.TrayIconService, showMainWindow);
        SingleInstanceCommandRouter.MarkReady();

        // ShareXg proof harness: render the main window and tray menu off-screen, then exit.
        string? renderDir = ApplicationCommandLine.GetParameterText("RenderMain");
        if (!string.IsNullOrEmpty(renderDir) && MainWindowIntegration.Instance is MainWindow mw)
        {
            System.IO.Directory.CreateDirectory(renderDir);
            await ProofRender.RenderAsync(mw, System.IO.Path.Combine(renderDir, "main-window.png"));
            Avalonia.Controls.Window menuWindow = new() { Content = mw.CreateTrayMenuPreview(), SizeToContent = Avalonia.Controls.SizeToContent.WidthAndHeight };
            await ProofRender.RenderAsync(menuWindow, System.IO.Path.Combine(renderDir, "tray-menu.png"));
            Environment.Exit(0);
        }

        ShareX.Tools.MouseHighlighterManager.ActivateOnStartup(
            ApplicationState.DefaultTaskSettings.ToolsSettings.MouseHighlighterOptions);

        if (showMainWindow)
        {
            MainWindowIntegration.Activate();
        }

        IconPackPromptWindow.ShowOnceWithMainWindow();

        DebugHelper.WriteLine("Startup time: {0} ms", _startTimer.ElapsedMilliseconds);
        await ApplicationCommandLine.ExecuteInitialAsync();

        if (ApplicationState.Settings.ActionsToolbarRunAtStartup)
        {
            TaskHelpers.OpenActionsToolbar();
        }

        DebugHelper.WriteLine("Hotkey host init finished.");
    }
}
