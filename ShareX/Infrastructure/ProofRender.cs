#nullable enable

using Avalonia;
using Avalonia.Controls;
using System.IO;
using System.Threading.Tasks;

namespace ShareX;

// ShareXg proof harness: show a window far off-screen (no focus, no taskbar entry), render it to PNG, close it.
internal static class ProofRender
{
    internal static async Task RenderAsync(Window window, string path)
    {
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Position = new PixelPoint(-20000, -20000);
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        await Task.Delay(2000);
        using Avalonia.Media.Imaging.RenderTargetBitmap bitmap = new(PixelSize.FromSize(window.ClientSize, 2), new Vector(192, 192));
        bitmap.Render(window);
        bitmap.Save(path);
        window.Hide();
    }
}
