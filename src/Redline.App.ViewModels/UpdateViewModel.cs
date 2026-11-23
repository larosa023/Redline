namespace Redline.App.ViewModels;

public sealed record UpdateOffer(string Version, string Notes);

public interface IUpdateSource
{
    Task<UpdateOffer?> CheckAsync();
    Task DownloadAndRestartAsync(UpdateOffer offer, IProgress<int> progress);
}

/// <summary>Built-in updater: quiet when there's nothing new or no internet; a one-click banner when there is.</summary>
public sealed class UpdateViewModel : ObservableObject
{
    readonly IUpdateSource _source;
    readonly Action<Action> _post;
    UpdateOffer? _offer;
    int _progress;
    bool _downloading;

    public UpdateViewModel(IUpdateSource source, Action<Action> post)
    {
        _source = source;
        _post = post;
        UpdateCommand = new AsyncRelayCommand(UpdateAsync, () => _offer is not null && !_downloading);
        DismissCommand = new RelayCommand(() => Offer = null);
    }

    public AsyncRelayCommand UpdateCommand { get; }
    public RelayCommand DismissCommand { get; }

    UpdateOffer? Offer
    {
        get => _offer;
        set
        {
            _offer = value;
            OnPropertyChanged(nameof(Available)); OnPropertyChanged(nameof(Title)); OnPropertyChanged(nameof(Notes));
            UpdateCommand.NotifyCanExecuteChanged();
        }
    }

    public bool Available => _offer is not null;
    public string Title => _offer is { } o ? $"Redline {o.Version} is ready" : "";
    public string Notes => _offer?.Notes ?? "";
    public int Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool Downloading { get => _downloading; private set => SetProperty(ref _downloading, value); }

    public async Task CheckAsync()
    {
        try
        {
            var o = await _source.CheckAsync();
            _post(() => Offer = o);
        }
        catch (Exception) { /* offline or GitHub unreachable: stay quiet */ }
    }

    async Task UpdateAsync()
    {
        if (_offer is not { } o) return;
        Downloading = true;
        try { await _source.DownloadAndRestartAsync(o, new Progress<int>(p => _post(() => Progress = p))); }
        catch (Exception) { Downloading = false; }
    }
}
