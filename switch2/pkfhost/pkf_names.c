// Names on the SD card. The console's file layer refuses names with non-ASCII characters or one of the FAT-invalid
// : * ? " < > | \ (an FTP MKD of "kana-ア-probe" or "accent-é-probe" fails with 550, as the app's own writes failed:
// PKForge's event cards are named in Japanese, its exports after games like "Legends: Z-A"). So every path the app
// hands to the SD card is written with each such byte, and '%' itself, as %XX, and listing a folder decodes the names
// back: the app keeps its own names, and what the SD card holds stays reversible.
#include <stdbool.h>
#include <stddef.h>
#include <string.h>
#include "pkf_names.h"

static bool needs_escape(unsigned char c) {
    return c >= 0x80 || (c != 0 && strchr("%:*?\"<>|\\", c) != NULL);
}

int pkf_name_encode(const char* in, char* out, size_t size) {
    static const char hex[] = "0123456789ABCDEF";
    size_t n = 0;
    // A device prefix ("sdmc:") is not part of a name: its colon stays.
    const char* colon = strchr(in, ':');
    const char* slash = strchr(in, '/');
    const char* p = in;
    if (colon && (!slash || colon < slash)) {
        size_t prefix = (size_t)(colon - in) + 1;
        if (prefix >= size) return -1;
        memcpy(out, in, prefix);
        n = prefix;
        p = colon + 1;
    }
    for (; *p; p++) {
        unsigned char c = (unsigned char)*p;
        if (needs_escape(c)) {
            if (n + 3 >= size) return -1;
            out[n++] = '%';
            out[n++] = hex[c >> 4];
            out[n++] = hex[c & 15];
        } else {
            if (n + 1 >= size) return -1;
            out[n++] = (char)c;
        }
    }
    out[n] = '\0';
    return 0;
}

static int hex_value(char c) {
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    return -1;  // only the uppercase digits pkf_name_encode writes
}

bool pkf_name_decode(char* name) {
    char* out = name;
    bool changed = false;
    for (const char* p = name; *p; ) {
        int hi, lo;
        if (p[0] == '%' && (hi = hex_value(p[1])) >= 0 && (lo = hex_value(p[2])) >= 0) {
            *out++ = (char)(hi << 4 | lo);
            p += 3;
            changed = true;
        } else {
            *out++ = *p++;
        }
    }
    *out = '\0';
    return changed;
}

#ifdef __SWITCH__
#include <errno.h>
#include <fcntl.h>
#include <sys/iosupport.h>
#include <sys/stat.h>
#include <sys/statvfs.h>
#include <sys/time.h>
#include "pkf_host.h"

// 3 bytes for each byte of the longest path the file system takes (FS_MAX_PATH, 0x301).
#define PKF_ENCODED_MAX (3 * 0x301 + 1)

static devoptab_t g_sd;  // the SD card's own functions, before the names were wrapped

// Encodes path into buf; true when it differs from the path (so a raw name is worth a second try).
static bool encode(struct _reent* r, const char* path, char* buf, bool* failed) {
    *failed = pkf_name_encode(path, buf, PKF_ENCODED_MAX) != 0;
    if (*failed) r->_errno = ENAMETOOLONG;
    return !*failed && strcmp(path, buf) != 0;
}

// A file put on the card by a PC can carry a raw name the console reads: a read that finds no encoded name tries it.
#define TRY_RAW(call_encoded, call_raw) do {                                  \
        char buf[PKF_ENCODED_MAX]; bool failed;                               \
        bool differs = encode(r, path, buf, &failed);                         \
        if (failed) return -1;                                                \
        int rc = (call_encoded);                                              \
        if (rc < 0 && differs && r->_errno == ENOENT) rc = (call_raw);        \
        return rc;                                                            \
    } while (0)

#define ENCODED_ONLY(call) do {                                               \
        char buf[PKF_ENCODED_MAX]; bool failed;                               \
        encode(r, path, buf, &failed);                                        \
        if (failed) return -1;                                                \
        return (call);                                                        \
    } while (0)

static int sd_open(struct _reent* r, void* file, const char* path, int flags, int mode) {
    if (flags & O_CREAT) ENCODED_ONLY(g_sd.open_r(r, file, buf, flags, mode));
    TRY_RAW(g_sd.open_r(r, file, buf, flags, mode), g_sd.open_r(r, file, path, flags, mode));
}

static int sd_stat(struct _reent* r, const char* path, struct stat* st) {
    TRY_RAW(g_sd.stat_r(r, buf, st), g_sd.stat_r(r, path, st));
}

static int sd_lstat(struct _reent* r, const char* path, struct stat* st) {
    TRY_RAW(g_sd.lstat_r(r, buf, st), g_sd.lstat_r(r, path, st));
}

static int sd_unlink(struct _reent* r, const char* path) {
    TRY_RAW(g_sd.unlink_r(r, buf), g_sd.unlink_r(r, path));
}

static int sd_rmdir(struct _reent* r, const char* path) {
    TRY_RAW(g_sd.rmdir_r(r, buf), g_sd.rmdir_r(r, path));
}

static int sd_chdir(struct _reent* r, const char* path) {
    TRY_RAW(g_sd.chdir_r(r, buf), g_sd.chdir_r(r, path));
}

static int sd_mkdir(struct _reent* r, const char* path, int mode) {
    ENCODED_ONLY(g_sd.mkdir_r(r, buf, mode));
}

static int sd_chmod(struct _reent* r, const char* path, mode_t mode) {
    TRY_RAW(g_sd.chmod_r(r, buf, mode), g_sd.chmod_r(r, path, mode));
}

static int sd_utimes(struct _reent* r, const char* path, const struct timeval times[2]) {
    TRY_RAW(g_sd.utimes_r(r, buf, times), g_sd.utimes_r(r, path, times));
}

static int sd_statvfs(struct _reent* r, const char* path, struct statvfs* st) {
    TRY_RAW(g_sd.statvfs_r(r, buf, st), g_sd.statvfs_r(r, path, st));
}

static int sd_rename(struct _reent* r, const char* from, const char* to) {
    char a[PKF_ENCODED_MAX], b[PKF_ENCODED_MAX];
    if (pkf_name_encode(from, a, sizeof a) != 0 || pkf_name_encode(to, b, sizeof b) != 0) {
        r->_errno = ENAMETOOLONG;
        return -1;
    }
    int rc = g_sd.rename_r(r, a, b);
    if (rc < 0 && r->_errno == ENOENT && strcmp(from, a) != 0) rc = g_sd.rename_r(r, from, b);
    // POSIX rename replaces an existing file, and .NET's File.Move(overwrite) relies on it; the SD card refuses
    // with EEXIST (pinning a restore point failed rewriting its .json). FAT has no atomic replace, so the old file
    // goes first: a power cut between the two leaves the new file under its temporary name, never a torn one.
    if (rc < 0 && r->_errno == EEXIST && g_sd.unlink_r(r, b) == 0) {
        rc = g_sd.rename_r(r, a, b);
        if (rc < 0 && r->_errno == ENOENT && strcmp(from, a) != 0) rc = g_sd.rename_r(r, from, b);
    }
    return rc;
}

static DIR_ITER* sd_diropen(struct _reent* r, DIR_ITER* dir, const char* path) {
    char buf[PKF_ENCODED_MAX];
    if (pkf_name_encode(path, buf, sizeof buf) != 0) {
        r->_errno = ENAMETOOLONG;
        return NULL;
    }
    DIR_ITER* it = g_sd.diropen_r(r, dir, buf);
    if (!it && r->_errno == ENOENT && strcmp(path, buf) != 0) it = g_sd.diropen_r(r, dir, path);
    return it;
}

static int sd_dirnext(struct _reent* r, DIR_ITER* dir, char* name, struct stat* st) {
    int rc = g_sd.dirnext_r(r, dir, name, st);
    if (rc == 0) pkf_name_decode(name);
    return rc;
}

s64 pkf_sd_names_install(void) {
    devoptab_t* sd = (devoptab_t*)GetDeviceOpTab("sdmc");
    if (!sd) return PKF_IO_FAILED;
    g_sd = *sd;
    // Each function the SD card has is wrapped; one it lacks stays absent.
    if (sd->open_r) sd->open_r = sd_open;
    if (sd->stat_r) sd->stat_r = sd_stat;
    if (sd->lstat_r) sd->lstat_r = sd_lstat;
    if (sd->unlink_r) sd->unlink_r = sd_unlink;
    if (sd->rmdir_r) sd->rmdir_r = sd_rmdir;
    if (sd->chdir_r) sd->chdir_r = sd_chdir;
    if (sd->mkdir_r) sd->mkdir_r = sd_mkdir;
    if (sd->chmod_r) sd->chmod_r = sd_chmod;
    if (sd->utimes_r) sd->utimes_r = sd_utimes;
    if (sd->statvfs_r) sd->statvfs_r = sd_statvfs;
    if (sd->rename_r) sd->rename_r = sd_rename;
    if (sd->diropen_r) sd->diropen_r = sd_diropen;
    if (sd->dirnext_r) sd->dirnext_r = sd_dirnext;
    return 0;
}
#endif
