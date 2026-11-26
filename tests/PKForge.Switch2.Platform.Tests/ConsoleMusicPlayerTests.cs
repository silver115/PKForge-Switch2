using Microsoft.Maui.Storage;
using PKForge.Domain;
using Xunit;

namespace PKForge.Switch2.Platform.Tests;

public sealed class ConsoleMusicPlayerTests : IDisposable
{
    readonly string _folder = Directory.CreateTempSubdirectory("pkf-music-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    sealed class FakeOutput : IMusicOutput
    {
        public readonly List<string> Opened = [];
        public HashSet<string> Unplayable { get; } = [];
        public float Gain;
        public MusicOutputState State { get; set; }
        public int PositionMs { get; set; }

        public bool Open(string path, int positionMs)
        {
            if (Unplayable.Contains(Path.GetFileName(path))) return false;
            Opened.Add(Path.GetFileName(path));
            State = MusicOutputState.Paused;
            return true;
        }

        public void Play(bool playing) => State = playing ? MusicOutputState.Playing : MusicOutputState.Paused;
        public void Stop() => State = MusicOutputState.None;
        public void SetGain(float gain) => Gain = gain;
    }

    sealed class FakePreferences : IPreferences
    {
        readonly Dictionary<string, object?> _values = [];
        public bool ContainsKey(string key, string? sharedName = null) => _values.ContainsKey(key);
        public void Remove(string key, string? sharedName = null) => _values.Remove(key);
        public void Clear(string? sharedName = null) => _values.Clear();
        public void Set<T>(string key, T value, string? sharedName = null) => _values[key] = value;
        public T Get<T>(string key, T defaultValue, string? sharedName = null) => _values.TryGetValue(key, out var value) ? (T)value! : defaultValue;
    }

    void Touch(string relative)
    {
        var path = Path.Combine(_folder, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
    }

    ConsoleMusicPlayer Player(FakeOutput output) => new(output, _folder, new FakePreferences());

    // In path order, so an album's tracks in a subfolder stay together.
    [Fact]
    public void Library_IsTheFoldersMusicFiles_InPathOrder()
    {
        Touch("b.MP3");
        Touch("a.flac");
        Touch("album/2 second.ogg");
        Touch("album/1 first.ogg");
        Touch("d.wav");
        Touch("cover.jpg");
        Touch("notes.txt");

        var player = Player(new FakeOutput());

        Assert.Equal(["a", "1 first", "2 second", "b", "d"], player.Library.Select(track => track.Title));
    }

    [Fact]
    public void Add_ReadsTheFolderAgain_AndCountsTheNewTracks()
    {
        Touch("a.mp3");
        var player = Player(new FakeOutput());
        Touch("b.mp3");
        Touch("c.mp3");

        Assert.Equal(2, player.Add([]));
        Assert.Equal(3, player.Library.Count);
        Assert.Equal(0, player.Add([]));
    }

    [Fact]
    public void Play_OpensTheFirstTrack_AndPlaysIt()
    {
        Touch("a.mp3");
        Touch("b.mp3");
        var output = new FakeOutput();
        var player = Player(output);

        player.Play();

        Assert.Equal(["a.mp3"], output.Opened);
        Assert.True(player.IsPlaying);
        Assert.Equal(0, player.CurrentIndex);
    }

    [Fact]
    public void Tick_AfterATrackEnds_PlaysTheNextOne()
    {
        Touch("a.mp3");
        Touch("b.mp3");
        var output = new FakeOutput();
        var player = Player(output);
        player.Play();

        output.State = MusicOutputState.Ended;
        player.Tick();

        Assert.Equal(["a.mp3", "b.mp3"], output.Opened);
        Assert.Equal(1, player.CurrentIndex);
        Assert.True(player.IsPlaying);
    }

    [Fact]
    public void Play_SkipsATrackThatCannotBePlayed()
    {
        Touch("a.mp3");
        Touch("b.mp3");
        var output = new FakeOutput();
        output.Unplayable.Add("a.mp3");
        var player = Player(output);

        player.Play();

        Assert.Equal(["b.mp3"], output.Opened);
        Assert.Equal(1, player.CurrentIndex);
    }

    [Fact]
    public void SetVolume_IsHeardAtOnce()
    {
        Touch("a.mp3");
        var output = new FakeOutput();
        var player = Player(output);
        player.Play();

        player.SetVolume(50);

        Assert.Equal(MusicPlaces.Gain(50), output.Gain, 3);
        Assert.Equal(50, player.Volume);
    }

    [Fact]
    public void Library_WithNoFolder_IsEmpty_AndPlayDoesNothing()
    {
        var output = new FakeOutput();
        var player = new ConsoleMusicPlayer(output, Path.Combine(_folder, "missing"), new FakePreferences());

        player.Play();

        Assert.Empty(player.Library);
        Assert.Empty(output.Opened);
    }
}
