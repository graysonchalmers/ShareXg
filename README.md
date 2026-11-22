# ShareXg

A slim remix of [ShareX](https://github.com/ShareX/ShareX) for Windows. It keeps five verbs in front: **screenshot, markup, text grab (OCR), GIF and short clip**. Everything else ShareX does stays one click away under **More**. Nothing is removed.

> **Personal fork, shared under GPL-3.** ShareXg is not affiliated with or endorsed by the ShareX team, and it comes with no support or roadmap promises. If you want ShareX itself, get it from [getsharex.com](https://getsharex.com).

## What's different from ShareX

- **One key.** PrtSc captures a region and Ctrl+PrtSc the full screen. While ShareXg runs it takes PrtSc over from the Windows Snipping Tool, and hands it back when ShareXg exits.
- **Multi-monitor that works.** Each monitor gets its own region overlay at its native scaling. You can drag one region across monitors, and overlays fix themselves on mixed-DPI setups.
- **Snaps inside apps.** Region capture snaps to panels inside Electron and Chromium apps (via UI Automation), not just to whole windows.
- **Slim surfaces.** The tray menu and sidebar show the verbs, the screenshots folder and Settings. The full ShareX menu lives under More.
- **Slim markup.** The editor toolbar shows rectangle, ellipse, arrow, freehand, text, step number and blur, with shadows on by default. The other tools are under More and keep their hotkeys.
- **Short recordings.** GIFs stop at 15 s and clips at 30 s unless you set your own duration.
- **Quick OCR.** A Capture text menu entry, Ctrl+Shift+T, an OCR button on the after-capture popup, and an optional "copy text after capture".
- **Its own identity.** Its own name, single-instance lock, settings folder (`%LOCALAPPDATA%\ShareXg`), icon, orange accent and a build stamp in the title bar. No update checks or promo prompts.

## Build and run

Needs the .NET 11 SDK on Windows x64. There are no prebuilt releases.

```
dotnet build ShareX/ShareX.csproj -c Release -r win-x64
```

Build the `ShareX` project; the solution-level build does not work. Then run `ShareX\bin\Release\win-x64\ShareX.exe`.

## Staying close to upstream

ShareXg merges ShareX `develop` regularly (`Scripts/Sync-Upstream.ps1`) and keeps its own changes small: stock features are hidden, not deleted. Generic fixes may be offered back upstream. Developer notes are in [HANDOFF.md](HANDOFF.md).

## Credits and license

ShareXg is a **modified version of ShareX** by the ShareX Team, licensed under the [GNU General Public License v3](LICENSE.txt). ShareXg's changes are released under the same license. The full list of modifications is the git history since the last upstream merge (`git log upstream/develop..main`). The ShareX name and logo belong to the ShareX Team.
