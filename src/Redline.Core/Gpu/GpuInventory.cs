namespace Redline.Core.Gpu;

/// <summary>Which vendor libraries are worth loading, from the display adapters Windows lists.</summary>
public sealed record VendorPlan(bool Nvidia, bool Amd, bool Intel);

public static class GpuInventory
{
    /// <summary>
    /// Load a vendor's library only for a card Redline can tune: NVIDIA for any NVIDIA adapter, AMD for a discrete
    /// Radeon (not a Ryzen CPU's built-in "AMD Radeon(TM) Graphics"), Intel only for Arc. AMD's and Intel's libraries
    /// have hung when only a CPU's built-in graphics were present. If Windows lists nothing, ask every vendor.
    /// </summary>
    public static VendorPlan Plan(IEnumerable<(string PnpId, string Name)> adapters)
    {
        var list = adapters.ToList();
        if (list.Count == 0) return new(true, true, true);
        bool Ven(string pnp, string id) => pnp.Contains($"VEN_{id}", StringComparison.OrdinalIgnoreCase);
        var nvidia = list.Any(a => Ven(a.PnpId, "10DE"));
        var amd = list.Any(a => Ven(a.PnpId, "1002") && IsDiscreteRadeon(a.Name));
        var intel = list.Any(a => Ven(a.PnpId, "8086") && a.Name.Contains("Arc", StringComparison.OrdinalIgnoreCase));
        return new(nvidia, amd, intel);
    }

    static bool IsDiscreteRadeon(string name) =>
        name.Contains(" RX ", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Radeon PRO", StringComparison.OrdinalIgnoreCase)
        || name.Contains("Radeon VII", StringComparison.OrdinalIgnoreCase);
}
