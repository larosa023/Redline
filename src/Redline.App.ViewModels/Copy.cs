namespace Redline.App.ViewModels;

/// <summary>User-facing text. Friendly in normal states; plain and calm in warnings and errors.</summary>
public static class Copy
{
    public const string StatusFactory = "Factory, nothing applied";
    public const string StatusCustom = "Custom tune is active";
    public static string StatusProfile(string name) => $"{name} is active";

    public const string HelperMissingTitle = "Redline Helper isn't running";
    public const string HelperSetupDidNotFinish = "Helper setup didn't finish. If you saw an admin prompt, choose Yes. Details: C:\\ProgramData\\Redline\\install.log";
    public const string HelperDidNotStart = "The helper was set up but hasn't answered yet. Redline will keep trying on its own. Details: C:\\ProgramData\\Redline\\install.log";
    public const string HelperMissingBody = "Redline needs its background helper to read and tune your hardware. Repair sets it up again (one admin prompt).";

    public const string ChipCruising = "Cruising";
    public const string ChipWarm = "Running warm";
    public const string ChipBest = "New personal best";

    public const string ConnectorWarningTitle = "About your card's power connector";
    public const string ConnectorWarningBody =
        "Your card uses the 16-pin power connector. Raising the power limit above 100% sends more current through it. " +
        "Make sure the plug is pushed in fully and the cable isn't bent sharply near the connector.";

    public static string KeepPrompt(int s) => $"Keep these settings? Reverting in {s}…";

    /// <summary>Hero line when a tune is on and a before/after check has measured it.</summary>
    public static string TunedHeroLine(double perfPct, double tempDeltaC) =>
        perfPct >= 0.5 || tempDeltaC <= -0.5
            ? HeroLine(perfPct, tempDeltaC)
            : "Your tune is on. The last check found no clear gain under full load; games often still benefit.";

    public static string HeroLine(double perfPct, double tempDeltaC)
    {
        var faster = perfPct >= 0.5 ? $"{perfPct:0}% faster" : null;
        var cooler = tempDeltaC <= -0.5 ? $"{-tempDeltaC:0} °C cooler" : null;
        return (faster, cooler) switch
        {
            (null, null) => "Your card's running at factory settings. Plenty of room to play.",
            ({ } f, null) => $"Your card's right where it likes to be: {f} than the factory.",
            (null, { } c) => $"Same speed, {c} than the factory. Nice and calm.",
            ({ } f, { } c) => $"Your card's right where it likes to be: {f} than the factory and {c}.",
        };
    }
}
