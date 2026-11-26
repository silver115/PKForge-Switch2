#include "dl_shim_base.h"

// PKForge: resolves through the runtime's own tables (entrypoints.c in each static library), so the shim
// matches whatever this runtime version compiled. mono-nx listed .NET 9's functions by hand.
const void* SystemResolveDllImport(const char* name);
const void* GlobalizationResolveDllImport(const char* name);
const void* CompressionResolveDllImport(const char* name);

// PKForge: .NET's own sockets and name lookups are refused, as when the console is offline. Once the console's
// sockets were started (for the host's live log), PKForge's startup update check got through the name lookup to
// .NET's socket I/O, and the app died on Home: "mono_coop_cond_broadcast Cannot transition thread ... DO_BLOCKING".
// The app's network goes through a native HttpMessageHandler later (design 7.7), so nothing managed needs these.
// The Makefile links with --wrap for both, so full AOT's direct calls and the runtime's own tables come here.
int32_t __wrap_SystemNative_Socket(int32_t addressFamily, int32_t socketType, int32_t protocolType, intptr_t* createdSocket)
{
    *createdSocket = -1;
    return 0x10026; // Error_ENETDOWN (pal_error_common.h)
}

int32_t __wrap_SystemNative_GetHostEntryForName(const uint8_t* address, int32_t addressFamily, void* entry)
{
    return 3; // GetAddrInfoErrorFlags_EAI_FAIL (pal_networking.h)
}

void *getsym_SystemNative(const char *name)
{
    return (void *)SystemResolveDllImport(name);
}

void *getsym_GlobalizationNative(const char *name)
{
    return (void *)GlobalizationResolveDllImport(name);
}

void *getsym_CompressionNative(const char *name)
{
    return (void *)CompressionResolveDllImport(name);
}
