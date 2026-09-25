// dinput8.dll proxy: the game imports only DirectInput8Create. The real function is resolved
// on first use, never under the loader lock.
#ifdef DKP_PROXY_DINPUT8

#include "loader.h"

const char* const g_proxy_name = "dinput8.dll";

using DirectInput8CreateFn = HRESULT(WINAPI*)(HINSTANCE, DWORD, REFIID, LPVOID*, void*);
static INIT_ONCE g_once = INIT_ONCE_STATIC_INIT;
static DirectInput8CreateFn g_real_create;

static BOOL CALLBACK resolve(PINIT_ONCE, PVOID, PVOID*)
{
    if (HMODULE m = load_system_dll(L"dinput8.dll"))
        g_real_create = reinterpret_cast<DirectInput8CreateFn>(GetProcAddress(m, "DirectInput8Create"));
    logf("loader: system dinput8 resolved: %s", g_real_create ? "ok" : "FAILED");
    return TRUE;
}

extern "C" HRESULT WINAPI DirectInput8Create(HINSTANCE inst, DWORD ver, REFIID riid, LPVOID* out, void* outer)
{
    InitOnceExecuteOnce(&g_once, resolve, nullptr, nullptr);
    if (!g_real_create)
        return E_FAIL;
    return g_real_create(inst, ver, riid, out, outer);
}

#endif
