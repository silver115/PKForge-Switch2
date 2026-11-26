using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Maui.Dispatching;
using Microsoft.Maui.Storage;
using PKForge.Domain;

namespace PKForge.Switch2.Platform;

public enum MusicOutputState { None, Playing, Paused, Ended }

/// <summary>Plays one music file at a time: the console's (switch2/pkfhost/pkf_music.c), or a test's.</summary>
public interface IMusicOutput
{
    /// <summary>Opens a file, paused, at a position; false when it cannot be played.</summary>
    bool Open(string path, int positionMs);
    void Play(bool playing);
    void Stop();
    /// <summary>0..1, heard at once.</summary>
    void SetGain(float gain);
    int PositionMs { get; }
    MusicOutputState State { get; }
}

/// <summary>The console's music output: decoded and played by the native host, on an audio thread of its own.</summary>
public sealed unsafe class NativeMusicOutput : IMusicOutput
{
    private const string Lib = "pkfhost";

    [DllImport(Lib)] private static extern long pkf_music_open(byte* path, long startMs);
    [DllImport(Lib)] private static extern void pkf_music_play(int playing);
    [DllImport(Lib)] private static extern void pkf_music_stop();
    [DllImport(Lib)] private static extern void pkf_music_gain(int perMille);
    [DllImport(Lib)] private static extern long pkf_music_position_ms();
    [DllImport(Lib)] private static extern int pkf_music_state();
    [DllImport(Lib)] private static extern void pkf_music_shutdown();

    public bool Open(string path, int positionMs)
    {
        var bytes = Encoding.UTF8.GetBytes(path + "\0");
        fixed (byte* pPath = bytes)
            return pkf_music_open(pPath, positionMs) == 0;
    }

    public void Play(bool playing) => pkf_music_play(playing ? 1 : 0);
    public void Stop() => pkf_music_stop();
    public void SetGain(float gain) => pkf_music_gain((int)Math.Round(Math.Clamp(gain, 0, 1) * 1000));
    public int PositionMs => (int)pkf_music_position_ms();
    public MusicOutputState State => (MusicOutputState)pkf_music_state();

    /// <summary>Ends the audio thread; called once, as the app exits.</summary>
    public static void Shutdown() => pkf_music_shutdown();
}

/// <summary>
/// The console's background music, behaving as PKForge's Android MusicPlayer does: each place's music, crossfades
/// between places, the spot each place's music was left at, shuffle, volume and autostart, kept in the same
/// preferences. The library is the music folder on the SD card (MP3, FLAC, WAV, Ogg Vorbis, subfolders too), read
/// again when the music menu asks for tracks: the console has no file picker. Track names are file names, so nothing
/// is opened until it plays. <see cref="Tick"/> moves on when a track ends.
/// </summary>
public sealed class ConsoleMusicPlayer : IMusicPlayer, IDisposable
{
    private const int FadeSteps = 12, FadeStepMs = 50;
    private static readonly string[] Extensions = [".mp3", ".flac", ".wav", ".ogg", ".oga"];

    private readonly IMusicOutput _output;
    private readonly IPreferences _preferences;
    private readonly IDispatcherTimer? _timer;
    private readonly List<MusicTrack> _library = [];
    private readonly Random _rng = new();
    private readonly Dictionary<string, (int Index, int Position)> _resume = new();
    private bool _open;
    private int? _index;
    private bool _on;
    private int _fade, _failures;
    private float _level = 1;
    private MusicPlace _place = MusicPlace.Menus;
    // What the place plays (null: silence), and what the output is playing from: they differ during a crossfade.
    private int[]? _playlist;
    private int[]? _playerList;

    /// <param name="dispatcher">The UI loop, for the timer that notices a track's end; null in tests, which call <see cref="Tick"/>.</param>
    public ConsoleMusicPlayer(IMusicOutput output, string folder, IPreferences preferences, IDispatcher? dispatcher = null)
    {
        _output = output;
        Folder = folder;
        _preferences = preferences;
        Scan();
        if (dispatcher is null) return;
        _timer = dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(250);
        _timer.Tick += (_, _) => Tick();
        _timer.Start();
    }

    /// <summary>Where the library's files are: sdmc:/switch/PKForge/music on the console.</summary>
    public string Folder { get; }

    public IReadOnlyList<MusicTrack> Library => _library;
    public bool IsPlaying => _output.State == MusicOutputState.Playing;
    public bool IsOn => _on;
    public int? CurrentIndex => _index;
    public MusicPlace Place => _place;

    public MusicOrder Order
    {
        get => Enum.Parse<MusicOrder>(_preferences.Get("music_order", nameof(MusicOrder.InOrder)));
        private set => _preferences.Set("music_order", value.ToString());
    }

    public bool Autostart
    {
        get => _preferences.Get("music_autostart", false);
        private set => _preferences.Set("music_autostart", value);
    }

    public int Volume => MusicPlaces.ClampVolume(_preferences.Get("music_volume", MusicPlaces.DefaultVolume));

    /// <summary>Set when a track cannot be played; the music menu shows it, so a failure is never silent.</summary>
    public string? LastError { get; private set; }

    public void SetVolume(int volume)
    {
        _preferences.Set("music_volume", MusicPlaces.ClampVolume(volume));
        // Heard at once; a fade in progress picks the new gain up on its next step.
        if (_open) Apply(_level);
    }

    /// <summary>The console has no file picker, so the music folder is read again; returns how many tracks are new.</summary>
    public int Add(IReadOnlyList<PickedDocument> documents)
    {
        var current = _index is { } index ? _library[index].DocumentId : null;
        var added = Scan();
        _index = current is null ? null : _library.FindIndex(track => track.DocumentId == current) is >= 0 and var at ? at : null;
        if (current is not null && _index is null) StopInternal();
        // Indices moved under the playlists: work them out again.
        _resume.Clear();
        _playlist = Playlist();
        if (_playerList is not null) _playerList = _playlist;
        return added;
    }

    public void Remove(int index)
    {
        if ((uint)index >= _library.Count) return;
        if (_index == index) { StopInternal(); _index = null; }
        else if (_index > index) _index--;
        _library.RemoveAt(index);
        _resume.Clear();
        _playlist = Playlist();
        if (_playerList is not null) _playerList = _playlist;
    }

    public void Clear()
    {
        StopInternal();
        _library.Clear();
        _index = null;
        _resume.Clear();
        _playlist = null;
    }

    public void Play()
    {
        _on = true;
        _playlist = Playlist();
        // A silent place stays silent; the music comes back where there is some.
        if (_playlist is null) return;
        _fade++;
        if (_open && _index is not null && _playerList is not null && SameList(_playerList, _playlist))
        {
            Apply(1);
            _output.Play(true);
            return;
        }
        Start(_playlist, fadeIn: false);
    }

    public void Pause()
    {
        _on = false;
        _fade++;
        _output.Play(false);
    }

    public void Skip()
    {
        var list = _playlist ?? Playlist();
        if (list is null) return;
        _fade++;
        var at = _index is { } index ? Array.IndexOf(list, index) : -1;
        var next = Shuffling && list.Length > 1
            ? list.Where(i => i != _index).ElementAt(_rng.Next(list.Length - (at >= 0 ? 1 : 0)))
            : list[(at + 1) % list.Length];
        _on = true;
        _playerList = list;
        PlayIndex(next, 0, 1);
    }

    public void SetOrder(MusicOrder order) => Order = order;

    public void SetAutostart(bool on) => Autostart = on;

    public MusicSource SourceFor(MusicPlace place) => MusicSource.Parse(_preferences.Get(SourceKey(place), ""));

    public void SetSource(MusicPlace place, MusicSource source)
    {
        _preferences.Set(SourceKey(place), source.Serialize());
        EnterPlace(_place);
    }

    public void EnterPlace(MusicPlace place)
    {
        _place = place;
        var list = Playlist();
        if (_playlist is not null && list is not null && SameList(_playlist, list)) return;
        _playlist = list;
        if (!_on) return;
        _ = CrossfadeAsync(list);
    }

    /// <summary>Starts the music as the app opens, when the player asked for that.</summary>
    public void MaybeAutostart()
    {
        if (Autostart && _library.Count > 0) Play();
    }

    /// <summary>Called four times a second on the UI loop: a track that played to its end gives way to the next.</summary>
    public void Tick()
    {
        if (_open && _output.State == MusicOutputState.Ended) Skip();
    }

    public void Dispose()
    {
        _timer?.Stop();
        StopInternal();
    }

    private bool Shuffling => MusicPlaces.Shuffles(_place, SourceFor, Order);

    private int[]? Playlist() => MusicPlaces.Playlist(_place, SourceFor, _library);

    private static string SourceKey(MusicPlace place) => $"music_place_{place}";

    private static bool SameList(int[] a, int[] b) => a.AsSpan().SequenceEqual(b);

    private static string Key(int[] list) => string.Join(",", list);

    // Fades work in 0..1 and the player's volume scales them.
    private void Apply(float level)
    {
        _level = level;
        _output.SetGain(level * MusicPlaces.Gain(Volume));
    }

    // The folder's music files, subfolders included, in path order (an album's tracks stay together); returns how many
    // were not in the library before.
    private int Scan()
    {
        var before = _library.Select(track => track.DocumentId).ToHashSet(StringComparer.Ordinal);
        var files = Directory.Exists(Folder)
            ? Directory.EnumerateFiles(Folder, "*", SearchOption.AllDirectories)
                .Where(file => Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList()
            : [];
        _library.Clear();
        _library.AddRange(files.Select(file => new MusicTrack(file, Path.GetFileNameWithoutExtension(file))));
        return files.Count(file => !before.Contains(file));
    }

    // The old music fades out, its spot is kept, and the new one fades in from where it was left.
    private async Task CrossfadeAsync(int[]? list)
    {
        var fade = ++_fade;
        if (_open && _playerList is not null && _index is { } index)
        {
            _resume[Key(_playerList)] = (index, _output.PositionMs);
            if (IsPlaying)
                for (var step = FadeSteps - 1; step >= 0; step--)
                {
                    Apply((float)step / FadeSteps);
                    await Task.Delay(FadeStepMs);
                    if (fade != _fade) return;
                }
        }
        StopInternal();
        _playerList = null;
        if (list is null || !_on) return;
        Start(list, fadeIn: true);
        if (!_open) return;
        for (var step = 1; step <= FadeSteps; step++)
        {
            await Task.Delay(FadeStepMs);
            if (fade != _fade) return;
            Apply((float)step / FadeSteps);
        }
    }

    private void Start(int[] list, bool fadeIn)
    {
        _playerList = list;
        var (index, position) = _resume.TryGetValue(Key(list), out var spot) && list.Contains(spot.Index)
            ? spot
            : (Shuffling ? list[_rng.Next(list.Length)] : list[0], 0);
        PlayIndex(index, position, fadeIn ? 0 : 1);
    }

    private void PlayIndex(int index, int position, float volume)
    {
        StopInternal();
        _index = index;
        var track = _library[index];
        LastError = null;
        if (!_output.Open(track.DocumentId, position))
        {
            LastError = $"{track.Title} could not be played (MP3, FLAC, WAV and OGG files play).";
            // An unplayable track is skipped, once round the list at most.
            if (_playerList is { Length: > 1 } list && ++_failures < list.Length) Skip();
            return;
        }
        _open = true;
        Apply(volume);
        _output.Play(true);
        _failures = 0;
    }

    private void StopInternal()
    {
        if (!_open) return;
        _output.Stop();
        _open = false;
    }
}
