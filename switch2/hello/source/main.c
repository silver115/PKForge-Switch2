// Phase 0, first console checks in C only: versions, memory, users, read-only save listing and reads,
// a large romfs and the crash log. Writes sdmc:/switch/PKForge/logs/hello.txt.
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/stat.h>
#include "pkf_host.h"

// Copy of PKForge.Infrastructure/EmulatorSaveHeuristics.cs SwitchTitles; hello is a throwaway check.
static const u64 kTitles[] = {
    0x010003F003A34000, 0x0100187003A36000, 0x0100ABF008968000, 0x01008DB008C2C000, 0x0100000011D90000,
    0x010018E011D92000, 0x01001F5010DFA000, 0x0100A3D008C5C000, 0x01008F6008C5E000, 0x0100F43008C44000,
};
#define TITLE_COUNT ((s32)(sizeof kTitles / sizeof kTitles[0]))

static FILE* g_log;

#define OUT(...) do { printf(__VA_ARGS__); if (g_log) { fprintf(g_log, __VA_ARGS__); fflush(g_log); } consoleUpdate(NULL); } while (0)

static bool is_allowed(u64 t) {
    for (s32 i = 0; i < TITLE_COUNT; i++)
        if (kTitles[i] == t) return true;
    return false;
}

static const char* romfs_big(void) {
    const char* result = "absent";
    if (R_FAILED(romfsInit())) return result;
    FILE* f = fopen("romfs:/filler.bin", "rb");
    if (f) {
        static char tail[4096];
        result = fseek(f, -(long)sizeof tail, SEEK_END) == 0 && fread(tail, 1, sizeof tail, f) == sizeof tail ? "ok" : "error";
        fclose(f);
    }
    romfsExit();
    return result;
}

static void list_save(const PkfSaveInfo* s, int user) {
    static char list[65536];
    s64 n = pkf_save_list_files(s->title_id, s->uid, list, sizeof list);
    if (n < 0) { OUT("files_error=%016lX/%d 0x%llx\n", s->title_id, user, (unsigned long long)-n); return; }
    for (char* line = strtok(list, "\n"); line; line = strtok(NULL, "\n")) {
        char* tab = strchr(line, '\t');
        if (!tab) continue;
        *tab = '\0';
        s64 size = strtoll(tab + 1, NULL, 10);
        void* buf = malloc(size > 0 ? (size_t)size : 1);
        s64 got = buf ? pkf_save_read_file(s->title_id, s->uid, line, buf, size) : -1;
        free(buf);
        OUT("file=%016lX/%d/%s size=%ld read=%ld\n", s->title_id, user, line, size, got);
    }
}

int main(int argc, char* argv[]) {
    consoleInit(NULL);
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    PadState pad;
    padInitializeDefault(&pad);

    mkdir("sdmc:/switch", 0777);
    mkdir("sdmc:/switch/PKForge", 0777);
    mkdir("sdmc:/switch/PKForge/logs", 0777);
    pkf_install_crash_log("sdmc:/switch/PKForge/logs");
    pkf_set_allowed_titles(kTitles, TITLE_COUNT);
    g_log = fopen("sdmc:/switch/PKForge/logs/hello.txt", "w");

    char firmware[32];
    u32 protocol = 0;
    pkf_versions(firmware, sizeof firmware, &protocol);
    OUT("firmware=%s\nkaihen_protocol=%u\n", firmware, protocol);

    u64 heap_total, malloc_used, mono_used;
    pkf_memory(&heap_total, &malloc_used, &mono_used);
    OUT("heap_total_mb=%lu heap_used_mb=%lu\n", heap_total >> 20, malloc_used >> 20);

    PkfUid users[ACC_USER_LIST_SIZE];
    s64 user_count = pkf_list_users(users, ACC_USER_LIST_SIZE);
    OUT("users=%ld\n", user_count);

    static PkfSaveInfo saves[512];
    s64 save_count = pkf_list_account_saves(saves, 512);
    if (save_count < 0) OUT("saves_error=0x%llx\n", (unsigned long long)-save_count);
    for (s64 i = 0; i < save_count; i++) {
        int user = -1;
        for (s64 u = 0; u < user_count; u++)
            if (users[u].hi == saves[i].uid.hi && users[u].lo == saves[i].uid.lo) user = (int)u;
        bool allowed = is_allowed(saves[i].title_id);
        OUT("save=%016lX user=%d allowed=%d size=%ld\n", saves[i].title_id, user, allowed, saves[i].size);
        if (allowed) list_save(&saves[i], user);
    }

    OUT("romfs_big=%s\n", romfs_big());
    OUT("done=1\n");
    if (g_log) fclose(g_log);
    g_log = NULL;

    struct stat st;
    if (stat("sdmc:/switch/PKForge/crash-test", &st) == 0) {
        volatile int* nowhere = NULL;
        *nowhere = 1;  // deliberate crash: checks the crash log
    }

    printf("\nDone. Press + to exit.\n");
    while (appletMainLoop()) {
        padUpdate(&pad);
        if (padGetButtonsDown(&pad) & HidNpadButton_Plus) break;
        consoleUpdate(NULL);
    }
    consoleExit(NULL);
    return 0;
}
