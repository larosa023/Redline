using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace Redline.Hardware.Intel;

/// <summary>
/// Intel Graphics Control Library (ControlLib.dll, installed with Intel Arc drivers). Struct sizes and field offsets
/// are taken from intel/drivers.gpu.control-library's igcl_api.h (MIT) by compiling it, so no struct is hand-copied.
/// Integrated GPUs are skipped: they can't be tuned and would only clutter the device list.
/// </summary>
public sealed unsafe class IgclApi : IIgclApi, IDisposable
{
    // igcl_api.h (x64) sizes and offsets.
    const int InitArgsSize = 36, InitVersion = 4, InitAppVersion = 8, InitFlags = 12;
    const int PropsSize = 320, PropsDeviceIdPtr = 8, PropsDeviceIdSize = 16, PropsDeviceType = 20, PropsDriverVersion = 32,
        PropsVendor = 64, PropsName = 88, NameLen = 100, PropsAdapterFlags = 188;
    const int OcSize = 440, OcSupported = 5, OcGpuFreq = 8, OcGpuVolt = 56, OcVramFreq = 104, OcPower = 200;
    const int CtlSupported = 0, CtlUnits = 4, CtlMin = 8, CtlMax = 16, CtlStep = 24, CtlDefault = 32;
    const int TelSize = 1024, TelTime = 8, TelGpuEnergy = 32, TelGpuVolt = 56, TelClock = 80, TelTemp = 104, TelActivity = 128,
        TelPowerLimited = 200, TelTempLimited = 201, TelVoltLimited = 203, TelUtilLimited = 204, TelVramClock = 256, TelVramTemp = 352,
        TelCardEnergy = 384, TelFan = 688;
    const int ItemSupported = 0, ItemUnits = 4, ItemType = 8, ItemValue = 16;
    const uint AppVersion = (1u << 16) | 1u;
    const int DeviceTypeGraphics = 1, AdapterIntegrated = 1;

    readonly object _lock = new();
    nint _lib, _api;
    readonly List<nint> _devices = new();
    List<IgclGpuInfo>? _infos;
    readonly Dictionary<int, (double Time, double Energy, double Activity)> _last = new();

    nint Fn(string name) => NativeLibrary.TryGetExport(_lib, name, out var f) ? f : throw new IgclException($"{name} missing in ControlLib.dll");

    static void Check(int r, string what) { if (r != 0) throw new IgclException($"IGCL {what} failed (0x{r:X})"); }

    public bool TryLoad()
    {
        lock (_lock)
        {
            if (_api != 0) return true;
            if (!OperatingSystem.IsWindows() || !NativeLibrary.TryLoad("ControlLib.dll", out _lib)) return false;
            try
            {
                var args = stackalloc byte[InitArgsSize];
                new Span<byte>(args, InitArgsSize).Clear();
                *(uint*)args = InitArgsSize;
                args[InitVersion] = 0;
                *(uint*)(args + InitAppVersion) = AppVersion;
                *(uint*)(args + InitFlags) = 0;
                nint api;
                if (((delegate* unmanaged[Cdecl]<byte*, nint*, int>)Fn("ctlInit"))(args, &api) != 0 || api == 0) return false;
                _api = api;
                uint count = 0;
                var enumerate = (delegate* unmanaged[Cdecl]<nint, uint*, nint*, int>)Fn("ctlEnumerateDevices");
                if (enumerate(_api, &count, null) != 0 || count == 0) return true;
                var handles = new nint[count];
                fixed (nint* h = handles) Check(enumerate(_api, &count, h), "ctlEnumerateDevices");
                var infos = new List<IgclGpuInfo>();
                foreach (var h in handles.Take((int)count))
                    if (Describe(h, _devices.Count) is { } info) { _devices.Add(h); infos.Add(info); }
                _infos = infos;
                return true;
            }
            catch (IgclException) { return false; }
        }
    }

    IgclGpuInfo? Describe(nint dev, int index)
    {
        var p = stackalloc byte[PropsSize];
        new Span<byte>(p, PropsSize).Clear();
        var luid = stackalloc byte[8];
        *(uint*)p = PropsSize;
        *(nint*)(p + PropsDeviceIdPtr) = (nint)luid;
        *(uint*)(p + PropsDeviceIdSize) = 8;
        if (((delegate* unmanaged[Cdecl]<nint, byte*, int>)Fn("ctlGetDeviceProperties"))(dev, p) != 0) return null;
        if (*(int*)(p + PropsDeviceType) != DeviceTypeGraphics || (*(uint*)(p + PropsAdapterFlags) & AdapterIntegrated) != 0) return null;
        var nameBytes = new ReadOnlySpan<byte>(p + PropsName, NameLen);
        var name = System.Text.Encoding.ASCII.GetString(nameBytes[..Math.Max(0, nameBytes.IndexOf((byte)0) is var z and >= 0 ? z : NameLen)]).Trim();
        var dv = *(ulong*)(p + PropsDriverVersion);
        var driver = $"{(dv >> 48) & 0xFFFF}.{(dv >> 32) & 0xFFFF}.{(dv >> 16) & 0xFFFF}.{dv & 0xFFFF}";
        var id = Convert.ToHexString(new ReadOnlySpan<byte>(luid, 8)).ToLowerInvariant();
        return new IgclGpuInfo(index, name.Length > 0 ? name : "Intel Arc", driver, id);
    }

    nint Dev(int gpu) => gpu >= 0 && gpu < _devices.Count ? _devices[gpu] : throw new IgclException("IGCL device not found");

    public IReadOnlyList<IgclGpuInfo> Gpus() { lock (_lock) return _infos ?? []; }

    static IgclControl? Control(byte* c)
    {
        if (c[CtlSupported] == 0) return null;
        return new IgclControl(*(double*)(c + CtlMin), *(double*)(c + CtlMax), *(double*)(c + CtlStep), *(double*)(c + CtlDefault), (IgclUnits)(*(int*)(c + CtlUnits)));
    }

    public IgclOc Properties(int gpu)
    {
        lock (_lock)
        {
            var p = stackalloc byte[OcSize];
            new Span<byte>(p, OcSize).Clear();
            *(uint*)p = OcSize;
            Check(((delegate* unmanaged[Cdecl]<nint, byte*, int>)Fn("ctlOverclockGetProperties"))(Dev(gpu), p), "ctlOverclockGetProperties");
            return new IgclOc(p[OcSupported] != 0, Control(p + OcGpuFreq), Control(p + OcGpuVolt), Control(p + OcVramFreq), Control(p + OcPower));
        }
    }

    public void AcceptWaiver(int gpu) { lock (_lock) Check(((delegate* unmanaged[Cdecl]<nint, int>)Fn("ctlOverclockWaiverSet"))(Dev(gpu)), "ctlOverclockWaiverSet"); }

    double Get(int gpu, string fn)
    {
        lock (_lock)
        {
            double v;
            Check(((delegate* unmanaged[Cdecl]<nint, double*, int>)Fn(fn))(Dev(gpu), &v), fn);
            return v;
        }
    }

    void Set(int gpu, string fn, double v) { lock (_lock) Check(((delegate* unmanaged[Cdecl]<nint, double, int>)Fn(fn))(Dev(gpu), v), fn); }

    public double GetGpuFrequencyOffset(int gpu) => Get(gpu, "ctlOverclockGpuFrequencyOffsetGet");
    public void SetGpuFrequencyOffset(int gpu, double mhz) => Set(gpu, "ctlOverclockGpuFrequencyOffsetSet", mhz);
    public double GetGpuVoltageOffset(int gpu) => Get(gpu, "ctlOverclockGpuVoltageOffsetGet");
    public void SetGpuVoltageOffset(int gpu, double mv) => Set(gpu, "ctlOverclockGpuVoltageOffsetSet", mv);
    public double GetVramFrequencyOffset(int gpu) => Get(gpu, "ctlOverclockVramFrequencyOffsetGet");
    public void SetVramFrequencyOffset(int gpu, double mhz) => Set(gpu, "ctlOverclockVramFrequencyOffsetSet", mhz);
    public double GetPowerLimit(int gpu) => Get(gpu, "ctlOverclockPowerLimitGet");
    public void SetPowerLimit(int gpu, double value) => Set(gpu, "ctlOverclockPowerLimitSet", value);
    public void ResetToDefault(int gpu) { lock (_lock) Check(((delegate* unmanaged[Cdecl]<nint, int>)Fn("ctlOverclockResetToDefault"))(Dev(gpu)), "ctlOverclockResetToDefault"); }

    static double? Item(byte* t, int offset)
    {
        var it = t + offset;
        if (it[ItemSupported] == 0) return null;
        var v = it + ItemValue;
        return *(int*)(it + ItemType) switch
        {
            0 => *(sbyte*)v, 1 => *v, 2 => *(short*)v, 3 => *(ushort*)v, 4 => *(int*)v, 5 => *(uint*)v,
            6 => *(long*)v, 7 => *(ulong*)v, 8 => *(float*)v, 9 => *(double*)v, _ => null,
        };
    }

    static IgclUnits Units(byte* t, int offset) => (IgclUnits)(*(int*)(t + offset + ItemUnits));

    public IgclTelemetry? Telemetry(int gpu)
    {
        lock (_lock)
        {
            var t = stackalloc byte[TelSize];
            new Span<byte>(t, TelSize).Clear();
            *(uint*)t = TelSize;
            if (((delegate* unmanaged[Cdecl]<nint, byte*, int>)Fn("ctlPowerTelemetryGet"))(Dev(gpu), t) != 0) return null;

            var time = Item(t, TelTime);
            var energy = Item(t, TelCardEnergy) ?? Item(t, TelGpuEnergy);
            var activity = Item(t, TelActivity);
            double? power = null, usage = null;
            if (time is { } now && _last.TryGetValue(gpu, out var prev) && now > prev.Time)
            {
                var dt = now - prev.Time;
                if (energy is { } e && e >= prev.Energy) power = (e - prev.Energy) / dt;
                if (activity is { } a && a >= prev.Activity) usage = Math.Clamp((a - prev.Activity) / dt * 100, 0, 100);
            }
            if (time is { } tm) _last[gpu] = (tm, energy ?? 0, activity ?? 0);

            var volt = Item(t, TelGpuVolt);
            var voltMv = volt is { } vv ? (Units(t, TelGpuVolt) == IgclUnits.Volts ? vv * 1000 : vv) : (double?)null;
            var fan = Item(t, TelFan);
            return new IgclTelemetry(Item(t, TelClock), Item(t, TelVramClock), Item(t, TelTemp), Item(t, TelVramTemp), power, usage,
                fan is { } f && Units(t, TelFan) == IgclUnits.Rpm ? f : null, voltMv,
                t[TelPowerLimited] != 0, t[TelTempLimited] != 0, t[TelVoltLimited] != 0, t[TelUtilLimited] != 0);
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_api != 0 && NativeLibrary.TryGetExport(_lib, "ctlClose", out var close)) ((delegate* unmanaged[Cdecl]<nint, int>)close)(_api);
            _api = 0;
        }
    }
}
