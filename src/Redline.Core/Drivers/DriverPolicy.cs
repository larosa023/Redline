using System.Text.Json;
using Redline.Core.Gpu;
using Redline.Core.Storage;

namespace Redline.Core.Drivers;

public enum DriverStatus { Ok, TooOld, KnownBad, Unknown }

public sealed record DriverVerdict(DriverStatus Status, string? Message);

public sealed record KnownBadDriver(string Version, string Reason);

public sealed record VendorDriverPolicy(string MinVersion, IReadOnlyList<KnownBadDriver> KnownBad);

public static class DriverPolicy
{
    static readonly Lazy<IReadOnlyDictionary<Vendor, VendorDriverPolicy>> _shipped = new(() =>
    {
        using var s = typeof(DriverPolicy).Assembly.GetManifestResourceStream("Redline.Core.Drivers.driver-policy.json")
            ?? throw new InvalidOperationException("driver-policy.json missing");
        return JsonSerializer.Deserialize<Dictionary<Vendor, VendorDriverPolicy>>(s, Json.Options)!;
    });

    public static DriverVerdict Check(Vendor v, string driverVersion, IReadOnlyDictionary<Vendor, VendorDriverPolicy>? data = null)
    {
        if (v == Vendor.Simulated) return new(DriverStatus.Ok, null);
        if (!(data ?? _shipped.Value).TryGetValue(v, out var p)) return new(DriverStatus.Unknown, null);
        if (Parse(driverVersion) is not { } ver) return new(DriverStatus.Unknown, "Couldn't read the driver version.");
        var bad = p.KnownBad.FirstOrDefault(b => Parse(b.Version) is { } bv && Compare(bv, ver) == 0);
        if (bad is not null) return new(DriverStatus.KnownBad, $"Driver {driverVersion} has a known problem with tuning: {bad.Reason}. Updating your driver is recommended.");
        if (Parse(p.MinVersion) is { } min && Compare(ver, min) < 0)
            return new(DriverStatus.TooOld, $"Driver {driverVersion} is too old for tuning. Please update to {p.MinVersion} or newer; monitoring still works.");
        return new(DriverStatus.Ok, null);
    }

    static int[]? Parse(string s)
    {
        var parts = s.Trim().Split('.');
        var r = new int[parts.Length];
        for (var i = 0; i < parts.Length; i++) if (!int.TryParse(parts[i], out r[i])) return null;
        return r.Length == 0 ? null : r;
    }

    static int Compare(int[] a, int[] b)
    {
        for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
        {
            var x = i < a.Length ? a[i] : 0;
            var y = i < b.Length ? b[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }
}
