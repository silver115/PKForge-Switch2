using PKForge.Domain;

namespace PKForge.Switch2.Platform;

// PKForge's platform services that have nothing behind them on the Switch 2 in phase 1 (design 7.6, 7.7): no system
// file or folder picker, no second screen, and music waits for a later update. Each answers "nothing there".

public sealed class UnavailableDocumentPicker : IDocumentPicker
{
    public ValueTask<PickedDocument?> PickSaveAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<PickedDocument?>(null);

    public ValueTask<IReadOnlyList<PickedDocument>> PickManyAsync(CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PickedDocument>>([]);
}

public sealed class UnavailableFolderPicker : IFolderPicker
{
    public ValueTask<PickedFolder?> PickFolderAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult<PickedFolder?>(null);
}

public sealed class UnavailableFolderFileAccess : IFolderFileAccess
{
    const string NoFolders = "There are no linked folders on the Switch 2.";

    public ValueTask<IReadOnlyList<PickedDocument>> ListFilesAsync(string treeId, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyList<PickedDocument>>([]);

    public ValueTask<ReadOnlyMemory<byte>> ReadFileAsync(string documentId, CancellationToken cancellationToken = default) =>
        ValueTask.FromException<ReadOnlyMemory<byte>>(new NotSupportedException(NoFolders));

    public ValueTask WriteFileAsync(string treeId, string fileName, ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default) =>
        ValueTask.FromException(new NotSupportedException(NoFolders));
}

public sealed class NoSecondaryDisplay : ISecondaryDisplayHost
{
    public bool IsAvailable => false;

    public ValueTask ShowAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    public ValueTask DismissAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
}

public sealed class SilentMusicPlayer : IMusicPlayer
{
    public IReadOnlyList<MusicTrack> Library => [];
    public bool IsPlaying => false;
    public bool IsOn => false;
    public int? CurrentIndex => null;
    public MusicOrder Order => MusicOrder.InOrder;
    public MusicPlace Place => MusicPlace.Menus;
    public bool Autostart => false;
    public int Volume => 100;

    public int Add(IReadOnlyList<PickedDocument> documents) => 0;
    public void Remove(int index) { }
    public void Clear() { }
    public void Play() { }
    public void Pause() { }
    public void Skip() { }
    public void SetOrder(MusicOrder order) { }
    public void SetAutostart(bool on) { }
    public MusicSource SourceFor(MusicPlace place) => MusicSource.Default;
    public void SetSource(MusicPlace place, MusicSource source) { }
    public void EnterPlace(MusicPlace place) { }
    public void SetVolume(int volume) { }
}
