// PKForge for Switch 2, phase 0 host: runs the spike (switch2/spike) on Mono, with the IL in romfs.
// Built from mono-nx's native/interpreter/source/main.c and native/aot/source/main.c (MIT, see mono-nx/LICENSE).
//   make               Mono interpreter                        PKF_AOT_MODE=interpreter
//   make JIT=1         Mono's JIT, on kaiHEN's JIT memory       PKF_AOT_MODE=jit
//   make AOT=1         full AOT (build.sh aot makes the objects) PKF_AOT_MODE=full
//   make AOT=1 JIT=1   AOT code, with the JIT for what it lacks PKF_AOT_MODE=aot+jit
#include <string.h>
#include <arpa/inet.h>
#include <sys/stat.h>
#include <unistd.h>
#include "core.h"
#include "dl_shim_base.h"
#include "pkf_host.h"
#include <mono/metadata/profiler.h>

#define PKF_LOGS "sdmc:/switch/PKForge/logs"

#if PKF_AOT
#define STATIC_MONO_SYM(x) do { extern void* x; mono_aot_register_module(x); } while (0)
#endif

#if PKF_AOT && PKF_JIT
#define PKF_MONO_MODE MONO_AOT_MODE_NORMAL
#define PKF_AOT_MODE_NAME "aot+jit"
#elif PKF_AOT
#define PKF_MONO_MODE MONO_AOT_MODE_FULL
#define PKF_AOT_MODE_NAME "full"
#elif PKF_JIT
#define PKF_MONO_MODE MONO_AOT_MODE_NORMAL
#define PKF_AOT_MODE_NAME "jit"
#else
#define PKF_MONO_MODE MONO_AOT_MODE_INTERP_ONLY
#define PKF_AOT_MODE_NAME "interpreter"
#endif

#if !DLSHIM_DISABLE
// Code that is interpreted or JIT-compiled finds the spike's [DllImport("pkfhost")] functions here; full AOT
// links them directly. mono-nx's dl_shim answers NULL for this name, so Mono asks this fallback next.
static void* pkfhost_load(const char* name, int flags, char** err, void* user_data) {
    return name && strcmp(name, "pkfhost") == 0 ? (void*)(intptr_t)0xABC00200 : NULL;
}

static void* pkfhost_symbol(void* handle, const char* name, char** err, void* user_data) {
    SYM_RESOLVE_EXISTING(pkf_set_allowed_titles);
    SYM_RESOLVE_EXISTING(pkf_list_users);
    SYM_RESOLVE_EXISTING(pkf_list_account_saves);
    SYM_RESOLVE_EXISTING(pkf_save_list_files);
    SYM_RESOLVE_EXISTING(pkf_save_read_file);
    SYM_RESOLVE_EXISTING(pkf_set_writable_titles);
    SYM_RESOLVE_EXISTING(pkf_save_open_write);
    SYM_RESOLVE_EXISTING(pkf_save_write_read);
    SYM_RESOLVE_EXISTING(pkf_save_write_file);
    SYM_RESOLVE_EXISTING(pkf_save_commit);
    SYM_RESOLVE_EXISTING(pkf_save_close_write);
    SYM_RESOLVE_EXISTING(pkf_keyboard_show);
    SYM_RESOLVE_EXISTING(pkf_music_open);
    SYM_RESOLVE_EXISTING(pkf_music_play);
    SYM_RESOLVE_EXISTING(pkf_music_stop);
    SYM_RESOLVE_EXISTING(pkf_music_gain);
    SYM_RESOLVE_EXISTING(pkf_music_position_ms);
    SYM_RESOLVE_EXISTING(pkf_music_state);
    SYM_RESOLVE_EXISTING(pkf_music_shutdown);
    SYM_RESOLVE_EXISTING(pkf_versions);
    SYM_RESOLVE_EXISTING(pkf_memory);
    SYM_RESOLVE_EXISTING(pkf_ms_since_main);
    SYM_RESOLVE_EXISTING(pkf_core_mask);
    SYM_RESOLVE_EXISTING(pkf_pin_thread);
    SYM_RESOLVE_EXISTING(pkf_video_init);
    SYM_RESOLVE_EXISTING(pkf_present);
    SYM_RESOLVE_EXISTING(pkf_poll);
    SYM_RESOLVE_EXISTING(pkf_video_exit);
    SYM_RESOLVE_EXISTING(pkf_live_log);
    return NULL;
}

static void* pkfhost_close(void* handle, void* user_data) { return NULL; }
#endif

// Finds an assembly as /<name>.dll in romfs. Full AOT loads every assembly a module references while CoreLib's
// own module loads, inside mono_jit_init: before Mono installs its default search (mono_runtime_init) and before
// managed code can run, so without this hook Mono falls back to managed resolution there and the process dies.
static MonoAssembly* pkf_preload(MonoAssemblyName* aname, char** assemblies_path, void* user_data) {
    const char* name = mono_assembly_name_get_name(aname);
    const char* culture = mono_assembly_name_get_culture(aname);
    if (!name)
        return NULL;
    // A reference cycle: AOT modules load their references before their own assembly is listed as loaded,
    // so an assembly that is still loading is found through its image.
    MonoImage* image = mono_image_loaded(name);
    if (image && mono_image_get_assembly(image))
        return mono_image_get_assembly(image);
    if (strcmp(name, "System.Private.CoreLib") == 0 || (culture && *culture))
        return NULL;  // CoreLib has its own path; satellite assemblies do not ship
    char path[300];
    snprintf(path, sizeof path, "/%s.dll", name);
    if (access(path, F_OK) != 0)
        return NULL;
    MonoImageOpenStatus status;
    return mono_assembly_open(path, &status);
}

// Each managed thread Mono starts (the thread pool, tasks, timers) leaves the UI thread's core: libnx starts every
// thread on one core, and Horizon does not time-slice equal priorities, so a busy one there would stop the UI.
static void pkf_thread_started(MonoProfiler* profiler, uintptr_t tid) {
    (void)profiler;
    (void)tid;
    static int logged;
    s32 core = pkf_place_worker_thread();
    if (core >= 0 && logged++ < 16) io_debugf("Managed thread placed on core %d", core);
}

// netload.py --log listens for the log on the PC. The loader passes the PC's address on every netload, so this
// argument says whether to use it.
static bool g_live_log_wanted;
// netload.py --report: after quitting, send the PC this run's report before going back to the loader (run_report.c).
static bool g_send_report;

// The log also goes live to the PC that netloaded the .nro. The app calls this once Mono runs: with sockets started
// on the main thread before Mono, the app died on Home ("mono_coop_cond_broadcast Cannot transition thread ...").
s64 pkf_live_log(void) {
    if (!g_live_log_wanted || !__nxlink_host.s_addr)
        return PKF_NOT_ALLOWED;
    socket_esnure_init_thread_safe();
    int sock = nxlinkConnectToHost(false, false);
    if (sock < 0)
        return PKF_IO_FAILED;
    io_stdio_tee_socket(sock);
    io_debugf("Live log to the PC: connected");
    return 0;
}

int main(int argc, char* argv[])
{
    // First: the SD card refuses some of the names PKForge writes (event cards in Japanese, "Legends: Z-A").
    pkf_sd_names_install();
    mkdir("sdmc:/switch/PKForge", 0777);
    mkdir(PKF_LOGS, 0777);
    pkf_install_crash_log(PKF_LOGS);
    setenv("DOTNET_SYSTEM_GLOBALIZATION_INVARIANT", "1", 1);
    setenv("PKF_AOT_MODE", PKF_AOT_MODE_NAME, 1);

    if (R_FAILED(romfsInit())) {
        fatal_error("The .nro has no romfs (the spike's assemblies live there)");
        return 1;
    }
    // Mono does not take "romfs:" paths, so from here the assemblies are "/<name>.dll" (aot_config.ini).
    chdir("romfs:/");
    if (!application_initialize("romfs:/aot_config.ini"))
        return 1;
    for (int i = 1; i < argc; i++)
    {
        if (strcmp(argv[i], "--live-log") == 0)
            g_live_log_wanted = true;
        if (strcmp(argv[i], "--report") == 0)
            g_send_report = true;
    }
    // Netloaded: quitting goes back to the loader for the next run, not out of the homebrew loader with the whole
    // application (aot_config.ini's exit_process_on_end, kept for normal launches). Not after a report: a run that
    // used the console's sockets after Mono showed "an error occurred" on the way back (2026-11-23), so it exits whole.
    if (__nxlink_host.s_addr && !g_send_report)
        g_config.exit_process_on_end = false;
    // Which loader started this run (logged for test runs).
    if (envGetLoaderInfoSize())
        io_debugf("Loader: %.*s", (int)envGetLoaderInfoSize(), envGetLoaderInfo());
    io_debugf("Mode: %s", PKF_AOT_MODE_NAME);

#if PKF_AOT
    io_debugf("Registering the AOT modules");
    #include "mono_symbols.h"
    io_debugf("AOT modules registered");
#endif
    mono_jit_set_aot_mode(PKF_MONO_MODE);
#if PKF_TRACE
    // Every Mono log channel at debug level, through mono-nx's handler into our log (make TRACE=1).
    g_config.mono_runtime_logging = g_config.mononx_logging = true;
#endif
    application_configure_mono();
#if !DLSHIM_DISABLE
    mono_dl_fallback_register(pkfhost_load, pkfhost_symbol, pkfhost_close, NULL);
#endif
    mono_install_assembly_preload_hook(pkf_preload, NULL);
    io_debugf("UI thread on core %d", pkf_claim_ui_core());
    mono_profiler_set_thread_started_callback(mono_profiler_create(NULL), pkf_thread_started);

    io_debugf("Starting Mono");
    MonoDomain* domain = mono_jit_init("PKForge");
    if (!domain) {
        fatal_error("Failed to initialize the Mono domain");
        return 1;
    }
    io_debugf("Loading assembly %s", g_config.default_assembly);
    MonoAssembly* assembly = mono_domain_assembly_open(domain, g_config.default_assembly);
    if (!assembly) {
        fatal_error("Failed to load the spike assembly");
        return 1;
    }

    // Main gets the .nro's path (the spike's size check), then the loader's arguments (netload.py --script).
    char* monoargs[64] = { g_config.default_assembly, argc > 0 ? argv[0] : "" };
    int monoargc = 2;
    for (int i = 1; i < argc && monoargc < 64; i++)
        monoargs[monoargc++] = argv[i];
    int code = mono_jit_exec(domain, assembly, monoargc, monoargs);
    io_debugf("Main returned %d", code);

    mono_jit_cleanup(domain);
    romfsExit();
    if (g_send_report && __nxlink_host.s_addr)
        pkf_send_report();
    application_terminate();
    return code;
}
