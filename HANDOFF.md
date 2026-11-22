# ShareXg handoff

Phase: Prove | North Star: Grayson's daily capture tool, a slim public GPL-3 remix of ShareX. Press PrtSc and get exactly the region you meant on any monitor, marked up and on the clipboard in seconds. Short GIFs, clips and quick text grabs sit one step away, everything else stays under More, and the fork stays small enough to sync with upstream.

Machine-specific notes (paths, backups, session history) live in the untracked `HANDOFF.local.md` on the dev machine. Full review: `docs/teardowns/TEARDOWN-2026-11-22.md` (kept locally, not committed).

## North Star (2026-11-22; ⚠️ = to confirm with Grayson)
- **Job:** one key gives a correct, markup-ready capture of exactly the region meant, in seconds, on a mixed-DPI multi-monitor desktop and inside Electron/Chromium panels.
- **User:** Grayson first. Public for GPL-3 compliance and for anyone who wants it, with no support or roadmap promise. ⚠️
- **Verbs it owns:** Screenshot (PrtSc = region, Ctrl+PrtSc = full, panel snapping, cross-monitor drag), Markup (slim editor), Text (OCR from a capture), GIF (≤15 s), Clip (≤30 s). ⚠️ Is OCR a full verb, or a convenience on the screenshot path?
- **The bar:** PrtSc and multi-monitor capture work every time. A broken capture outranks any new feature.
- **Non-goals:** uploaders, URL tools, workflows/actions, the ~40 tools, image effects beyond shadow, theming, and other users' feature requests. Stock features stay under More and are never deleted, which keeps upstream merges cheap.
- **"Used" means:** ShareXg, not Snipping Tool, takes the captures: at least 5 per active day for a week ⚠️, plus at least one GIF or clip and one markup a week, read from History.db. ⚠️ A popup OCR run leaves no History row or log line, so OCR needs its own signal. Baseline 2026-11-22: 23 captures in 1.5 days, 0 GIF, 0 clip, OCR used once from the popup.
- **Fork stance:** stay within a few days of upstream `develop`. Keep ShareXg-only changes small and in their own files where possible. Offer generic fixes upstream (UIA panel snapping, per-monitor DPI heal) only with Grayson's OK.

## Current state
- **Repo:** public `graysonchalmers/ShareXg`, default branch `main`. Work lands by PR. The `upstream` remote is ShareX/ShareX (`develop`). `gh` defaults to upstream, so always pass `-R graysonchalmers/ShareXg`.
- **Upstream parity:** check with `Scripts/Sync-Upstream.ps1`. It fetches, merges into `sync/<date>`, builds and reports conflicts, and never touches `main`. On 2026-11-22 the fork was 10 commits behind and `git merge-tree` showed no conflicts.
- **Build:** `dotnet build ShareX/ShareX.csproj -c Release -r win-x64 -maxcpucount:1` (.NET 11 SDK). The solution-level build fails (NETSDK1047/1134). There are no test projects: the gate is the build plus the proof harnesses below.
- **Install:** stop `ShareX.exe`, mirror `ShareX\bin\Release\win-x64` to `%LOCALAPPDATA%\ShareXg\app`, start `app\ShareX.exe -silent`. Settings live in `%LOCALAPPDATA%\ShareXg`.
- **Identity:** "ShareXg", its own mutex GUID, a hue-rotated icon, orange accent (#F2A33A), and no update, icon-pack or what's-new prompts. Version stamp `22.4 🦊 GLINT · <sha> · <date>`; the sha is embedded by the `ShareXgSha` MSBuild target.

## What ShareXg changes (vs ShareX)
- **Hotkeys:** PrtSc = region, Ctrl+PrtSc = full screen. A low-level hook takes PrtSc from Snipping Tool (`TakeOverPrintScreen`, default on). `Infrastructure/PrintScreenGuard.cs` sets the HKCU Snipping flag while running and restores it on exit, via a watcher if killed, or at next start.
- **Region capture:** one overlay per monitor at native scaling (`PerMonitorOverlay`), drag across monitors (`CrossMonitorDrag`), and DPI self-heal for mixed scaling. The opt-in drag trace writes to `%LOCALAPPDATA%\ShareXg\span-log.txt` only while that file exists.
- **Snapping:** UI Automation snaps into Electron/Chromium panels (`UiaRegionScanner`).
- **Slim UI:** a tiny welcome dialog; the tray and sidebar show the verbs plus Screenshots folder, Settings, More ▸ (full stock menu) and About. The editor toolbar keeps Select, Rectangle, Ellipse, Arrow, Freehand, Text, Step and Blur, with the rest under More. Shadows are on.
- **Recording:** GIF capped at 15 s and clip at 30 s (`ScreenRecordManager.RecordCapSeconds`) unless the user sets a fixed duration. The native recorder needs no ffmpeg.
- **OCR:** Capture text (OCR) menu entry, Ctrl+Shift+T, an OCR button on the after-capture popup, and an optional copy-text-after-capture toggle. A one-time migration adds the button and hotkey to existing configs.

## Proof harnesses (off-screen; never on the user's screen)
Run with `ShareX.exe -multi -sandbox -silent -NoHotkeys`:
- `-RenderWelcome <png>` renders the welcome dialog.
- `-RenderMain <dir>` writes `main-window.png` and `tray-menu.png`.
- `-RenderEditor <dir>` writes `editor.png` and `editor-more.png`.
- `-RenderPerMonitor <dir>` writes per-monitor slices, span math and a drag-path test.

`-TestRegionOverlay <seconds>` opens the real overlays on screen, so ask first. Sandbox runs still write to the live log, so hotkey-register failures there are expected noise. Committed proof lives in `proof/`.

## Open
- North Star ⚠️ items above.
- No live GIF/clip recording on the real build yet. Do one supervised run.
- Upstream PR candidates (each needs Grayson's OK): UIA panel snapping (#8862), per-monitor DPI heal (#9175, #9046). Remove debug-only logging from any upstream PR.

## Rules
- No screen or mic recording, keypresses or UI tests while Grayson is active without a "give me 30 seconds" first. Off-screen renders and headless checks are always fine.
- Restart ShareXg quietly (`-silent`). Never trigger the welcome dialog on his screen.
- Anything Grayson asks for is turned on in his own settings by default.
- Work on a branch, open a PR, merge when the build and harnesses pass. The repo is public: no personal paths, emails or screenshots in commits.
