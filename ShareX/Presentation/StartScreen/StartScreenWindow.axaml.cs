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

using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform;
using ShareX.AvaloniaUI.Theming;
using ShareX.HelpersLib;
using System;
using System.IO;
using System.Threading.Tasks;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = SkiaSharp.SKBitmap;

namespace ShareX;

public partial class StartScreenWindow : Window
{
    // Contact line on the welcome dialog.
    internal const string ContactEmail = "grayson@graysonchalmers.com";

    private readonly AvaloniaBitmap _logoBitmap;

    public StartScreenWindow()
    {
        InitializeComponent();

        using Stream logoStream = new MemoryStream(ShareXResources.IconBytes);
        _logoBitmap = new Avalonia.Media.Imaging.Bitmap(logoStream);
        LogoImage.Source = _logoBitmap;
        ContactText.Text = ContactEmail;
        VersionStamp.Text = $"v{ApplicationInfo.VersionText}";

        Opened += (_, _) => Activate();
        Closed += OnClosed;
    }

    // Proof shots: open the window far off-screen (never activated, no taskbar entry), render it, close it.
    public static async Task RenderToPngAsync(string path)
    {
        StartScreenWindow window = new()
        {
            WindowStartupLocation = WindowStartupLocation.Manual,
            Position = new PixelPoint(-20000, -20000),
            ShowActivated = false,
            ShowInTaskbar = false
        };
        window.Show();
        await Task.Delay(1500);
        DebugHelper.WriteLine($"RenderWelcome size {window.ClientSize} scaling {window.RenderScaling}");
        PixelSize pixels = PixelSize.FromSize(window.ClientSize, 2);
        using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(pixels, new Vector(192, 192));
        bitmap.Render(window);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        bitmap.Save(path);
        window.Close();
    }

    public Task ShowAsync()
    {
        TaskCompletionSource<bool> completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Closed += (_, _) => completion.TrySetResult(true);
        Show();
        return completion.Task;
    }

    private void OnTitlePointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e) => BeginMoveDrag(e);

    private void OnGetStartedClick(object? sender, RoutedEventArgs e) => Close();

    private void OnClosed(object? sender, EventArgs e)
    {
        _logoBitmap.Dispose();
        ApplicationState.Settings.ShowStartScreen = false;
        ApplicationState.Settings.ShowIconPackPrompt = false;
        SettingManager.SaveApplicationConfigAsync();
    }
}
