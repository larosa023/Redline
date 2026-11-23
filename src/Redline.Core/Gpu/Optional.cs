namespace Redline.Core.Gpu;

/// <summary>Devices that require the user to accept a vendor overclocking waiver (Intel) before tuning.</summary>
public interface IRequiresWaiver
{
    bool WaiverAccepted { get; }
    void AcceptWaiver();
}

/// <summary>Devices whose fans take a fixed percentage; the engine runs the fan curve and sets it each tick.</summary>
public interface IFanPercentControl
{
    Task SetFanPercentAsync(int pct, CancellationToken ct);
    Task SetFanAutoAsync(CancellationToken ct);
}
