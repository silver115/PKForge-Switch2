// System.Native functions that .NET 10's entrypoints.c table names but the libnx build leaves out
// (pal_networkchange.c is not built; the signal stubs lack GetPlatformSignalNumber). The dl_shim resolves
// through that table, so these answer "not supported" instead of failing the link.
// shortcut: these belong in the fork's libnx stubs; move them there when the fork is next rebuilt.
#include <stdint.h>

#define PAL_ERROR_ENOTSUP 0x1003D  // Error_ENOTSUP, src/native/libs/Common/pal_error_common.h

typedef void (*NetworkChangeEvent)(intptr_t sock, int32_t notificationKind);

int32_t SystemNative_CreateNetworkChangeListenerSocket(intptr_t* retSocket) {
    *retSocket = -1;
    return PAL_ERROR_ENOTSUP;
}

int32_t SystemNative_ReadEvents(intptr_t sock, NetworkChangeEvent onNetworkChange) {
    return PAL_ERROR_ENOTSUP;
}

// 0 tells PosixSignalRegistration that the signal does not exist here, so it throws PlatformNotSupportedException.
int32_t SystemNative_GetPlatformSignalNumber(int32_t signal) {
    return 0;
}
