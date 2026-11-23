namespace Redline.Core.Games;

public sealed record GameRule(string ExeName, string ProfileId);

/// <summary>Decides which profile should be active as games start and stop. Returns the profile to apply, or null for no change.</summary>
public sealed class ProfileSwitcher(IReadOnlyList<GameRule> rules, string baseProfileId)
{
    readonly List<GameRule> _running = new();   // one entry per running instance, in start order
    string _base = baseProfileId;

    public string ActiveProfileId { get; private set; } = baseProfileId;

    static string Norm(string exe) => Path.GetFileName(exe.Trim());

    GameRule? Rule(string exe) => rules.FirstOrDefault(r => string.Equals(Norm(r.ExeName), Norm(exe), StringComparison.OrdinalIgnoreCase));

    string Desired => _running.Count > 0 ? _running[^1].ProfileId : _base;

    string? Switch()
    {
        var d = Desired;
        if (d == ActiveProfileId) return null;
        ActiveProfileId = d;
        return d;
    }

    public string? OnProcessStarted(string exe)
    {
        if (Rule(exe) is not { } r) return null;
        _running.Add(r);
        return Switch();
    }

    public string? OnProcessStopped(string exe)
    {
        if (Rule(exe) is not { } r) return null;
        var i = _running.FindLastIndex(x => x == r);
        if (i < 0) return null;
        _running.RemoveAt(i);
        return Switch();
    }

    public string? SetBase(string profileId)
    {
        _base = profileId;
        return Switch();
    }
}
