using System.Text.RegularExpressions;

namespace Redline.Core.Cpu;

public sealed record BiosGuide(string Title, string Summary, IReadOnlyList<string> Steps, IReadOnlyList<string> Warnings);

/// <summary>Plain-language BIOS tuning walkthroughs. Redline never changes these settings itself.</summary>
public static class BiosGuides
{
    static readonly string[] CommonWarnings =
    [
        "Change one thing at a time, then use the PC normally for a few hours (including a game you play) before changing the next.",
        "If your PC won't boot after a change, clear CMOS (see your motherboard manual) to return to defaults.",
        "Update your BIOS first; newer versions often fix stability and boost behavior.",
    ];

    static readonly BiosGuide Ryzen7000 = new("AMD Ryzen 7000 / 9000 — Curve Optimizer",
        "Ryzen boosts on its own. A negative Curve Optimizer lets it run the same clocks at lower voltage, so it boosts higher and runs cooler.",
        [
            "Enter the BIOS (usually Delete or F2 at startup) and find Precision Boost Overdrive (often under AMD Overclocking).",
            "Set Precision Boost Overdrive to Advanced, and PBO Limits to Motherboard (or leave at Auto for stock power).",
            "Open Curve Optimizer, choose All Cores, Sign: Negative, Magnitude: 10.",
            "Save and reboot. Run Redline's CPU stress test, then use the PC normally for a day.",
            "If everything is stable, try 15, then 20. If you get crashes or reboots, go back to the last stable number.",
            "Optional: set your best cores (marked in Ryzen Master) to a smaller value than the rest.",
        ],
        [.. CommonWarnings, "Instability from Curve Optimizer often shows up at idle or light load, not just under stress."]);

    static readonly BiosGuide RyzenX3D = Ryzen7000 with
    {
        Title = "AMD Ryzen X3D — Curve Optimizer",
        Summary = "X3D chips have stricter voltage limits, so Curve Optimizer is the main tuning tool. Keep power limits at stock.",
        Warnings = [.. CommonWarnings, "Don't raise voltages on X3D chips; the 3D cache is sensitive to voltage."],
    };

    static readonly BiosGuide Ryzen5000 = Ryzen7000 with { Title = "AMD Ryzen 5000 — Curve Optimizer" };

    static readonly BiosGuide Intel = new("Intel Core 12th–14th gen — power limits",
        "Intel chips gain the most stability and efficiency from sensible power limits and Intel's default profile.",
        [
            "Update your BIOS to the latest version (important for 13th/14th gen stability fixes).",
            "Enter the BIOS and choose the 'Intel Default Settings' or 'Intel Baseline' profile if offered.",
            "Set Long Duration Power Limit (PL1) and Short Duration Power Limit (PL2) to Intel's values for your CPU.",
            "Optionally set an undervolt using 'AC Load Line' tuning if your board supports it; leave advanced voltage offsets alone unless you're experienced.",
            "Save, reboot, and run Redline's CPU stress test.",
        ],
        [.. CommonWarnings, "Avoid unlimited power limits; they add heat and can shorten CPU life."]);

    static readonly BiosGuide IntelUltra = Intel with { Title = "Intel Core Ultra — power limits", Steps = Intel.Steps.Skip(1).ToList() };

    static readonly BiosGuide Generic = new("CPU tuning — general advice",
        "Redline couldn't match your CPU to a specific guide. These steps apply to most modern desktops.",
        [
            "Make sure XMP/EXPO is enabled so your memory runs at its rated speed.",
            "Leave CPU voltages and multipliers at Auto unless you know your platform well.",
            "Use Redline's CPU boost settings in profiles to trade speed for quiet.",
        ],
        CommonWarnings);

    public static IReadOnlyList<BiosGuide> All { get; } = [Ryzen7000, RyzenX3D, Ryzen5000, Intel, IntelUltra, Generic];

    public static BiosGuide For(string cpuName)
    {
        if (Regex.IsMatch(cpuName, @"Ryzen.*X3D", RegexOptions.IgnoreCase)) return RyzenX3D;
        if (Regex.IsMatch(cpuName, @"Ryzen \d+ (7|9)\d{3}", RegexOptions.IgnoreCase)) return Ryzen7000;
        if (Regex.IsMatch(cpuName, @"Ryzen \d+ 5\d{3}", RegexOptions.IgnoreCase)) return Ryzen5000;
        if (Regex.IsMatch(cpuName, @"Core\(TM\) Ultra|Core Ultra", RegexOptions.IgnoreCase)) return IntelUltra;
        if (Regex.IsMatch(cpuName, @"i[3579]-1[234]\d{3}", RegexOptions.IgnoreCase)) return Intel;
        return Generic;
    }
}
