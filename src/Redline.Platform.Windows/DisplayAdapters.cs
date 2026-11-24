using System.Management;
using System.Runtime.Versioning;

namespace Redline.Platform.Windows;

/// <summary>The display adapters Windows knows about (no vendor libraries involved).</summary>
[SupportedOSPlatform("windows")]
public static class DisplayAdapters
{
    public static IReadOnlyList<(string PnpId, string Name)> List()
    {
        try
        {
            using var q = new ManagementObjectSearcher("SELECT PNPDeviceID, Name FROM Win32_VideoController");
            return q.Get().Cast<ManagementObject>()
                .Select(o => (PnpId: o["PNPDeviceID"] as string ?? "", Name: o["Name"] as string ?? ""))
                .Where(a => a.PnpId.Length > 0).ToList();
        }
        catch (Exception e) when (e is ManagementException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            return [];   // unknown: GpuInventory then asks every vendor
        }
    }
}
