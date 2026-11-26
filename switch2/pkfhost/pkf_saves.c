// Console users and saves. Only account saves of allow-listed titles are ever opened, and only writable titles (phase 2:
// Legends: Z-A) for writing, one at a time; the C# side runs the write sequence (ConsoleSaveFileAccess).
#include <dirent.h>
#include <stdio.h>
#include <string.h>
#include <sys/stat.h>
#include "pkf_host.h"

#define PKF_MOUNT "pkfsave"
#define PKF_WRITE_MOUNT "pkfw"
#define PKF_MAX_TITLES 32

static u64 g_allowed[PKF_MAX_TITLES];
static s32 g_allowed_count = -1;  // -1 until set; it can be set only once

s64 pkf_set_allowed_titles(const u64* ids, s32 count) {
    if (g_allowed_count >= 0 || count < 0 || count > PKF_MAX_TITLES) return PKF_NOT_ALLOWED;
    memcpy(g_allowed, ids, sizeof(u64) * (size_t)count);
    g_allowed_count = count;
    return 0;
}

static u64 g_writable[PKF_MAX_TITLES];
static s32 g_writable_count = -1;  // -1 until set; it can be set only once
static bool g_write_open;

s64 pkf_set_writable_titles(const u64* ids, s32 count) {
    if (g_writable_count >= 0 || count < 0 || count > PKF_MAX_TITLES) return PKF_NOT_ALLOWED;
    memcpy(g_writable, ids, sizeof(u64) * (size_t)count);
    g_writable_count = count;
    return 0;
}

static bool is_writable(u64 title_id) {
    for (s32 i = 0; i < g_writable_count; i++)
        if (g_writable[i] == title_id) return true;
    return false;
}

static bool is_allowed(u64 title_id) {
    for (s32 i = 0; i < g_allowed_count; i++)
        if (g_allowed[i] == title_id) return true;
    return false;
}

s64 pkf_list_users(PkfUid* out, s32 max) {
    AccountUid uids[ACC_USER_LIST_SIZE];
    s32 total = 0;
    Result rc = accountInitialize(AccountServiceType_Application);
    if (R_FAILED(rc)) return -(s64)rc;
    rc = accountListAllUsers(uids, ACC_USER_LIST_SIZE, &total);
    accountExit();
    if (R_FAILED(rc)) return -(s64)rc;
    s32 n = total < max ? total : max;
    for (s32 i = 0; i < n; i++) {
        out[i].hi = uids[i].uid[0];
        out[i].lo = uids[i].uid[1];
    }
    return n;
}

s64 pkf_list_account_saves(PkfSaveInfo* out, s32 max) {
    FsSaveDataInfoReader reader;
    FsSaveDataInfo info;
    s64 got = 0, n = 0;
    Result rc = fsOpenSaveDataInfoReader(&reader, FsSaveDataSpaceId_User);
    if (R_FAILED(rc)) return -(s64)rc;
    while (n < max) {
        rc = fsSaveDataInfoReaderRead(&reader, &info, 1, &got);
        if (R_FAILED(rc) || got == 0) break;
        if (info.save_data_type != FsSaveDataType_Account) continue;
        out[n].title_id = info.application_id;
        out[n].uid.hi = info.uid.uid[0];
        out[n].uid.lo = info.uid.uid[1];
        out[n].size = (s64)info.size;
        n++;
    }
    fsSaveDataInfoReaderClose(&reader);
    return R_FAILED(rc) ? -(s64)rc : n;
}

static s64 mount_read_only(u64 title_id, PkfUid uid) {
    if (!is_allowed(title_id)) return PKF_NOT_ALLOWED;
    AccountUid account = { .uid = { uid.hi, uid.lo } };
    FsFileSystem fs;
    Result rc = fsOpen_SaveDataReadOnly(&fs, title_id, account);
    if (R_FAILED(rc)) return -(s64)rc;
    if (fsdevMountDevice(PKF_MOUNT, fs) < 0) {
        fsFsClose(&fs);
        return PKF_IO_FAILED;
    }
    return 0;
}

// Appends "rel\tsize\n" for every file under dir; recurses into folders.
static s64 walk(const char* dir, const char* rel, char* buf, s32 len, s32* pos) {
    DIR* d = opendir(dir);
    if (!d) return PKF_IO_FAILED;
    s64 rc = 0;
    struct dirent* e;
    while (rc == 0 && (e = readdir(d))) {
        if (!strcmp(e->d_name, ".") || !strcmp(e->d_name, "..")) continue;
        char full[FS_MAX_PATH], child[FS_MAX_PATH];
        snprintf(full, sizeof full, "%s%s%s", dir, dir[strlen(dir) - 1] == '/' ? "" : "/", e->d_name);
        snprintf(child, sizeof child, "%s%s%s", rel, rel[0] ? "/" : "", e->d_name);
        struct stat st;
        if (stat(full, &st) != 0) { rc = PKF_IO_FAILED; break; }
        if (S_ISDIR(st.st_mode)) { rc = walk(full, child, buf, len, pos); continue; }
        int w = snprintf(buf + *pos, (size_t)(len - *pos), "%s\t%lld\n", child, (long long)st.st_size);
        if (w < 0 || w >= len - *pos) { rc = PKF_TOO_SMALL; break; }
        *pos += w;
    }
    closedir(d);
    return rc;
}

s64 pkf_save_list_files(u64 title_id, PkfUid uid, char* buf, s32 len) {
    if (len < 1) return PKF_TOO_SMALL;
    s64 rc = mount_read_only(title_id, uid);
    if (rc < 0) return rc;
    s32 pos = 0;
    buf[0] = '\0';
    rc = walk(PKF_MOUNT ":/", "", buf, len, &pos);
    fsdevUnmountDevice(PKF_MOUNT);
    return rc < 0 ? rc : pos;
}

s64 pkf_save_read_file(u64 title_id, PkfUid uid, const char* path, void* buf, s64 len) {
    s64 rc = mount_read_only(title_id, uid);
    if (rc < 0) return rc;
    char full[FS_MAX_PATH];
    snprintf(full, sizeof full, PKF_MOUNT ":/%s", path);
    FILE* f = fopen(full, "rb");
    s64 n = PKF_IO_FAILED;
    if (f) {
        n = (s64)fread(buf, 1, (size_t)len, f);
        if (ferror(f)) n = PKF_IO_FAILED;
        fclose(f);
    }
    fsdevUnmountDevice(PKF_MOUNT);
    return n;
}

// The save's id, from the same reader pkf_list_account_saves uses: the extra data (journal size) is read by id.
static s64 save_data_id(u64 title_id, AccountUid account, u64* out) {
    FsSaveDataInfoReader reader;
    FsSaveDataInfo info;
    s64 got = 0;
    Result rc = fsOpenSaveDataInfoReader(&reader, FsSaveDataSpaceId_User);
    if (R_FAILED(rc)) return -(s64)rc;
    s64 found = PKF_IO_FAILED;
    while (R_SUCCEEDED(rc = fsSaveDataInfoReaderRead(&reader, &info, 1, &got)) && got > 0)
        if (info.save_data_type == FsSaveDataType_Account && info.application_id == title_id
            && info.uid.uid[0] == account.uid[0] && info.uid.uid[1] == account.uid[1]) {
            *out = info.save_data_id;
            found = 0;
            break;
        }
    fsSaveDataInfoReaderClose(&reader);
    return found;
}

s64 pkf_save_open_write(u64 title_id, PkfUid uid, s64* journal_bytes) {
    if (!is_allowed(title_id) || !is_writable(title_id)) return PKF_NOT_ALLOWED;
    if (g_write_open) return PKF_IO_FAILED;
    AccountUid account = { .uid = { uid.hi, uid.lo } };
    u64 id = 0;
    s64 found = save_data_id(title_id, account, &id);
    if (found < 0) return found;
    FsSaveDataExtraData extra;
    Result rc = fsReadSaveDataFileSystemExtraData(&extra, sizeof extra, id);
    if (R_FAILED(rc)) return -(s64)rc;
    *journal_bytes = extra.journal_size;
    FsFileSystem fs;
    rc = fsOpen_SaveData(&fs, title_id, account);
    if (R_FAILED(rc)) return -(s64)rc;
    if (fsdevMountDevice(PKF_WRITE_MOUNT, fs) < 0) {
        fsFsClose(&fs);
        return PKF_IO_FAILED;
    }
    g_write_open = true;
    return 0;
}

s64 pkf_save_write_read(const char* path, void* buf, s64 len) {
    if (!g_write_open) return PKF_IO_FAILED;
    char full[FS_MAX_PATH];
    snprintf(full, sizeof full, PKF_WRITE_MOUNT ":/%s", path);
    FILE* f = fopen(full, "rb");
    if (!f) return PKF_IO_FAILED;
    s64 n = (s64)fread(buf, 1, (size_t)len, f);
    if (ferror(f)) n = PKF_IO_FAILED;
    fclose(f);
    return n;
}

// In place: "r+b" keeps the file's size, which the C# side has already checked is the same.
s64 pkf_save_write_file(const char* path, const void* buf, s64 len) {
    if (!g_write_open) return PKF_IO_FAILED;
    char full[FS_MAX_PATH];
    snprintf(full, sizeof full, PKF_WRITE_MOUNT ":/%s", path);
    FILE* f = fopen(full, "r+b");
    if (!f) return PKF_IO_FAILED;
    s64 n = (s64)fwrite(buf, 1, (size_t)len, f);
    bool ok = n == len && fflush(f) == 0;
    ok = fclose(f) == 0 && ok;
    return ok ? n : PKF_IO_FAILED;
}

s64 pkf_save_commit(void) {
    if (!g_write_open) return PKF_IO_FAILED;
    Result rc = fsdevCommitDevice(PKF_WRITE_MOUNT);
    return R_FAILED(rc) ? -(s64)rc : 0;
}

// Without a commit the journaled save keeps what it had: closing is how an unfinished write is given up.
void pkf_save_close_write(void) {
    if (!g_write_open) return;
    fsdevUnmountDevice(PKF_WRITE_MOUNT);
    g_write_open = false;
}
