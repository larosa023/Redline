# Redline

Redline is a free, open-source Windows app for tuning and monitoring your graphics card. It works with NVIDIA, AMD and Intel GPUs and shows CPU stats too.

- **One-click auto-tune:** *Performance* finds your card's best stable clocks; *Efficiency* keeps stock performance with less power, heat and noise.
- **Manual control** when you want it: power limit, clock offsets, voltage (where your card allows it) and a drag-to-edit fan curve.
- **Profiles** (*Send it*, *Whisper*, *Factory*, or your own), per-game switching, tray menu and hotkeys.
- **In-game overlay**, weekly recap, before/after check, chip score and shareable tune codes.
- **Five themes:** Daylight, Midnight, Ember, Glacier and Neon.

## Safety first

- Opening Redline never changes anything. Changes wait until you press **Apply**, and revert automatically after 15 seconds unless you confirm them.
- Redline only uses the official tuning interfaces from NVIDIA, AMD and Intel, and stays inside a conservative safe range unless you turn on Advanced mode.
- Auto-tune never raises your power limit unless you allow it, and every result keeps a safety margin.
- A temperature guard scales back your tune if your card runs too hot, and after any crash Redline starts at factory settings.
- Tunes are never written to your card's firmware. A restart always returns the card to factory settings.
- No accounts, no analytics. The only thing Redline sends is the update check to GitHub.

## Installing

Download the latest installer from [Releases](https://github.com/larosa023/Redline/releases).

Redline isn't code-signed, so Windows SmartScreen may say it "protected your PC" the first time. Click **More info → Run anyway**. Setup asks for administrator permission once to install Redline's background helper, the only part that talks to your hardware.

## Building

Requires the .NET 10 SDK on Windows 10 21H2+ or Windows 11.

```
dotnet build Redline.sln
dotnet test Redline.sln
```

## License

MIT. See [LICENSE](LICENSE) and [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
