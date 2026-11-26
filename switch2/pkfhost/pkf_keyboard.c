// The console's software keyboard (swkbd) for a text field. It blocks the calling (UI) thread while it shows.
#include "pkf_host.h"

s64 pkf_keyboard_show(const char* text, const char* guide, s32 max_len, s32 numeric, s32 multiline, char* out, s32 out_len) {
    if (!out || out_len < 1) return PKF_TOO_SMALL;
    SwkbdConfig kbd;
    Result rc = swkbdCreate(&kbd, 0);
    if (R_FAILED(rc)) return -(s64)rc;
    swkbdConfigMakePresetDefault(&kbd);
    if (numeric) swkbdConfigSetType(&kbd, SwkbdType_NumPad);
    if (multiline) swkbdConfigSetReturnButtonFlag(&kbd, 1);  // the Return key types a newline
    if (text) swkbdConfigSetInitialText(&kbd, text);
    if (guide) swkbdConfigSetGuideText(&kbd, guide);
    if (max_len > 0) swkbdConfigSetStringLenMax(&kbd, (u32)max_len);
    out[0] = '\0';
    rc = swkbdShow(&kbd, out, (size_t)out_len);
    swkbdClose(&kbd);
    if (R_SUCCEEDED(rc)) return 0;
    // Cancelling ends the applet this way; anything else is a real failure for the app's log.
    return R_VALUE(rc) == MAKERESULT(Module_Libnx, LibnxError_LibAppletBadExit) ? 1 : -(s64)rc;
}
