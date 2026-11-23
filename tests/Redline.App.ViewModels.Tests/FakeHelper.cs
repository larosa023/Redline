using System.Text.Json;
using Redline.App.ViewModels;
using Redline.Core.Drivers;
using Redline.Core.Gpu;
using Redline.Core.Safety;
using Redline.Core.Storage;
using Redline.Ipc;

namespace Redline.App.ViewModels.Tests;

public sealed class FakeHelper : IHelperClient
{
    public readonly List<(string Type, object? Payload)> Sent = new();
    public HelperState State;
    public bool Available = true;
    public Func<string, object?, IpcMessage?>? Respond;
    public event Action<IpcMessage>? EventReceived;
    public event Action? Disconnected;
    public bool Connected => Available;

    public FakeHelper(Capabilities? caps = null, string? activeProfile = "factory", TuneSettings? active = null)
    {
        caps ??= Caps(sixteenPin: true);
        State = new HelperState("1.0.0",
            [new DeviceState("gpu0", "NVIDIA GeForce RTX 4090", Vendor.Nvidia, caps, active ?? TuneSettings.Factory, activeProfile,
                new DriverVerdict(DriverStatus.Ok, null), true, true)],
            new Core.Cpu.CpuState("AMD Ryzen 9 7900X", 65, 30, 5200, 90, false), ProfileStore.Defaults(), new AppSettings(), true,
            StartupDecision.Clean, false, null, false);
    }

    public static Capabilities Caps(bool sixteenPin = false) => new(new Dictionary<Control, ControlRange>
    {
        [Control.PowerLimit] = new(34, 133, 1, 100),
        [Control.CoreOffset] = new(-1000, 1000, 15, 0),
        [Control.MemoryOffset] = new(-2000, 3000, 50, 0),
        [Control.FanSpeed] = new(30, 100, 1, 0),
    }, true, sixteenPin);

    public Task ConnectAsync(TimeSpan timeout) => Available ? Task.CompletedTask : throw new HelperUnavailableException("Redline Helper isn't running.");

    public Task<IpcMessage> RequestAsync(string type, object? payload)
    {
        if (!Available) throw new HelperUnavailableException("Redline Helper isn't running.");
        Sent.Add((type, payload));
        if (Respond?.Invoke(type, payload) is { } r) return Task.FromResult(r);
        if (type == MessageTypes.UpdateSettings) State = State with { Settings = ((SettingsRequest)payload!).Settings };
        return Task.FromResult(type switch
        {
            MessageTypes.GetState => IpcMessage.OkFor(0, State),
            MessageTypes.StageApply => IpcMessage.OkFor(0, new StageApplyResponse(Guid.NewGuid(), ((StageApplyRequest)payload!).Settings, [], 15)),
            _ => IpcMessage.OkFor(0, State),
        });
    }

    public void Raise<T>(string type, T payload) => EventReceived?.Invoke(IpcMessage.Create(0, type, payload));
    public void Drop() { Available = false; Disconnected?.Invoke(); }

    public static readonly Action<Action> Inline = a => a();
}
