// Interface between the native loader (dinput8.dll) and the managed core
// (DynamicKeyPrompts.dll, C# NativeAOT). Keep in sync with src/Core/LoaderApi.cs.
#pragma once
#include <stdint.h>

#define DKP_LOADER_API_VERSION 1

typedef struct DkpLoaderApi {
    uint32_t size;          // sizeof(DkpLoaderApi)
    uint32_t version;       // DKP_LOADER_API_VERSION
    void* game_base;        // module base of DarkSoulsII.exe
    const wchar_t* mod_dir; // ...\Game\DynamicKeyPrompts (no trailing slash)
    int arxan_detected;     // dearxan result
    int arxan_status;       // DearxanStatus (1 = success)

    void (*log)(const char* utf8_line);
    // MinHook wrappers; return MH_STATUS (0 = OK).
    int (*create_hook)(void* target, void* detour, void** original);
    int (*enable_hook)(void* target);
    int (*disable_hook)(void* target);
} DkpLoaderApi;

// Exported by the core. Called once on the game's main thread, at the game's entry
// point, after Arxan has been neutered and before any game code has run.
typedef int (*DkpInitFn)(const DkpLoaderApi* api);
