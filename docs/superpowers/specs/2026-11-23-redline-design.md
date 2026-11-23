# Redline — Design

**Date:** 2026-11-23
**Owner:** Anthony LaRosa (larosa023)
**Repo:** github.com/larosa023/Redline (public)
**Status:** Draft, awaiting approval

---

## 1. Intent

Redline is a Windows app for tuning and monitoring graphics cards. It is
meant for anyone with a gaming PC, on any hardware, not only the owner's
own machine. It should do what tools like MSI Afterburner do, and more,
but look and feel like a modern, clean consumer app, not a wall of tiny
sliders and graphs.

The *why*: existing tuning tools are powerful but intimidating and dated.
Redline should make tuning approachable: one click to find a good, stable
tune automatically, with full manual control there for people who want it,
and safety nets so a bad setting can't leave someone stuck.

## 2. Goals

1. **Works on any PC.** Supports NVIDIA, AMD and Intel graphics cards, and
   any Intel or AMD CPU (for monitoring), including systems with more than
   one GPU (e.g. laptops with integrated + dedicated graphics).
2. **Capability-driven UI.** The app detects what each GPU allows and only
   shows controls that actually work on that card. Locked laptop GPUs get
   whatever is available (often monitoring and maybe fans).
3. **GPU tuning:** power limit, core clock offset, memory clock offset,
   voltage-related controls where the vendor exposes them, fan control.
4. **Full monitoring:** live GPU and CPU clocks, temperatures, power,
   usage, fan speeds, VRAM use; smooth graphs with history.
5. **Auto-tune** with two modes:
   - **Performance:** find the highest stable clocks.
   - **Efficiency:** keep stock-level performance at lower power, heat
     and noise (undervolt-style).
   Uses a built-in stress test with error checking; backs off on any
   instability.
6. **Manual sliders** for fine-tuning on top of or instead of auto-tune.
7. **Profiles:** named presets (e.g. Gaming, Quiet, Stock), one-click
   switching, one-click return to stock.
8. **Per-game profiles:** automatically switch when a chosen game starts,
   switch back when it closes.
9. **Before/after check:** a ~60-second built-in test that reports what
   the tune changed (performance %, temperature, power, fan speed).
10. **Temperature guard:** if the GPU exceeds a user-set temperature, the
    app scales the tune back automatically and notifies the user.
11. **Tray mode + hotkeys:** a small tray menu for switching profiles;
    global keyboard shortcuts for profile switching.
12. **Drag-to-edit fan curve:** a graph with draggable points.
13. **Chip quality score:** how this card's tune compares to the card's
    stock specs, shown as a shareable image card.
14. **Power-cost savings:** turns watts saved into estimated money saved
    per month (user enters their electricity rate).
15. **Profile sharing:** export/import a tune as a short text code.
16. **In-game overlay:** FPS, frame time, GPU/CPU temperature and usage on
    top of games.
17. **Built-in updater:** checks for updates, shows short release notes,
    one-click update, downloads only what changed. Releases published to
    GitHub Releases.
18. **Clean, modern themes:** dark and light, several accent colors, big
    readable numbers, generous spacing, smooth animations.
19. **Safety:**
    - **Safe start:** if the PC crashes or shuts down uncleanly while a
      tune is active (or mid-auto-tune), the next launch applies stock and
      asks before reapplying the tune.
    - **Delayed apply at boot:** saved tune applies a few seconds after
      Windows starts, not instantly.
    - **Undo history:** every change is logged and can be stepped back.
    - **Driver checks:** warn when a GPU driver is too old or known-bad
      for tuning.
20. **"Send report" button:** bundles logs and hardware info into one file
    for bug reports (user sends it themselves).
21. **Privacy:** nothing leaves the PC except the update check. No
    accounts, no analytics.
22. **Lightweight:** near-zero CPU use when minimized; must not hurt game
    performance.

## 3. Non-goals

- **CPU tuning** (PBO, Curve Optimizer, Intel undervolting). Requires
  low-level kernel drivers that antivirus flags and that can crash
  strangers' systems. CPU is monitoring only.
- **RAM tuning.** On modern platforms this is BIOS-only.
- **macOS / Linux.** Windows 10/11 x64 only.
- **Integration with Hangar.** Redline is standalone.
- **Staged releases.** Everything ships together as 1.0.
- **Online services:** no accounts, cloud sync or community database.

## 4. Anti-goals (would make it a failure even if it "works")

- Looks like MSI Afterburner: dense, nerdy, skinned, cluttered.
- Gets users **banned in online games**. The overlay must never inject
  into game processes (see §6.6).
- Leaves a user's PC unstable with no easy way back to stock.
- Triggers antivirus warnings because of bundled kernel drivers.
- Collects or sends user data.
- Applies settings a card doesn't support, or claims to apply them when
  the driver silently refused.

## 5. Approaches considered

| Approach | Trade-offs | Verdict |
|---|---|---|
| **C# / .NET with native vendor SDKs + LibreHardwareMonitor** | Best monitoring library already in C#; vendor SDKs callable from C#; modern Windows UI; low memory. Windows-only (fine). | **Chosen** |
| C++ native | Slightly faster; much slower to build, hard to make look modern. | Rejected |
| Electron / Tauri web UI | Easy to make pretty; would need hardware monitoring rebuilt from scratch; Electron is memory-heavy while gaming. | Rejected |
| Rust | Good language; immature Windows UI and hardware monitoring ecosystem. | Rejected |
| Wrap existing tools (Afterburner, etc.) | Not redistributable, NVIDIA-centric, fragile. | Rejected |

Anthony asked why C# and accepted the reasoning above.

## 6. Architecture

### 6.1 Projects

```
Redline.sln
├─ src/Redline.Core            (.NET, no Windows UI deps — fully unit-testable on any OS)
│    Domain models, IGpuBackend abstraction, capability model, profiles,
│    fan curves, auto-tune engine, temperature guard, safe-start state,
│    undo history, profile share codes, power-cost math, settings storage.
├─ src/Redline.Hardware.Nvidia (NVML via P/Invoke; nvml.dll ships with the driver)
├─ src/Redline.Hardware.Amd    (ADLX; ships with the AMD driver)
├─ src/Redline.Hardware.Intel  (IGCL / ControlLib.dll; ships with the Intel driver)
├─ src/Redline.Hardware.Sim    (simulated GPU for development, tests and a demo mode)
├─ src/Redline.Monitoring      (LibreHardwareMonitor wrapper: CPU + extra sensors)
├─ src/Redline.StressTest      (Direct3D 11 compute-shader load + error checking)
├─ src/Redline.Overlay         (transparent top-most overlay window + PresentMon FPS)
├─ src/Redline.App             (WinUI 3 desktop app, tray, hotkeys, updater)
└─ tests/…                     (xUnit tests for Core and backends-with-fakes)
```

### 6.2 GPU backend abstraction

One interface, `IGpuBackend`, discovers GPUs and returns `IGpuDevice`
objects. Each device reports a **capability set**: for each control
(power limit, core offset, memory offset, voltage offset, fan control,
etc.) whether it is supported, and its min/max/step as reported by the
driver. The UI is built entirely from capabilities, never from vendor
checks.

Every write is **read back and verified**; if the driver reports a
different value, the UI shows what actually applied.

Vendor notes (verified against current SDK docs):

- **NVIDIA (NVML):** power limit, core/memory clock offsets (newer
  `nvmlDeviceSetClockOffsets` on R555+ drivers, older
  `SetGpcClkVfOffset`/`SetMemClkVfOffset` as fallback; the newer call has
  known per-performance-state quirks, so read-back is essential), fan
  speed control, telemetry. Setting values needs administrator rights.
- **AMD (ADLX):** separate manual graphics, VRAM, fan and power tuning
  interfaces, each with an "is supported" check; tuning interface
  versions differ by GPU generation. ADLX also exposes AMD's own
  auto-undervolt/auto-overclock, which Redline may use as a hint but
  does not depend on.
- **Intel (IGCL):** GPU frequency offset, voltage offset, power limit,
  VRAM settings on supporting cards, and telemetry. Requires the user to
  accept Intel's overclocking waiver via the API; Redline shows that
  waiver in its own UI before first tuning an Intel card.

### 6.3 Monitoring

- GPU telemetry comes from the vendor backend.
- CPU and system sensors come from **LibreHardwareMonitor** (MPL-2.0).
  Current versions use the **PawnIO** driver (not the antivirus-flagged
  WinRing0) for CPU sensors, and PawnIO must be installed separately.
  Redline offers to install it (with explanation) on first run. Without
  it, Redline still works but CPU temps may be missing, and says so.
- Polling at 1 s by default, 4–10× slower when minimized; history kept
  in a fixed-size ring buffer in memory.

### 6.4 Auto-tune engine (in Core, hardware-agnostic)

- Runs against `IGpuDevice` + the stress test + a stability monitor.
- **Performance mode:** raise core offset in steps, stress-test each step
  (with error checking), back off on failure, then repeat for memory;
  finish with a longer confirmation run at the chosen values minus a
  safety margin.
- **Efficiency mode:** lower power limit / apply negative offsets or
  voltage reductions (whatever the card supports) while measuring
  stress-test performance; keep the lowest-power setting within ~2% of
  stock performance.
- Failure signals: stress-test computation errors, driver timeout/reset
  (Windows "display driver stopped responding" events), app crash,
  clocks/telemetry dropping out.
- Before every step, the step is written to disk as "pending". If Redline
  starts and finds a pending step, it applies stock and marks that step as
  failed (safe start).
- User can cancel at any time; cancel returns to the last confirmed-good
  settings.

### 6.5 Stress test

Direct3D 11 compute shader workload (via Vortice.Windows) that is
deterministic: results are checksummed against a known-good run at stock,
so silent memory/core errors are caught, not just crashes. Same engine
powers the before/after check (scored by work done per second).

### 6.6 Overlay (anti-cheat safe)

- A transparent, click-through, always-on-top window. **No injection or
  hooking into game processes**, ever.
- FPS and frame times come from **PresentMon** (Intel, MIT-licensed,
  uses Windows event tracing).
- Limitation, stated in the UI: overlays like this show over games in
  borderless/windowed mode; exclusive fullscreen games may hide them.
  This is accepted in exchange for zero anti-cheat risk.

### 6.7 App, privileges, startup

- **UI:** WinUI 3 (Windows App SDK), unpackaged, self-contained .NET.
  Charts via LiveCharts2 (or equivalent); tray via H.NotifyIcon.
- **Admin rights:** tuning needs administrator rights. The app runs
  elevated. Starting with Windows uses a scheduled task with "run with
  highest privileges" so users don't get a UAC prompt every boot.
- **Per-game profiles:** watch process starts/stops; match on executable
  name chosen by the user from a list of running/installed games.
- **Settings & profiles** stored as JSON in `%AppData%\Redline`.

### 6.8 Updater & releases

- **Velopack** for install + delta updates from **GitHub Releases** on
  the public repo.
- **GitHub Actions (Windows runner)** builds, runs tests, packages, and
  publishes a release when a version tag is pushed. Claude runs the whole
  release process; Anthony never has to do git/release steps by hand.
- **No code signing** (Anthony's decision). Builds are unsigned, so
  Windows SmartScreen shows an "unknown publisher" warning on first
  install; the README and download page explain how to proceed.

### 6.9 Visual design

- Dark and light themes, several accent colors, Windows 11 Mica
  background.
- Big readable numbers, card-based layout, plenty of whitespace, smooth
  transitions. Feels like a modern phone settings app, not a cockpit.
- Before UI work starts, Claude shows Anthony 2–3 visual mockups to pick
  a direction.

## 7. Constraints

- Windows 10 (21H2+) and 11, x64.
- No bundled kernel drivers. Only vendor driver DLLs already on the system
  plus optional PawnIO.
- Development happens in a Linux workspace: Core and tests build/run
  there; Windows-only projects build and test in GitHub Actions; real
  hardware testing happens on Anthony's PC (RTX 4090, Ryzen 9 7900X).
- AMD and Intel paths can only be verified by outside testers before 1.0.

## 8. Testing strategy

- Core logic (auto-tune decisions, fan curve math, profiles, safe-start,
  share codes, temperature guard) has unit tests driven by the simulated
  GPU backend, runnable on Linux.
- Vendor backends tested against fakes of the native API layer in CI;
  real-hardware smoke checks on Anthony's 4090.
- Hidden **diagnostics page** dumps the capability set and raw readings
  for each GPU, which testers include in "Send report".

## 9. Calls made by Claude (Anthony to check)

1. **Chip quality score** compares against the card's own rated specs
   (bundled reference data), not against other users. A community
   comparison would need a server and break the privacy goal.
2. **Profile sharing** is copy/paste codes only, no website or server.
3. **Overlay** is the non-injecting kind. Exclusive-fullscreen games may
   not show it; this avoids anti-cheat bans.
4. **PawnIO** is offered as an optional install for CPU temperatures,
   rather than bundling any driver.
5. **The app runs as administrator**, with a scheduled task for startup.
6. **WinUI 3** for the UI framework.
7. **Simulated GPU / demo mode** included so the app can be developed and
   shown without special hardware.

## 10. Decisions by Anthony

1. **No code signing**, now or later.
2. **Open source** under the MIT license.

## 11. Left to the builder

- Exact auto-tune step sizes, durations and safety margins (tunable
  constants, sensible defaults per vendor).
- Known-bad driver list contents.
- Chart and tray libraries if better options appear.
- File formats for settings, share codes and logs.
- Exact layout and visuals, after the mockup round with Anthony.
