using Redline.Core.Cpu;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Sim;
using Redline.Core.Storage;
using Redline.Core.Tests.Fakes;
using Redline.Engine;
using Redline.Ipc;

namespace Redline.Engine.Tests;

public sealed class FakePlan : IPowerPlan
{
    public CpuBoostSettings Current = new(CpuBoostMode.Aggressive, 100);
    public CpuBoostSettings ReadCurrent() => Current;
    public void Apply(CpuBoostSettings s) => Current = new(s.BoostMode ?? Current.BoostMode, s.MaxProcessorStatePct ?? Current.MaxProcessorStatePct);
}

public sealed class FakeCpu : ICpuMonitor
{
    public CpuState? Read() => new("AMD Ryzen 9 7900X 12-Core Processor", 65, 30, 5200, 90, false);
}

public sealed class FakeProcs : IProcessWatcher
{
    public event Action<string>? Started;
    public event Action<string>? Stopped;
    public void Start(string exe) => Started?.Invoke(exe);
    public void Stop(string exe) => Stopped?.Invoke(exe);
}

public sealed class FakeSignals : IStabilitySignals
{
    public event Action<string>? DriverReset;
    public void Reset(string id = "*") => DriverReset?.Invoke(id);
}

public sealed class EngineFixture : IAsyncDisposable
{
    public readonly TempDir Dir = new();
    public readonly FakeTime Time = new();
    public readonly FakePlan Plan = new();
    public readonly FakeProcs Procs = new();
    public readonly FakeSignals Signals = new();
    public readonly SimGpuDevice Gpu;
    public readonly List<IpcMessage> Events = new();
    public Engine Engine { get; private set; } = null!;
    int _id;

    public EngineFixture(SimGpuSpec? spec = null) => Gpu = new SimGpuDevice(spec ?? TestGpus.Spec(sixteenPin: true), "gpu0");

    public string DataDir => Dir.Path;
    public SimStressRunner Stress { get; } = new();

    public void SaveSettings(AppSettings s) => new JsonStore<AppSettings>(Path.Combine(DataDir, "settings.json"), () => new AppSettings()).Save(s);

    public async Task<Engine> StartAsync(IGpuExtraSensors? extras = null, IFrameSource? frames = null)
    {
        Engine = new Engine(new EngineOptions(DataDir), [new SimGpuBackend(Gpu)], Stress, Plan, new FakeCpu(),
            Procs, Signals, Time, extras: extras, frames: frames);
        Engine.Event += m => { lock (Events) Events.Add(m); };
        await Engine.StartAsync(default);
        return Engine;
    }

    public Task<IpcMessage> Send<T>(string type, T payload) => Engine.HandleAsync(IpcMessage.Create(++_id, type, payload), default);
    public Task<IpcMessage> Send(string type) => Engine.HandleAsync(new IpcMessage(++_id, type, null), default);

    public async Task<HelperState> State() => (await Send(MessageTypes.GetState)).PayloadAs<HelperState>()!;

    public async ValueTask DisposeAsync()
    {
        if (Engine is not null) await Engine.DisposeAsync();
        Dir.Dispose();
    }

    public static readonly SafetyOptions Safe = SafetyOptions.Default;
}
