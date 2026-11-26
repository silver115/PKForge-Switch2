// The C API the C# side calls: console users and saves (reads, and writes of writable titles), versions, memory, timing, crash log.
#pragma once
#include <switch.h>

typedef struct { u64 hi, lo; } PkfUid;                         // AccountUid.uid[0], .uid[1]
typedef struct { u64 title_id; PkfUid uid; s64 size; } PkfSaveInfo;

// Each function returns >= 0 on success, else -(s64)Result from a service or one of these.
#define PKF_NOT_ALLOWED (-0x100000000LL)  // title not in the allow-list, or the list was already set
#define PKF_IO_FAILED   (-0x100000001LL)  // mount, open, read or directory walk failed
#define PKF_TOO_SMALL   (-0x100000002LL)  // the output buffer cannot hold the result

s64  pkf_set_allowed_titles(const u64* ids, s32 count);          // once
s64  pkf_list_users(PkfUid* out, s32 max);                        // count
s64  pkf_list_account_saves(PkfSaveInfo* out, s32 max);          // FsSaveDataType_Account, every title (metadata only)
s64  pkf_save_list_files(u64 title_id, PkfUid uid, char* buf, s32 len);  // "path\tsize\n" lines, NUL-terminated; bytes
s64  pkf_save_read_file(u64 title_id, PkfUid uid, const char* path, void* buf, s64 len);  // bytes read
s64  pkf_set_writable_titles(const u64* ids, s32 count);         // once; a subset of the allow-list (phase 2: Legends: Z-A)
s64  pkf_save_open_write(u64 title_id, PkfUid uid, s64* journal_bytes);  // one at a time; PKF_NOT_ALLOWED if not writable
s64  pkf_save_write_read(const char* path, void* buf, s64 len);  // bytes read from the save open for writing
s64  pkf_save_write_file(const char* path, const void* buf, s64 len);  // in place, same size; bytes written
s64  pkf_save_commit(void);                                       // 0; nothing is kept that was not committed
void pkf_save_close_write(void);
s64  pkf_keyboard_show(const char* text, const char* guide, s32 max_len, s32 numeric, s32 multiline, char* out, s32 out_len);  // 0 entered, 1 cancelled
s64  pkf_versions(char* firmware, s32 len, u32* kaihen_protocol);         // "unknown" and 0 when a service fails
void pkf_register_mono_heap(u64 size, u64 (*used)(void));
void pkf_memory(u64* heap_total, u64* malloc_used, u64* mono_used);  // malloc = libounce's mimalloc
u64  pkf_ms_since_main(void);
u64  pkf_core_mask(void);          // the CPU cores this process may run threads on (bit n = core n)
s64  pkf_pin_thread(s32 core);     // moves the calling thread to one core (libnx starts every thread on the same one)
s64  pkf_live_log(void);         // defined by the host (main.c): netload.py --log; 0, or PKF_NOT_ALLOWED / PKF_IO_FAILED
bool pkf_send_report(void);      // defined by the host (run_report.c): netload.py --report; false when the PC is not listening
// Horizon does not time-slice threads of equal priority on one core, so a busy background thread there stops the UI.
// The UI thread claims the process's first core; every other managed thread is placed on the remaining ones in turn.
// The SD card refuses names with non-ASCII bytes or : * ? " < > | \ ; its paths go through pkf_names.c's %XX form.
s64  pkf_sd_names_install(void);    // once, before the app touches the SD card; 0, or PKF_IO_FAILED
s32  pkf_claim_ui_core(void);       // pins the calling (UI) thread to the first core; returns that core, or -1
s32  pkf_place_worker_thread(void); // pins the calling thread to the next non-UI core; returns it, or -1 (UI thread)
void pkf_install_crash_log(const char* dir);                     // <dir>/crash-pending.txt: kept and filled on a CPU exception

// The screen and input (pkf_video.c). Touch is in the frame's pixels; raw_max_* is the largest raw touch value
// seen, to calibrate the Switch 2's touch range. quit is set when + is pressed (the phase 0 spike's way out).
// The app reads the fields after raw_max_y: held buttons, both sticks (-32767..32767, up positive), the touch in
// libnx's raw 1280x720, and applet_quit, set once the system asks the app to close (appletMainLoop returned false).
typedef struct {
    s32 touch_count, touch_x, touch_y;
    u64 buttons_down;
    s32 quit;
    s32 raw_max_x, raw_max_y;
    u64 buttons_held;
    s32 stick_lx, stick_ly, stick_rx, stick_ry;
    s32 raw_x, raw_y;
    s32 applet_quit;
} PkfInput;
s64  pkf_video_init(s32 width, s32 height);   // a linear RGBA8888 framebuffer on the default window, double-buffered
s64  pkf_present(const void* rgba, s32 stride);  // copies one frame in; waits for a free buffer (display pacing).
void pkf_present_times(u64* out);  // total ns waiting for a buffer, copying and queueing, then the frame count
                                                 // Draw elsewhere: the framebuffer is very slow to read on Switch 2.
s64  pkf_poll(PkfInput* out);
void pkf_video_exit(void);
