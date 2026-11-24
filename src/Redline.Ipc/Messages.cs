using System.Text.Json;
using Redline.Core.AutoTune;
using Redline.Core.Cpu;
using Redline.Core.Drivers;
using Redline.Core.Gpu;
using Redline.Core.History;
using Redline.Core.Safety;
using Redline.Core.Stats;
using Redline.Core.Storage;

namespace Redline.Ipc;

public static class MessageTypes
{
    // Requests (app → helper)
    public const string GetState = "GetState", SubscribeTelemetry = "SubscribeTelemetry", UnsubscribeTelemetry = "UnsubscribeTelemetry",
        StageApply = "StageApply", ConfirmKeep = "ConfirmKeep", RevertNow = "RevertNow", ApplyProfile = "ApplyProfile",
        ResetFactory = "ResetFactory", Undo = "Undo", SaveProfile = "SaveProfile", AddProfile = "AddProfile",
        DeleteProfile = "DeleteProfile", RenameProfile = "RenameProfile", StartAutoTune = "StartAutoTune",
        CancelAutoTune = "CancelAutoTune", RunBeforeAfter = "RunBeforeAfter", UpdateSettings = "UpdateSettings",
        GetHistory = "GetHistory", ExportReport = "ExportReport", AcceptIntelWaiver = "AcceptIntelWaiver",
        GetWeekly = "GetWeekly", Quit = "Quit", GetSeries = "GetSeries", ImportProfile = "ImportProfile";

    // Responses
    public const string Ok = "Ok", ErrorResponse = "Error";

    // Events (helper → app)
    public const string State = "State", Telemetry = "Telemetry", AutoTuneProgress = "AutoTuneProgress",
        KeepOrRevertTick = "KeepOrRevertTick", GuardAction = "GuardAction", Toast = "Toast", FpsSample = "FpsSample",
        BeforeAfterResult = "BeforeAfterResult";

    public static IReadOnlyList<string> Requests { get; } =
    [
        GetState, SubscribeTelemetry, UnsubscribeTelemetry, StageApply, ConfirmKeep, RevertNow, ApplyProfile, ResetFactory, Undo,
        SaveProfile, AddProfile, DeleteProfile, RenameProfile, StartAutoTune, CancelAutoTune, RunBeforeAfter, UpdateSettings,
        GetHistory, ExportReport, AcceptIntelWaiver, GetWeekly, Quit, GetSeries, ImportProfile,
    ];

    public static IReadOnlyList<string> Events { get; } = [State, Telemetry, AutoTuneProgress, KeepOrRevertTick, GuardAction, Toast, FpsSample, BeforeAfterResult];

    public static IReadOnlySet<string> All { get; } = Requests.Concat(Events).Append(Ok).Append(ErrorResponse).ToHashSet();
}

public sealed record IpcMessage(int Id, string Type, JsonElement? Payload)
{
    public static IpcMessage Create<T>(int id, string type, T payload) =>
        new(id, type, JsonSerializer.SerializeToElement(payload, PipeProtocol.Options));

    public static IpcMessage OkFor(int id, object? payload = null) =>
        new(id, MessageTypes.Ok, payload is null ? null : JsonSerializer.SerializeToElement(payload, payload.GetType(), PipeProtocol.Options));

    public static IpcMessage Error(int id, string message) => Create(id, MessageTypes.ErrorResponse, new ErrorPayload(message));

    public T? PayloadAs<T>() => Payload is { } p && p.ValueKind != JsonValueKind.Null ? p.Deserialize<T>(PipeProtocol.Options) : default;
}

// ---- Payloads ----
public sealed record ErrorPayload(string Message);
public sealed record DeviceRequest(string DeviceId);
public sealed record StageApplyRequest(string DeviceId, TuneSettings Settings, SafetyOptions Safety);
public sealed record StageApplyResponse(Guid Token, TuneSettings Applied, IReadOnlyList<SafetyIssue> Issues, int Seconds);
public sealed record TokenRequest(Guid Token);
public sealed record ApplyProfileRequest(string DeviceId, string ProfileId);
public sealed record ProfileRequest(Profile Profile);
public sealed record AddProfileRequest(string Name, string Icon);
public sealed record IdRequest(string Id);
public sealed record RenameProfileRequest(string Id, string Name);
public sealed record StartAutoTuneRequest(string DeviceId, AutoTuneOptions Options, string TargetProfileId);
public sealed record BeforeAfterRequest(string DeviceId, string ProfileId);
public sealed record SettingsRequest(AppSettings Settings);
public sealed record ExportReportRequest(string Path);
public sealed record QuitRequest(bool KeepTune);
public sealed record HistoryResponse(IReadOnlyList<HistoryEntry> Entries);

// ---- State & events ----
public sealed record DeviceState(string Id, string Name, Vendor Vendor, Capabilities Caps, TuneSettings Active, string? ActiveProfileId,
    DriverVerdict Driver, bool IntelWaiverAccepted, bool Available);

public sealed record HelperState(string HelperVersion, IReadOnlyList<DeviceState> Devices, CpuState? Cpu, IReadOnlyList<Profile> Profiles,
    AppSettings Settings, bool PawnIoInstalled, StartupDecision LastStartup, bool AutoTuneRunning, double? PersonalBestMhz, bool NewPersonalBest);

public sealed record TelemetryEvent(string DeviceId, Telemetry? Gpu, CpuState? Cpu, bool GameRunning);
public sealed record KeepOrRevertTickEvent(Guid Token, int SecondsLeft, bool Done, KeepOrRevertOutcome? Outcome);
public sealed record AutoTuneProgressEvent(string DeviceId, AutoTuneProgress Progress, AutoTuneResult? Result);
public sealed record GuardActionEvent(string DeviceId, string Action, string Message);
public sealed record ToastEvent(string Title, string Message, string Severity);
public sealed record FpsSampleEvent(string ProcessName, double AvgFps, double OnePercentLowFps);
public sealed record WeeklyResponse(WeeklySummary Summary);

public sealed record SeriesRequest(StatsRange Range);
public sealed record SeriesResponse(IReadOnlyList<StatSeries> Series);
public sealed record ImportProfileRequest(Profile Profile);
public sealed record BeforeAfterResultEvent(string ProfileId, BeforeAfter? Result, string? Error);
