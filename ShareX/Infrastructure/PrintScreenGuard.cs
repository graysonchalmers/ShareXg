#nullable enable
using Microsoft.Win32;
using ShareX.HelpersLib;
using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ShareX;

/// <summary>
/// ShareXg: turns off Windows' "Print Screen opens Snipping Tool" while ShareXg runs, so PrtSc reaches ShareXg.
/// The original value is saved to a file and restored on exit, by a hidden watcher process if ShareXg crashes or
/// is killed, and on the next start if even that failed.
/// </summary>
internal static class PrintScreenGuard
{
    private const string KeyPath = @"Control Panel\Keyboard";
    private const string ValueName = "PrintScreenKeyForSnippingEnabled";
    private const string Missing = "missing";

    private static string StateFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ShareXg", "prtsc-original.txt");

    internal static void Engage()
    {
        try
        {
            RestoreIfPending(); // a previous run died before restoring
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            object? current = key.GetValue(ValueName);
            Directory.CreateDirectory(Path.GetDirectoryName(StateFile)!);
            File.WriteAllText(StateFile, current?.ToString() ?? Missing);
            key.SetValue(ValueName, 0, RegistryValueKind.DWord);
            Broadcast();
            StartWatcher();
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreIfPending();
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
        }
    }

    internal static void RestoreIfPending()
    {
        try
        {
            if (!File.Exists(StateFile)) return;
            string original = File.ReadAllText(StateFile).Trim();
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(KeyPath);
            if (original == Missing) key.DeleteValue(ValueName, false);
            else key.SetValue(ValueName, int.TryParse(original, out int v) ? v : 1, RegistryValueKind.DWord);
            File.Delete(StateFile);
            Broadcast();
        }
        catch (Exception e)
        {
            DebugHelper.WriteException(e);
        }
    }

    // Hidden PowerShell that waits for this process to end, then restores the value. Covers crashes and Task Manager kills.
    private static void StartWatcher()
    {
        string f = StateFile.Replace("'", "''");
        string script =
            $"Wait-Process -Id {Environment.ProcessId} -ErrorAction SilentlyContinue; " +
            $"if (Test-Path '{f}') {{ $o = (Get-Content '{f}').Trim(); " +
            $"if ($o -eq '{Missing}') {{ Remove-ItemProperty 'HKCU:/Control Panel/Keyboard' -Name {ValueName} -ErrorAction SilentlyContinue }} " +
            $"else {{ Set-ItemProperty 'HKCU:/Control Panel/Keyboard' -Name {ValueName} -Value ([int]$o) -Type DWord }}; Remove-Item '{f}' }}";
        Process.Start(new ProcessStartInfo("conhost.exe", $"--headless powershell.exe -NoProfile -WindowStyle Hidden -Command \"{script.Replace("\"", "\\\"")}\"")
        {
            UseShellExecute = false,
            CreateNoWindow = true
        });
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, string lParam, uint flags, uint timeout, out IntPtr result);

    private static void Broadcast()
    {
        SendMessageTimeout((IntPtr)0xffff, 0x001A, IntPtr.Zero, "Keyboard", 0x0002, 1000, out _);
    }
}
