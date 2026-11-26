// Background music (src/PKForge.Switch2.Platform/ConsoleMusicPlayer.cs): one track at a time, decoded while it plays
// on a thread of its own and sent to audout as 16-bit stereo at audout's rate. MP3, FLAC, WAV and Ogg Vorbis, with the
// public-domain decoders in third_party (dr_libs e381981, stb 6b5799b): a few hundred KB of code. A track opens by
// reading only its header, and is never decoded whole.
#define DR_MP3_IMPLEMENTATION
#include "third_party/dr_mp3.h"
#define DR_FLAC_IMPLEMENTATION
#include "third_party/dr_flac.h"
#define DR_WAV_IMPLEMENTATION
#include "third_party/dr_wav.h"
#define STB_VORBIS_NO_PUSHDATA_API
#include "third_party/stb_vorbis.c"
#include <ctype.h>
#include <malloc.h>
#include <string.h>
#include "pkf_host.h"

#define OUT_FRAMES 2048   // frames in one audout buffer: 43 ms at 48 kHz
#define OUT_BUFFERS 3     // queued ahead: enough that a busy frame never starves it
#define SRC_FRAMES 1024   // source frames decoded at a time
#define MAX_CHANNELS 8

enum { KIND_NONE, KIND_MP3, KIND_FLAC, KIND_WAV, KIND_VORBIS };

typedef struct {
    int kind;
    drmp3 mp3;
    drflac* flac;
    drwav wav;
    stb_vorbis* vorbis;
    u32 channels, rate;
    u64 frames;                         // source frames decoded so far: the position
    s16 src[(SRC_FRAMES + 1) * 2];      // decoded stereo frames; the next output frame lies at pos within them
    u32 src_count;
    double pos;
    bool eof;
} Track;

static Mutex g_lock;
static CondVar g_wake;
static Thread g_thread;
static bool g_started, g_quit;
static u32 g_out_rate;
static Track g_track;
static s32 g_state;                     // PKF_MUSIC_*
static AudioOutBuffer g_buffers[OUT_BUFFERS];
static s16 g_decoded[SRC_FRAMES * MAX_CHANNELS];   // the audio thread's and an open's scratch, under g_lock

static void close_track(Track* t) {
    switch (t->kind) {
    case KIND_MP3: drmp3_uninit(&t->mp3); break;
    case KIND_FLAC: drflac_close(t->flac); break;
    case KIND_WAV: drwav_uninit(&t->wav); break;
    case KIND_VORBIS: stb_vorbis_close(t->vorbis); break;
    }
    t->kind = KIND_NONE;
}

// Up to n source frames as stereo; 0 at the end of the track.
static u32 decode(Track* t, s16* out, u32 n) {
    u64 got = 0;
    switch (t->kind) {
    case KIND_MP3: got = drmp3_read_pcm_frames_s16(&t->mp3, n, g_decoded); break;
    case KIND_FLAC: got = drflac_read_pcm_frames_s16(t->flac, n, g_decoded); break;
    case KIND_WAV: got = drwav_read_pcm_frames_s16(&t->wav, n, g_decoded); break;
    case KIND_VORBIS: got = (u64)stb_vorbis_get_samples_short_interleaved(t->vorbis, (int)t->channels, g_decoded, (int)(n * t->channels)); break;
    }
    u32 ch = t->channels;
    for (u32 i = 0; i < (u32)got; i++) {
        out[i * 2] = g_decoded[i * ch];
        out[i * 2 + 1] = g_decoded[i * ch + (ch > 1 ? 1 : 0)];
    }
    t->frames += got;
    return (u32)got;
}

// Fills out with up to n frames at the output rate (linear interpolation between source frames); fewer at the end.
static u32 fill(Track* t, s16* out, u32 n) {
    double step = (double)t->rate / g_out_rate;
    u32 written = 0;
    while (written < n) {
        u32 i = (u32)t->pos;
        if (i + 1 >= t->src_count) {
            if (t->eof) break;
            // Keep the last frame: the next output frame may lie between it and the first new one.
            u32 kept = 0;
            if (t->src_count > 0) {
                t->src[0] = t->src[(t->src_count - 1) * 2];
                t->src[1] = t->src[(t->src_count - 1) * 2 + 1];
                t->pos -= t->src_count - 1;
                kept = 1;
            }
            u32 got = decode(t, t->src + kept * 2, SRC_FRAMES);
            t->src_count = kept + got;
            if (got == 0) t->eof = true;
            continue;
        }
        double f = t->pos - i;
        const s16* a = t->src + i * 2;
        out[written * 2] = (s16)(a[0] + (a[2] - a[0]) * f);
        out[written * 2 + 1] = (s16)(a[1] + (a[3] - a[1]) * f);
        written++;
        t->pos += step;
    }
    return written;
}

static void music_thread(void* arg) {
    (void)arg;
    u32 next = 0, queued = 0;
    mutexLock(&g_lock);
    while (!g_quit) {
        if (g_state == PKF_MUSIC_PLAYING && queued < OUT_BUFFERS) {
            AudioOutBuffer* buffer = &g_buffers[next];
            s16* data = buffer->buffer;
            u32 n = fill(&g_track, data, OUT_FRAMES);
            if (n < OUT_FRAMES) {
                memset(data + n * 2, 0, (OUT_FRAMES - n) * 4);
                g_state = PKF_MUSIC_ENDED;
                if (n == 0) continue;
            }
            if (R_SUCCEEDED(audoutAppendAudioOutBuffer(buffer))) {
                next = (next + 1) % OUT_BUFFERS;
                queued++;
            }
            continue;
        }
        if (queued > 0) {
            mutexUnlock(&g_lock);
            AudioOutBuffer* released = NULL;
            u32 count = 0;
            Result rc = audoutWaitPlayFinish(&released, &count, 100000000ULL);
            mutexLock(&g_lock);
            if (R_SUCCEEDED(rc)) queued -= count < queued ? count : queued;
            continue;
        }
        condvarWait(&g_wake, &g_lock);
    }
    mutexUnlock(&g_lock);
}

static s64 start(void) {
    if (g_started) return 0;
    Result rc = audoutInitialize();
    if (R_FAILED(rc)) return -(s64)rc;
    rc = audoutStartAudioOut();
    if (R_FAILED(rc)) {
        audoutExit();
        return -(s64)rc;
    }
    g_out_rate = audoutGetSampleRate();
    if (g_out_rate == 0) g_out_rate = 48000;
    for (int i = 0; i < OUT_BUFFERS; i++) {
        u32 size = OUT_FRAMES * 4;   // 8 KiB: audout wants 4 KiB-aligned buffers
        g_buffers[i] = (AudioOutBuffer){ .buffer = memalign(0x1000, size), .buffer_size = size, .data_size = size };
        if (!g_buffers[i].buffer) return PKF_IO_FAILED;
    }
    mutexInit(&g_lock);
    condvarInit(&g_wake);
    // The last core, above the drawing threads' priority there: decoding takes a few percent of a core, and a buffer
    // that comes late is a click.
    u64 mask = pkf_core_mask();
    s32 core = mask ? 63 - __builtin_clzll(mask) : -2;
    rc = threadCreate(&g_thread, music_thread, NULL, NULL, 0x10000, 0x2B, core);
    if (R_FAILED(rc)) return -(s64)rc;
    rc = threadStart(&g_thread);
    if (R_FAILED(rc)) return -(s64)rc;
    g_started = true;
    return 0;
}

static bool has_extension(const char* path, const char* ext) {
    size_t n = strlen(path), e = strlen(ext);
    if (n < e) return false;
    for (size_t i = 0; i < e; i++)
        if (tolower((unsigned char)path[n - e + i]) != ext[i]) return false;
    return true;
}

s64 pkf_music_open(const char* path, s64 start_ms) {
    s64 rc = start();
    if (rc < 0) return rc;
    mutexLock(&g_lock);
    Track* t = &g_track;
    close_track(t);
    g_state = PKF_MUSIC_NONE;
    bool opened = false;
    if (has_extension(path, ".mp3") && drmp3_init_file(&t->mp3, path, NULL)) {
        t->kind = KIND_MP3;
        t->channels = t->mp3.channels;
        t->rate = t->mp3.sampleRate;
        opened = true;
    } else if (has_extension(path, ".flac") && (t->flac = drflac_open_file(path, NULL))) {
        t->kind = KIND_FLAC;
        t->channels = t->flac->channels;
        t->rate = t->flac->sampleRate;
        opened = true;
    } else if (has_extension(path, ".wav") && drwav_init_file(&t->wav, path, NULL)) {
        t->kind = KIND_WAV;
        t->channels = t->wav.channels;
        t->rate = t->wav.sampleRate;
        opened = true;
    } else if (has_extension(path, ".ogg") || has_extension(path, ".oga")) {
        int error = 0;
        if ((t->vorbis = stb_vorbis_open_filename(path, &error, NULL))) {
            stb_vorbis_info info = stb_vorbis_get_info(t->vorbis);
            t->kind = KIND_VORBIS;
            t->channels = (u32)info.channels;
            t->rate = info.sample_rate;
            opened = true;
        }
    }
    if (opened && (t->channels == 0 || t->channels > MAX_CHANNELS || t->rate == 0)) {
        close_track(t);
        opened = false;
    }
    if (!opened) {
        mutexUnlock(&g_lock);
        return PKF_IO_FAILED;
    }
    t->frames = 0;
    if (start_ms > 0) {
        u64 frame = (u64)start_ms * t->rate / 1000;
        bool sought = false;
        switch (t->kind) {
        case KIND_MP3: sought = drmp3_seek_to_pcm_frame(&t->mp3, frame); break;
        case KIND_FLAC: sought = drflac_seek_to_pcm_frame(t->flac, frame); break;
        case KIND_WAV: sought = drwav_seek_to_pcm_frame(&t->wav, frame); break;
        case KIND_VORBIS: sought = stb_vorbis_seek(t->vorbis, (unsigned int)frame); break;
        }
        if (sought) t->frames = frame;
    }
    t->src_count = 0;
    t->pos = 0;
    t->eof = false;
    g_state = PKF_MUSIC_PAUSED;
    mutexUnlock(&g_lock);
    return 0;
}

void pkf_music_play(s32 playing) {
    if (!g_started) return;
    mutexLock(&g_lock);
    if (playing && g_state == PKF_MUSIC_PAUSED) {
        g_state = PKF_MUSIC_PLAYING;
        condvarWakeOne(&g_wake);
    } else if (!playing && g_state == PKF_MUSIC_PLAYING) {
        g_state = PKF_MUSIC_PAUSED;
    }
    mutexUnlock(&g_lock);
}

void pkf_music_stop(void) {
    if (!g_started) return;
    mutexLock(&g_lock);
    close_track(&g_track);
    g_state = PKF_MUSIC_NONE;
    mutexUnlock(&g_lock);
}

void pkf_music_gain(s32 per_mille) {
    if (!g_started) return;
    audoutSetAudioOutVolume((per_mille < 0 ? 0 : per_mille > 1000 ? 1000 : per_mille) / 1000.0f);
}

s64 pkf_music_position_ms(void) {
    if (!g_started) return 0;
    mutexLock(&g_lock);
    s64 ms = g_track.kind != KIND_NONE && g_track.rate ? (s64)(g_track.frames * 1000 / g_track.rate) : 0;
    mutexUnlock(&g_lock);
    return ms;
}

s32 pkf_music_state(void) {
    return g_started ? __atomic_load_n(&g_state, __ATOMIC_RELAXED) : PKF_MUSIC_NONE;
}

void pkf_music_shutdown(void) {
    if (!g_started) return;
    mutexLock(&g_lock);
    g_quit = true;
    condvarWakeAll(&g_wake);
    mutexUnlock(&g_lock);
    threadWaitForExit(&g_thread);
    threadClose(&g_thread);
    close_track(&g_track);
    audoutStopAudioOut();
    audoutExit();
    for (int i = 0; i < OUT_BUFFERS; i++) free(g_buffers[i].buffer);
    g_started = false;
}
