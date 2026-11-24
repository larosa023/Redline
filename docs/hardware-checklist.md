# Real-hardware checklist

Run on a real PC before each release. Note GPU, driver version and result for each line.

## NVIDIA (reference: RTX 4090, Ryzen 9 7900X)

- [ ] Install: one admin prompt, helper service running.
- [ ] Opening Redline applies nothing (card stays at factory clocks and power).
- [ ] Power limit: 80% applies; reading back shows 80%; Factory restores 100%.
- [ ] Core offset +150: applies and reads back; confirm the value matches what other tools show.
- [ ] **Memory offset units:** +1000 in Redline vs. what MSI Afterburner / nvidia-smi report. Record the factor and adjust `reference.json` / UI labels if NVML uses a different scale.
- [ ] Fan curve drives fans; Factory returns fans to automatic.
- [ ] Keep-or-revert: ignore the prompt → settings revert after 15 s.
- [ ] Auto-tune Performance completes; result is stable in a real game.
- [ ] Auto-tune Efficiency completes; power drops, performance within 2%.
- [ ] Temperature guard: set ceiling to 70 °C, load the GPU → tune scales back.
- [ ] Per-game switching with a real game.
- [ ] Overlay visible in a borderless game.
- [ ] Tray + hotkeys switch profiles.
- [ ] Quit → back to factory (default).
- [ ] Reboot with Start with Windows on → tune applies ~15 s after login.
- [ ] Force a driver reset (TDR) → Redline switches to factory and shows a notice.
- [ ] Update from previous beta works in-app.

## AMD / Intel (testers)

- [ ] Same list, plus: Intel shows the overclocking waiver before the first tune.
- [ ] Send the "Send report" zip with results.

## AMD Radeon and Intel Arc (needs volunteers)

Redline's AMD and Intel support was built from the vendors' SDK headers and tested with simulated drivers only.
Ask testers to use the "Hardware report" issue form and attach Settings → Create report.

- [ ] AMD RDNA2/3/4: card appears; Home readings match AMD Software (temp, hotspot, clock, board power, fan RPM)
- [ ] AMD: power limit, core (max clock) offset, memory offset and voltage offset apply and read back; Back to factory works
- [ ] AMD: card already tuned in AMD Software → first Apply resets to factory once, then applies
- [ ] Intel Arc: card appears without the integrated GPU; Tune shows Intel's terms; nothing applies before "I accept"
- [ ] Intel: after accepting, power / core / voltage offsets apply and read back; acceptance survives a reboot
- [ ] Both: any control the driver reports oddly is simply missing (never wrong values)
