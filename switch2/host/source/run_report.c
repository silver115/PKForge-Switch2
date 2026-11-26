// Test runs (switch2/toolchain/netload.py --report): when a netloaded run quits, the host sends the PC this run's
// part of mono.txt and its newest results file, then closes the whole application (main.c): going back to the loader
// after using the console's sockets showed "an error occurred". The PC listens on nxlink's client port.
#include <arpa/inet.h>
#include <dirent.h>
#include <errno.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include <sys/socket.h>
#include <unistd.h>
#include <switch.h>
#include "core.h"
#include "pkf_host.h"

#define LOGS "sdmc:/switch/PKForge/logs"

// A send waits for the network itself if the socket says it would block: up to 30 s with nothing moving.
static bool wait_more(ssize_t n, size_t* idle) {
    if (n < 0 && (errno == EAGAIN || errno == EWOULDBLOCK) && ++*idle < 30000) {
        svcSleepThread(1000000ULL);
        return true;
    }
    return false;
}

static bool send_all(int sock, const void* buf, size_t len) {
    for (size_t sent = 0, idle = 0; sent < len;) {
        ssize_t n = send(sock, (const char*)buf + sent, len - sent, 0);
        if (n > 0) {
            sent += (size_t)n;
            idle = 0;
        } else if (!wait_more(n, &idle))
            return false;
    }
    return true;
}

static void send_s32(int sock, s32 value) { send_all(sock, &value, sizeof value); }

// Appends a file, or its part from the last occurrence of `from` on, to the report.
static void append_file(char** report, size_t* used, const char* path, const char* from) {
    FILE* file = fopen(path, "rb");
    if (!file) return;
    fseek(file, 0, SEEK_END);
    long size = ftell(file);
    fseek(file, 0, SEEK_SET);
    char* text = malloc((size_t)size + 1);
    if (text && fread(text, 1, (size_t)size, file) == (size_t)size) {
        text[size] = 0;
        const char* start = text;
        for (const char* at = text; from && (at = strstr(at, from)); at++) start = at;
        size_t len = strlen(start);
        char* grown = realloc(*report, *used + len + 64);
        if (grown) {
            *report = grown;
            *used += (size_t)sprintf(*report + *used, "=== %s\n", path);
            memcpy(*report + *used, start, len);
            *used += len;
        }
    }
    free(text);
    fclose(file);
}

static void send_report(int sock) {
    char* report = NULL;
    size_t used = 0;
    append_file(&report, &used, LOGS "/mono.txt", "--- starting log ---");
    // The newest results file: their names are timestamps, so the largest name.
    char newest[64] = "";
    DIR* dir = opendir(LOGS);
    for (struct dirent* entry; dir && (entry = readdir(dir));)
        if (!strncmp(entry->d_name, "results-", 8) && strlen(entry->d_name) < sizeof newest && strcmp(entry->d_name, newest) > 0)
            strcpy(newest, entry->d_name);
    if (dir) closedir(dir);
    if (newest[0]) {
        char path[128];
        snprintf(path, sizeof path, LOGS "/%s", newest);
        append_file(&report, &used, path, NULL);
    }
    send_s32(sock, (s32)used);
    if (used) send_all(sock, report, used);
    free(report);
}

bool pkf_send_report(void) {
    socket_esnure_init_thread_safe();
    int sock = nxlinkConnectToHost(false, false);
    io_debugf("Report to the PC: %s", sock >= 0 ? "sending" : "not listening");
    if (sock < 0) return false;
    // The report reads mono.txt, which the console does not open while it is still open for writing.
    io_stdio_finish();
    send_report(sock);
    close(sock);
    return true;
}
