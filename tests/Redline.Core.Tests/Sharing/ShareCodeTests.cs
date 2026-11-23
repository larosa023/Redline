using Redline.Core.Cpu;
using Redline.Core.Fans;
using Redline.Core.Gpu;
using Redline.Core.Sharing;
using Redline.Core.Storage;
using Xunit;

namespace Redline.Core.Tests.Sharing;

public class ShareCodeTests
{
    const string Model = "NVIDIA GeForce RTX 4090";
    static readonly Profile P = new("send-it", "Send it", "bolt",
        new TuneSettings(100, 150, 1000, null, FanCurve.Default), CpuBoostSettings.SendIt, true, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void RoundTrip_PreservesSettings()
    {
        var r = ShareCode.Decode(ShareCode.Encode(P, Model), Model);
        Assert.Null(r.Error);
        Assert.Equal(P.Gpu, r.Profile!.Gpu);
        Assert.Equal(P.Cpu, r.Profile.Cpu);
        Assert.Equal("Send it", r.Profile.Name);
        Assert.Equal(Model, r.GpuModel);
        Assert.False(r.ModelMismatch);
    }

    [Fact]
    public void Tampered_ChecksumMismatch()
    {
        var code = ShareCode.Encode(P, Model);
        var parts = code.Split('.');
        var payload = parts[1].ToCharArray();
        payload[5] = payload[5] == 'A' ? 'B' : 'A';
        var tampered = $"{parts[0]}.{new string(payload)}.{parts[2]}";
        Assert.Equal(ShareCodeError.ChecksumMismatch, ShareCode.Decode(tampered, Model).Error);
    }

    [Fact]
    public void Garbage_Malformed()
    {
        Assert.Equal(ShareCodeError.Malformed, ShareCode.Decode("hello", Model).Error);
        Assert.Equal(ShareCodeError.Malformed, ShareCode.Decode("", Model).Error);
    }

    [Fact]
    public void RL2Prefix_UnsupportedVersion()
    {
        var code = "RL2" + ShareCode.Encode(P, Model)[3..];
        Assert.Equal(ShareCodeError.UnsupportedVersion, ShareCode.Decode(code, Model).Error);
    }

    [Fact]
    public void DifferentModel_FlagsMismatch() =>
        Assert.True(ShareCode.Decode(ShareCode.Encode(P, Model), "AMD Radeon RX 7900 XTX").ModelMismatch);

    [Fact]
    public void Imported_NotStabilityTested_NewId()
    {
        var r = ShareCode.Decode(ShareCode.Encode(P, Model), Model);
        Assert.False(r.Profile!.StabilityTested);
        Assert.NotEqual(P.Id, r.Profile.Id);
        Assert.Null(r.Profile.TestedAt);
    }

    [Fact]
    public void Code_IsUnder200Chars_ForTypicalProfile() => Assert.True(ShareCode.Encode(P with { Gpu = P.Gpu with { Fan = null } }, Model).Length < 200);

    [Fact]
    public void Whitespace_Tolerated() =>
        Assert.Null(ShareCode.Decode("  " + ShareCode.Encode(P, Model) + "\n", Model).Error);
}
