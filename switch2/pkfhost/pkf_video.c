// The screen and input: Skia draws on the CPU into the app's own buffer, and each frame is copied into a linear
// framebuffer on the default window (libounce: "[Switch 2] only linear mode is available"). Input comes from HID.
#include <string.h>
#include "pkf_host.h"

static Framebuffer g_fb;
static PadState g_pad;
static s32 g_width, g_height;
static s32 g_raw_max_x, g_raw_max_y;
static bool g_open;

s64 pkf_video_init(s32 width, s32 height) {
    if (g_open) return 0;
    Result rc = framebufferCreate(&g_fb, nwindowGetDefault(), (u32)width, (u32)height, PIXEL_FORMAT_RGBA_8888, 2);
    if (R_FAILED(rc)) return -(s64)rc;
    rc = framebufferMakeLinear(&g_fb);
    if (R_FAILED(rc)) {
        framebufferClose(&g_fb);
        return -(s64)rc;
    }
    padConfigureInput(1, HidNpadStyleSet_NpadStandard);
    padInitializeDefault(&g_pad);
    hidInitializeTouchScreen();
    g_width = width;
    g_height = height;
    g_open = true;
    return 0;
}

// Time spent in each part of presenting, for the results file: waiting for a free buffer, copying, queueing.
static u64 g_present_ns[3], g_presents;

s64 pkf_present(const void* rgba, s32 stride) {
    if (!g_open) return PKF_IO_FAILED;
    u32 fb_stride = 0;
    u64 begin = armGetSystemTick();
    u8* dst = (u8*)framebufferBegin(&g_fb, &fb_stride);
    if (!dst) return PKF_IO_FAILED;
    u64 copy = armGetSystemTick();
    const u8* src = (const u8*)rgba;
    for (s32 y = 0; y < g_height; y++)
        memcpy(dst + (size_t)y * fb_stride, src + (size_t)y * stride, (size_t)g_width * 4);
    u64 end = armGetSystemTick();
    framebufferEnd(&g_fb);
    g_present_ns[0] += armTicksToNs(copy - begin);
    g_present_ns[1] += armTicksToNs(end - copy);
    g_present_ns[2] += armTicksToNs(armGetSystemTick() - end);
    g_presents++;
    return 0;
}

void pkf_present_times(u64* out) {
    for (int i = 0; i < 3; i++) out[i] = g_present_ns[i];
    out[3] = g_presents;
}

s64 pkf_poll(PkfInput* out) {
    memset(out, 0, sizeof *out);
    // Called every frame, as libnx needs: it handles HOME and suspension, and says when the app must close.
    out->applet_quit = !appletMainLoop();
    padUpdate(&g_pad);
    out->buttons_down = padGetButtonsDown(&g_pad);
    out->buttons_held = padGetButtons(&g_pad);
    out->quit = (out->buttons_down & HidNpadButton_Plus) != 0;
    HidAnalogStickState left = padGetStickPos(&g_pad, 0), right = padGetStickPos(&g_pad, 1);
    out->stick_lx = left.x;
    out->stick_ly = left.y;
    out->stick_rx = right.x;
    out->stick_ry = right.y;
    HidTouchScreenState touch = {0};
    if (hidGetTouchScreenStates(&touch, 1) && touch.count > 0) {
        u32 x = touch.touches[0].x, y = touch.touches[0].y;
        if ((s32)x > g_raw_max_x) g_raw_max_x = (s32)x;
        if ((s32)y > g_raw_max_y) g_raw_max_y = (s32)y;
        // libnx reports touch in 1280x720; shortcut: if the Switch 2 reports its native 1920x1080 instead,
        // raw_max_* shows it and this scale changes.
        out->touch_count = touch.count;
        out->raw_x = (s32)x;
        out->raw_y = (s32)y;
        out->touch_x = (s32)((u64)x * (u64)g_width / 1280);
        out->touch_y = (s32)((u64)y * (u64)g_height / 720);
    }
    out->raw_max_x = g_raw_max_x;
    out->raw_max_y = g_raw_max_y;
    return 0;
}

void pkf_video_exit(void) {
    if (!g_open) return;
    framebufferClose(&g_fb);
    g_open = false;
}
