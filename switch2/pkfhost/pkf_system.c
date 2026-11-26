// Versions, memory, time since main, and the crash log.
#include <fcntl.h>
#include <stdalign.h>
#include <stdio.h>
#include <stdlib.h>
#include <unistd.h>
#include <kaihen.h>
#include <switch2/memory/heap.h>
#include "pkf_host.h"

extern int main(int argc, char* argv[]);

static u64 g_main_tick;
__attribute__((constructor)) static void pkf_start_clock(void) { g_main_tick = armGetSystemTick(); }

u64 pkf_ms_since_main(void) {
    return armTicksToNs(armGetSystemTick() - g_main_tick) / 1000000;
}

u64 pkf_core_mask(void) {
    u64 mask = 0;
    if (R_FAILED(svcGetInfo(&mask, InfoType_CoreMask, CUR_PROCESS_HANDLE, 0))) return 0;
    return mask;
}

s64 pkf_pin_thread(s32 core) {
    Result rc = svcSetThreadCoreMask(CUR_THREAD_HANDLE, core, 1ULL << core);
    return R_FAILED(rc) ? -(s64)rc : 0;
}

static u64 g_ui_thread_id;
static s32 g_ui_core = -1;
static u32 g_next_worker;

s32 pkf_claim_ui_core(void) {
    u64 mask = pkf_core_mask();
    if (!mask || R_FAILED(svcGetThreadId(&g_ui_thread_id, CUR_THREAD_HANDLE))) return -1;
    s32 core = __builtin_ctzll(mask);
    if (pkf_pin_thread(core) < 0) return -1;
    g_ui_core = core;
    return core;
}

s32 pkf_place_worker_thread(void) {
    u64 id = 0;
    if (g_ui_core < 0 || R_FAILED(svcGetThreadId(&id, CUR_THREAD_HANDLE)) || id == g_ui_thread_id) return -1;
    u64 workers = pkf_core_mask() & ~(1ULL << g_ui_core);
    s32 count = __builtin_popcountll(workers);
    if (count == 0) return -1;
    // The n-th set bit of the worker mask, n going round with each new thread.
    u32 n = __atomic_fetch_add(&g_next_worker, 1, __ATOMIC_RELAXED) % (u32)count;
    for (s32 core = 0; core < 64; core++)
        if (workers & (1ULL << core) && n-- == 0)
            return pkf_pin_thread(core) < 0 ? -1 : core;
    return -1;
}

s64 pkf_versions(char* firmware, s32 len, u32* kaihen_protocol) {
    snprintf(firmware, (size_t)len, "unknown");
    *kaihen_protocol = 0;
    if (R_SUCCEEDED(setsysInitialize())) {
        SetSysFirmwareVersion fw;
        if (R_SUCCEEDED(setsysGetFirmwareVersion(&fw))) snprintf(firmware, (size_t)len, "%s", fw.display_version);
        setsysExit();
    }
    Service ro, hen;  // ro does not own its handle
    HenStatus status;
    if (R_SUCCEEDED(kaihenGetRoSession(&ro)) && R_SUCCEEDED(kaihenOpenHomebrewSession(&ro, &hen))) {
        if (R_SUCCEEDED(henGetStatus(&hen, &status))) *kaihen_protocol = status.protocol_version;
        serviceClose(&hen);
    }
    return 0;
}

static u64 g_mono_heap;
static u64 (*g_mono_used)(void);

void pkf_register_mono_heap(u64 size, u64 (*used)(void)) {
    g_mono_heap = size;
    g_mono_used = used;
}

// libounce's malloc is mimalloc over the program's heap; newlib's heap variables and mallinfo
// do not describe it (switch2/memory/heap.h). heapGetFreeSize is a lower bound.
void pkf_memory(u64* heap_total, u64* malloc_used, u64* mono_used) {
    const u64 malloc_total = heapGetTotalSize();
    *heap_total = malloc_total + g_mono_heap;
    *malloc_used = malloc_total - heapGetFreeSize();
    *mono_used = g_mono_used ? g_mono_used() : 0;
}

alignas(16) u8 __nx_exception_stack[0x10000];  // snprintf needs more than libnx's 4 KiB
u64 __nx_exception_stack_size = sizeof(__nx_exception_stack);
// libnx skips the handler in a process that counts as debugged; under kaiHEN the game process may.
u32 __nx_exception_ignoredebug = 1;

// crash-pending.txt is opened at start and deleted on a clean exit, so the handler only writes.
// After a crash an empty file means the handler never ran; a filled one holds the dump.
static int g_crash_fd = -1;
static char g_crash_path[320];

static void pkf_crash_log_clean_exit(void) {
    if (g_crash_fd < 0) return;
    close(g_crash_fd);
    g_crash_fd = -1;
    unlink(g_crash_path);
}

void pkf_install_crash_log(const char* dir) {
    snprintf(g_crash_path, sizeof g_crash_path, "%s/crash-pending.txt", dir);
    g_crash_fd = open(g_crash_path, O_WRONLY | O_CREAT | O_TRUNC, 0666);
    if (g_crash_fd >= 0) atexit(pkf_crash_log_clean_exit);
}

void __libnx_exception_handler(ThreadExceptionDump* ctx) {
    if (g_crash_fd < 0) return;
    static char text[2048];
    int n = snprintf(text, sizeof text, "error_desc=0x%x\npc=0x%lx\nlr=0x%lx\nsp=0x%lx\nfar=0x%lx\nmain=0x%lx\n",
        ctx->error_desc, ctx->pc.x, ctx->lr.x, ctx->sp.x, ctx->far.x, (u64)&main);
    u64 fp = ctx->fp.x;
    for (int i = 0; i < 32 && fp != 0 && (fp & 0xF) == 0 && n < (int)sizeof text - 40; i++) {
        const u64* frame = (const u64*)fp;
        if (frame[1] == 0) break;
        n += snprintf(text + n, sizeof text - (size_t)n, "frame%d=0x%lx\n", i, frame[1]);
        fp = frame[0];
    }
    write(g_crash_fd, text, (size_t)n);
    close(g_crash_fd);
    g_crash_fd = -1;
}
