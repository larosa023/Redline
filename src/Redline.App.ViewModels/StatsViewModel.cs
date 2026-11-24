using System.Collections.ObjectModel;
using Redline.Core.Stats;
using Redline.Ipc;

namespace Redline.App.ViewModels;

public sealed record SeriesItem(string Metric, string Unit, IReadOnlyList<double> Values, string Summary, string Latest);

/// <summary>Stats page: every sensor over the last 5 minutes, hour or day, with min/avg/max.</summary>
public sealed class StatsViewModel(ShellViewModel shell) : ObservableObject
{
    StatsRange _range = StatsRange.FiveMinutes;
    public ObservableCollection<SeriesItem> Series { get; } = new();
    public StatsRange Range { get => _range; set { if (SetProperty(ref _range, value)) _ = RefreshAsync(); } }

    static string F(double v) => Math.Abs(v) >= 100 ? $"{v:N0}" : $"{v:0.#}";

    public async Task RefreshAsync()
    {
        if (await shell.SendAsync(MessageTypes.GetSeries, new SeriesRequest(_range)) is not { } r || r.PayloadAs<SeriesResponse>() is not { } resp) return;
        var items = resp.Series.Select(s => new SeriesItem(s.Metric, s.Unit, s.Points.Select(p => p.Value).ToList(),
            $"Min {F(s.Min)} · Avg {F(s.Avg)} · Max {F(s.Max)} {s.Unit}", $"{F(s.Points[^1].Value)} {s.Unit}")).ToList();
        Series.Clear();
        foreach (var i in items) Series.Add(i);
    }
}
