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
using ShareX.Localization;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using MessageBox = ShareX.AvaloniaUI.MessageBox;
using MessageBoxButtons = ShareX.AvaloniaUI.MessageBoxButtons;
using MessageBoxIcon = ShareX.AvaloniaUI.MessageBoxIcon;

namespace ShareX
{
    public class HotkeyManager
    {
        public List<HotkeySettings> Hotkeys { get; private set; }
        public bool IgnoreHotkeys { get; set; }

        public delegate void HotkeyTriggerEventHandler(HotkeySettings hotkeySetting);
        public delegate void HotkeysToggledEventHandler(bool hotkeysEnabled);

        public HotkeyTriggerEventHandler HotkeyTrigger;
        public HotkeysToggledEventHandler HotkeysToggledTrigger;

        private IHotkeyHost hotkeyHost;

        public HotkeyManager(IHotkeyHost host)
        {
            hotkeyHost = host;
            hotkeyHost.HotkeyPress += OnHotkeyPressed;
            hotkeyHost.Closed += OnHostClosed;

            if (ApplicationState.Settings.TakeOverPrintScreen && !StartupOptions.Sandbox)
            {
                InstallPrintScreenHook();
            }
        }

        // ShareXg: Windows' Snipping Tool grabs PrtSc with its own low-level hook, which a registry change alone
        // doesn't stop until sign-out. A low-level hook installed later runs first, so we catch PrtSc here, fire the
        // matching ShareXg hotkey, and swallow the key. Windows silently drops a low-level hook whose callback times out,
        // so the hook is re-installed on unlock, on resume and every 10 minutes; one hook per process, removed on exit.
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc printScreenProc; // referenced here so the callback is never collected
        private static IntPtr printScreenHook;
        private static Avalonia.Threading.DispatcherTimer printScreenTimer;
        private static bool printScreenDown; // a PrtSc key-down was acted on and its key-up has not arrived yet

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);
        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        // Runs on the UI thread, which owns the hook and pumps its callbacks.
        private void InstallPrintScreenHook()
        {
            RemovePrintScreenHook(); // ShareXg: never two hooks, even if a second manager is created
            printScreenProc = PrintScreenHookProc;
            RefreshPrintScreenHook();

            if (printScreenHook != IntPtr.Zero)
            {
                DebugHelper.WriteLine("PrintScreen hook installed.");
            }

            Microsoft.Win32.SystemEvents.SessionSwitch += OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged += OnPowerModeChanged;
            printScreenTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromMinutes(10) };
            printScreenTimer.Tick += (sender, e) => RefreshPrintScreenHook();
            printScreenTimer.Start();
        }

        // ShareXg: unhook and hook again; also puts ShareXg back in front of a hook Snipping Tool installed later.
        private static void RefreshPrintScreenHook()
        {
            if (printScreenProc == null)
            {
                return; // removed while a refresh was queued
            }

            if (printScreenHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(printScreenHook);
            }

            printScreenDown = false; // a key-up may have been missed while the hook was gone
            printScreenHook = SetWindowsHookEx(13 /* WH_KEYBOARD_LL */, printScreenProc, GetModuleHandle(null), 0);

            if (printScreenHook == IntPtr.Zero)
            {
                DebugHelper.WriteLine("PrintScreen hook failed: " + Marshal.GetLastWin32Error());
            }
        }

        private static void RemovePrintScreenHook()
        {
            Microsoft.Win32.SystemEvents.SessionSwitch -= OnSessionSwitch;
            Microsoft.Win32.SystemEvents.PowerModeChanged -= OnPowerModeChanged;

            if (printScreenTimer != null)
            {
                printScreenTimer.Stop();
                printScreenTimer = null;
            }

            if (printScreenHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(printScreenHook);
                printScreenHook = IntPtr.Zero;
            }

            printScreenProc = null;
        }

        private static void OnSessionSwitch(object sender, Microsoft.Win32.SessionSwitchEventArgs e)
        {
            if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshPrintScreenHook());
            }
        }

        private static void OnPowerModeChanged(object sender, Microsoft.Win32.PowerModeChangedEventArgs e)
        {
            if (e.Mode == Microsoft.Win32.PowerModes.Resume)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(() => RefreshPrintScreenHook());
            }
        }

        private IntPtr PrintScreenHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0 && Marshal.ReadInt32(lParam) == 0x2C /* VK_SNAPSHOT */)
            {
                int msg = wParam.ToInt32();
                bool keyDown = msg == 0x0100 || msg == 0x0104; // WM_KEYDOWN, WM_SYSKEYDOWN

                // ShareXg: holding PrtSc auto-repeats key-down; act on the first one, swallow the repeats and its key-up.
                if (printScreenDown)
                {
                    if (!keyDown)
                    {
                        printScreenDown = false;
                    }

                    return (IntPtr)1;
                }

                if (Hotkeys != null && !IgnoreHotkeys)
                {
                    static bool Down(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;
                    bool ctrl = Down(0x11), shift = Down(0x10), alt = Down(0x12), win = Down(0x5B) || Down(0x5C);

                    HotkeySettings match = Hotkeys.Find(h => h.HotkeyInfo.KeyCode == InputKey.PrintScreen &&
                        h.HotkeyInfo.Control == ctrl && h.HotkeyInfo.Shift == shift && h.HotkeyInfo.Alt == alt && h.HotkeyInfo.Win == win);

                    if (match != null)
                    {
                        if (keyDown)
                        {
                            printScreenDown = true;

                            // ShareXg: same fullscreen rule as OnHotkeyPressed, checked after the callback returns.
                            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                            {
                                if (!ApplicationState.Settings.DisableHotkeysOnFullscreen || !CaptureHelpers.IsActiveWindowFullscreen())
                                {
                                    OnHotkeyTrigger(match);
                                }
                            });
                        }

                        return (IntPtr)1; // swallow down and up so Snipping Tool never sees it
                    }
                }
            }

            return CallNextHookEx(printScreenHook, nCode, wParam, lParam);
        }

        private void OnHotkeyPressed(ushort id, InputKey key, Modifiers modifier)
        {
            if (!IgnoreHotkeys && (!ApplicationState.Settings.DisableHotkeysOnFullscreen || !CaptureHelpers.IsActiveWindowFullscreen()))
            {
                HotkeySettings hotkeySetting = Hotkeys.Find(x => x.HotkeyInfo.ID == id);

                if (hotkeySetting != null)
                {
                    OnHotkeyTrigger(hotkeySetting);
                }
            }
        }

        private void OnHostClosed(object sender, EventArgs e)
        {
            RemovePrintScreenHook(); // ShareXg

            if (hotkeyHost != null && !hotkeyHost.IsDisposed)
            {
                UnregisterAllHotkeys(false);
            }
        }

        public void UpdateHotkeys(List<HotkeySettings> hotkeys, bool showFailedHotkeys)
        {
            if (Hotkeys != null)
            {
                UnregisterAllHotkeys();
            }

            Hotkeys = hotkeys;

            RegisterAllHotkeys();

            if (showFailedHotkeys)
            {
                ShowFailedHotkeys();
            }
        }

        protected void OnHotkeyTrigger(HotkeySettings hotkeySetting)
        {
            HotkeyTrigger?.Invoke(hotkeySetting);
        }

        public void RegisterHotkey(HotkeySettings hotkeySetting)
        {
            if (!ApplicationState.Settings.DisableHotkeys || hotkeySetting.TaskSettings.Job == HotkeyType.DisableHotkeys)
            {
                UnregisterHotkey(hotkeySetting, false);

                if (hotkeySetting.HotkeyInfo.Status != HotkeyStatus.Registered && hotkeySetting.HotkeyInfo.IsValidHotkey)
                {
                    hotkeyHost.RegisterHotkey(hotkeySetting.HotkeyInfo);

                    if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
                    {
                        DebugHelper.WriteLine("Hotkey registered: " + hotkeySetting);
                    }
                    else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                    {
                        DebugHelper.WriteLine("Hotkey register failed: " + hotkeySetting);
                    }
                }
                else
                {
                    hotkeySetting.HotkeyInfo.Status = HotkeyStatus.NotConfigured;
                }
            }

            if (!Hotkeys.Contains(hotkeySetting))
            {
                Hotkeys.Add(hotkeySetting);
            }
        }

        public void RegisterAllHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void RegisterFailedHotkeys()
        {
            foreach (HotkeySettings hotkeySetting in Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed))
            {
                RegisterHotkey(hotkeySetting);
            }
        }

        public void UnregisterHotkey(HotkeySettings hotkeySetting, bool removeFromList = true)
        {
            if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Registered)
            {
                hotkeyHost.UnregisterHotkey(hotkeySetting.HotkeyInfo);

                if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.NotConfigured)
                {
                    DebugHelper.WriteLine("Hotkey unregistered: " + hotkeySetting);
                }
                else if (hotkeySetting.HotkeyInfo.Status == HotkeyStatus.Failed)
                {
                    DebugHelper.WriteLine("Hotkey unregister failed: " + hotkeySetting);
                }
            }

            if (removeFromList)
            {
                Hotkeys.Remove(hotkeySetting);
            }
        }

        public void UnregisterAllHotkeys(bool removeFromList = true, bool temporary = false)
        {
            if (Hotkeys != null)
            {
                foreach (HotkeySettings hotkeySetting in Hotkeys.ToArray())
                {
                    if (!temporary || hotkeySetting.TaskSettings.Job != HotkeyType.DisableHotkeys)
                    {
                        UnregisterHotkey(hotkeySetting, removeFromList);
                    }
                }
            }
        }

        public void ToggleHotkeys(bool hotkeysDisabled)
        {
            if (!hotkeysDisabled)
            {
                RegisterAllHotkeys();
            }
            else
            {
                UnregisterAllHotkeys(false, true);
            }

            HotkeysToggledTrigger?.Invoke(hotkeysDisabled);
        }

        public void ShowFailedHotkeys()
        {
            List<HotkeySettings> failedHotkeysList = Hotkeys.Where(x => x.HotkeyInfo.Status == HotkeyStatus.Failed).ToList();

            if (failedHotkeysList.Count > 0)
            {
                string failedHotkeys = string.Join("\r\n", failedHotkeysList.Select(x => $"[{x.HotkeyInfo}] {x.TaskSettings}"));
                string hotkeyText = failedHotkeysList.Count > 1 ? Strings.HotkeyManager_ShowFailedHotkeys_hotkeys : Strings.HotkeyManager_ShowFailedHotkeys_hotkey;
                string text = string.Format(Strings.HotkeyManager_ShowFailedHotkeys_Unable_to_register_hotkey, hotkeyText, failedHotkeys);

                MessageBox.Show(text, "ShareX - " + Strings.HotkeyManager_ShowFailedHotkeys_Hotkey_registration_failed, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        public void ResetHotkeys()
        {
            UnregisterAllHotkeys();
            Hotkeys.AddRange(GetDefaultHotkeyList());
            RegisterAllHotkeys();

            if (ApplicationState.Settings.DisableHotkeys)
            {
                TaskHelpers.ToggleHotkeys();
            }
        }

        public static List<HotkeySettings> GetDefaultHotkeyList()
        {
            return new List<HotkeySettings>
            {
                new HotkeySettings(HotkeyType.RectangleRegion, InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.PrintScreen, InputKey.Control | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ActiveWindow, InputKey.Alt | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ScreenRecorder, InputKey.Shift | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.ScreenRecorderGIF, InputKey.Control | InputKey.Shift | InputKey.PrintScreen),
                new HotkeySettings(HotkeyType.OCR, InputKey.Control | InputKey.Shift | InputKey.T)
            };
        }
    }
}
