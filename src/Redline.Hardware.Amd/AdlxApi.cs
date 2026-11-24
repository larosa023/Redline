using System.Runtime.InteropServices;

namespace Redline.Hardware.Amd;

/// <summary>
/// ADLX (amdadlx64.dll, installed with AMD Software: Adrenalin) through its C vtables. Slot numbers come from the
/// SDK's C headers (GPUOpen-LibrariesAndSDKs/ADLX, MIT). Every call is checked; any failure becomes an
/// <see cref="AdlxException"/> and the device mapping treats it as "not supported".
/// </summary>
public sealed unsafe class AdlxApi : IAdlxApi, IDisposable
{
    // IADLXSystem
    const int SysGetGPUs = 1, SysGetGPUTuningServices = 8, SysGetPerformanceMonitoringServices = 9;
    // IADLXInterface base (all ref-counted interfaces)
    const int Release = 1, QueryInterface = 2;
    // IADLXGPUList
    const int ListSize = 3, ListAtGPU = 11;
    // IADLXGPU
    const int GpuName = 7, GpuTotalVram = 11, GpuUniqueId = 18;
    // IADLXGPUTuningServices
    const int TsIsAtFactory = 4, TsResetToFactory = 5, TsIsSupportedGfx = 8, TsIsSupportedVram = 9, TsIsSupportedPower = 11,
        TsGetGfx = 14, TsGetVram = 15, TsGetPower = 17;
    // IADLXManualGraphicsTuning2
    const int G2MaxFreqRange = 6, G2GetMaxFreq = 7, G2SetMaxFreq = 8, G2VoltRange = 9, G2GetVolt = 10, G2SetVolt = 11;
    // IADLXManualVRAMTuning2
    const int V2MaxFreqRange = 7, V2GetMaxFreq = 8, V2SetMaxFreq = 9;
    // IADLXManualPowerTuning
    const int PwRange = 3, PwGet = 4, PwSet = 5;
    // IADLXPerformanceMonitoringServices / IADLXGPUMetrics
    const int PmGetCurrentGPUMetrics = 18;
    const int MUsage = 4, MClock = 5, MVramClock = 6, MTemp = 7, MHotspot = 8, MPower = 9, MBoardPower = 10, MFan = 11, MVram = 12, MVolt = 13;

    const ulong Version = (2UL << 48) | (0UL << 32) | (0UL << 16) | 125UL;   // ADLX SDK 2.0.0.125

    [StructLayout(LayoutKind.Sequential)] struct IntRange { public int Min, Max, Step; }

    readonly object _lock = new();
    nint _lib;
    void* _system, _tuning, _perf;
    readonly List<nint> _gpus = new();
    readonly Dictionary<int, (nint Gfx2, nint Vram2, nint Power)> _tuners = new();
    List<AdlxGpuInfo>? _infos;

    public bool TryLoad()
    {
        lock (_lock)
        {
            if (_system != null) return true;
            if (!OperatingSystem.IsWindows() || !NativeLibrary.TryLoad("amdadlx64.dll", out _lib)) return false;
            void* sys = null;
            foreach (var name in new[] { "ADLXInitialize", "ADLXInitializeWithIncompatibleDriver" })
            {
                if (!NativeLibrary.TryGetExport(_lib, name, out var fn)) continue;
                var init = (delegate* unmanaged[Cdecl]<ulong, void**, int>)fn;
                if (init(Version, &sys) == 0 && sys != null) break;
                sys = null;
            }
            if (sys == null) return false;
            _system = sys;
            void* list = null, tuning = null, perf = null;
            if (((delegate* unmanaged<void*, void**, int>)Slot(_system, SysGetGPUs))(_system, &list) != 0 || list == null) return false;
            var n = ((delegate* unmanaged<void*, uint>)Slot(list, ListSize))(list);
            for (uint i = 0; i < n; i++)
            {
                void* gpu = null;
                if (((delegate* unmanaged<void*, uint, void**, int>)Slot(list, ListAtGPU))(list, i, &gpu) == 0 && gpu != null) _gpus.Add((nint)gpu);
            }
            Rel(list);
            if (((delegate* unmanaged<void*, void**, int>)Slot(_system, SysGetGPUTuningServices))(_system, &tuning) == 0) _tuning = tuning;
            if (((delegate* unmanaged<void*, void**, int>)Slot(_system, SysGetPerformanceMonitoringServices))(_system, &perf) == 0) _perf = perf;
            return true;
        }
    }

    /// <summary>Function pointer in slot <paramref name="slot"/> of the object's vtable.</summary>
    static nint Slot(void* obj, int slot) => (nint)(*(void***)obj)[slot];

    static void Rel(void* obj) { if (obj != null) ((delegate* unmanaged<void*, int>)Slot(obj, Release))(obj); }

    static void Check(int r, string what) { if (r != 0) throw new AdlxException($"ADLX {what} failed ({r})"); }

    void* Gpu(int i)
    {
        if (_system == null || i < 0 || i >= _gpus.Count) throw new AdlxException("ADLX GPU not found");
        return (void*)_gpus[i];
    }

    void* Tuning => _tuning != null ? _tuning : throw new AdlxException("ADLX tuning services unavailable");

    public IReadOnlyList<AdlxGpuInfo> Gpus()
    {
        lock (_lock)
        {
            if (_infos is not null) return _infos;
            var list = new List<AdlxGpuInfo>();
            for (var i = 0; i < _gpus.Count; i++)
            {
                var g = (void*)_gpus[i];
                byte* name = null; uint vram = 0; int uid = 0;
                ((delegate* unmanaged<void*, byte**, int>)Slot(g, GpuName))(g, &name);
                ((delegate* unmanaged<void*, uint*, int>)Slot(g, GpuTotalVram))(g, &vram);
                ((delegate* unmanaged<void*, int*, int>)Slot(g, GpuUniqueId))(g, &uid);
                var text = name != null ? Marshal.PtrToStringUTF8((nint)name) ?? "AMD Radeon" : "AMD Radeon";
                list.Add(new AdlxGpuInfo(i, text, uid.ToString(), (int)vram, ""));
            }
            return _infos = list;
        }
    }

    bool Supported(int gpu, int slot)
    {
        byte b = 0;
        var t = Tuning;
        return ((delegate* unmanaged<void*, void*, byte*, int>)Slot(t, slot))(t, Gpu(gpu), &b) == 0 && b != 0;
    }

    public AdlxSupport Support(int gpu)
    {
        lock (_lock)
        {
            var gfx = Supported(gpu, TsIsSupportedGfx);
            var t = Tuners(gpu);
            return new AdlxSupport(gfx, gfx && t.Gfx2 != 0, Supported(gpu, TsIsSupportedVram) && t.Vram2 != 0, Supported(gpu, TsIsSupportedPower) && t.Power != 0);
        }
    }

    (nint Gfx2, nint Vram2, nint Power) Tuners(int gpu)
    {
        if (_tuners.TryGetValue(gpu, out var t)) return t;
        t = (Query(gpu, TsGetGfx, "IADLXManualGraphicsTuning2"), Query(gpu, TsGetVram, "IADLXManualVRAMTuning2"), Query(gpu, TsGetPower, "IADLXManualPowerTuning"));
        _tuners[gpu] = t;
        return t;
    }

    nint Query(int gpu, int getSlot, string iid)
    {
        void* tuning = Tuning, iface = null, typed = null;
        if (((delegate* unmanaged<void*, void*, void**, int>)Slot(tuning, getSlot))(tuning, Gpu(gpu), &iface) != 0 || iface == null) return 0;
        fixed (char* id = iid)
        {
            var r = ((delegate* unmanaged<void*, char*, void**, int>)Slot(iface, QueryInterface))(iface, id, &typed);
            Rel(iface);
            return r == 0 ? (nint)typed : 0;
        }
    }

    static AdlxRange? Range(nint obj, int slot)
    {
        if (obj == 0) return null;
        IntRange r;
        return ((delegate* unmanaged<void*, IntRange*, int>)Slot((void*)obj, slot))((void*)obj, &r) == 0 ? new AdlxRange(r.Min, r.Max, r.Step) : null;
    }

    static int? GetInt(nint obj, int slot)
    {
        if (obj == 0) return null;
        int v;
        return ((delegate* unmanaged<void*, int*, int>)Slot((void*)obj, slot))((void*)obj, &v) == 0 ? v : null;
    }

    static void SetInt(nint obj, int slot, int v, string what)
    {
        if (obj == 0) throw new AdlxException($"ADLX {what} unavailable");
        Check(((delegate* unmanaged<void*, int, int>)Slot((void*)obj, slot))((void*)obj, v), what);
    }

    public AdlxRanges Ranges(int gpu)
    {
        lock (_lock)
        {
            var t = Tuners(gpu);
            return new AdlxRanges(Range(t.Gfx2, G2MaxFreqRange), Range(t.Gfx2, G2VoltRange), Range(t.Vram2, V2MaxFreqRange), Range(t.Power, PwRange));
        }
    }

    public bool IsAtFactory(int gpu)
    {
        lock (_lock)
        {
            byte b = 0;
            var t = Tuning;
            Check(((delegate* unmanaged<void*, void*, byte*, int>)Slot(t, TsIsAtFactory))(t, Gpu(gpu), &b), "IsAtFactory");
            return b != 0;
        }
    }

    public AdlxValues Read(int gpu)
    {
        lock (_lock)
        {
            var t = Tuners(gpu);
            return new AdlxValues(GetInt(t.Gfx2, G2GetMaxFreq), GetInt(t.Gfx2, G2GetVolt), GetInt(t.Vram2, V2GetMaxFreq), GetInt(t.Power, PwGet));
        }
    }

    public void Write(int gpu, AdlxValues v)
    {
        lock (_lock)
        {
            var t = Tuners(gpu);
            if (v.PowerPct is { } p) SetInt(t.Power, PwSet, p, "SetPowerLimit");
            if (v.GpuVoltageMv is { } mv) SetInt(t.Gfx2, G2SetVolt, mv, "SetGPUVoltage");
            if (v.GpuMaxFreqMhz is { } f) SetInt(t.Gfx2, G2SetMaxFreq, f, "SetGPUMaxFrequency");
            if (v.VramMaxFreqMhz is { } m) SetInt(t.Vram2, V2SetMaxFreq, m, "SetMaxVRAMFrequency");
        }
    }

    public void ResetToFactory(int gpu)
    {
        lock (_lock)
        {
            var t = Tuning;
            Check(((delegate* unmanaged<void*, void*, int>)Slot(t, TsResetToFactory))(t, Gpu(gpu)), "ResetToFactory");
        }
    }

    public AdlxMetrics? Metrics(int gpu)
    {
        lock (_lock)
        {
            if (_perf == null) return null;
            void* mp = null;
            if (((delegate* unmanaged<void*, void*, void**, int>)Slot(_perf, PmGetCurrentGPUMetrics))(_perf, Gpu(gpu), &mp) != 0 || mp == null) return null;
            var m = (nint)mp;
            try
            {
                return new AdlxMetrics(D(m, MUsage) ?? 0, I(m, MClock) ?? 0, I(m, MVramClock) ?? 0, D(m, MTemp) ?? 0, D(m, MHotspot), D(m, MPower) ?? 0,
                    I(m, MFan), I(m, MVram) ?? 0, I(m, MVolt), D(m, MBoardPower));
            }
            finally { Rel(mp); }
        }
    }

    static double? D(nint obj, int slot) { double d; return ((delegate* unmanaged<void*, double*, int>)Slot((void*)obj, slot))((void*)obj, &d) == 0 ? d : null; }
    static int? I(nint obj, int slot) { int i; return ((delegate* unmanaged<void*, int*, int>)Slot((void*)obj, slot))((void*)obj, &i) == 0 ? i : null; }

    public void Dispose()
    {
        // If a driver call is stuck (it holds the lock), don't hang the helper's shutdown waiting for it.
        if (!Monitor.TryEnter(_lock, TimeSpan.FromSeconds(2))) return;
        try
        {
            foreach (var t in _tuners.Values) { Rel((void*)t.Gfx2); Rel((void*)t.Vram2); Rel((void*)t.Power); }
            _tuners.Clear();
            foreach (var g in _gpus) Rel((void*)g);
            _gpus.Clear();
            Rel(_tuning); Rel(_perf);
            _tuning = _perf = null;
            if (_system != null && NativeLibrary.TryGetExport(_lib, "ADLXTerminate", out var term)) ((delegate* unmanaged[Cdecl]<int>)term)();
            _system = null;
        }
        finally { Monitor.Exit(_lock); }
    }
}
