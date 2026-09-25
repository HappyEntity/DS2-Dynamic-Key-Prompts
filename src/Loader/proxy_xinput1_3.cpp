// xinput1_3.dll proxy: the game imports XInputGetState (ordinal 2) and XInputSetState (3).
// Every export of the real DLL is forwarded under the same ordinal, including the unnamed
// 100-103 used by Steam Input and overlays. The real XInput 1.3 (DirectX redistributable)
// is preferred; Windows' built-in xinput1_4.dll is the fallback.
#ifdef DKP_PROXY_XINPUT1_3

#include "loader.h"

const char* const g_proxy_name = "xinput1_3.dll";

static INIT_ONCE g_once = INIT_ONCE_STATIC_INIT;
static HMODULE g_real;

static BOOL CALLBACK resolve(PINIT_ONCE, PVOID, PVOID*)
{
    g_real = load_system_dll(L"xinput1_3.dll");
    const char* which = "xinput1_3";
    if (!g_real) { g_real = load_system_dll(L"xinput1_4.dll"); which = "xinput1_4"; }
    logf("loader: system XInput resolved: %s", g_real ? which : "FAILED");
    return TRUE;
}

static FARPROC real(const char* nameOrOrdinal)
{
    InitOnceExecuteOnce(&g_once, resolve, nullptr, nullptr);
    return g_real ? GetProcAddress(g_real, nameOrOrdinal) : nullptr;
}

template <typename Fn>
static Fn get(const char* nameOrOrdinal)
{
    return reinterpret_cast<Fn>(real(nameOrOrdinal));
}

#define ORDINAL(n) reinterpret_cast<const char*>(static_cast<ULONG_PTR>(n))

constexpr DWORD kNotConnected = ERROR_DEVICE_NOT_CONNECTED;

extern "C" {

DWORD WINAPI XInputGetState(DWORD user, void* state)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, void*)>("XInputGetState");
    return fn ? fn(user, state) : kNotConnected;
}

DWORD WINAPI XInputSetState(DWORD user, void* vibration)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, void*)>("XInputSetState");
    return fn ? fn(user, vibration) : kNotConnected;
}

DWORD WINAPI XInputGetCapabilities(DWORD user, DWORD flags, void* caps)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, DWORD, void*)>("XInputGetCapabilities");
    return fn ? fn(user, flags, caps) : kNotConnected;
}

void WINAPI XInputEnable(BOOL enable)
{
    static auto fn = get<void(WINAPI*)(BOOL)>("XInputEnable");
    if (fn) fn(enable);
}

DWORD WINAPI XInputGetDSoundAudioDeviceGuids(DWORD user, GUID* render, GUID* capture)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, GUID*, GUID*)>("XInputGetDSoundAudioDeviceGuids");
    return fn ? fn(user, render, capture) : kNotConnected;
}

DWORD WINAPI XInputGetBatteryInformation(DWORD user, BYTE type, void* info)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, BYTE, void*)>("XInputGetBatteryInformation");
    return fn ? fn(user, type, info) : kNotConnected;
}

DWORD WINAPI XInputGetKeystroke(DWORD user, DWORD reserved, void* keystroke)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, DWORD, void*)>("XInputGetKeystroke");
    return fn ? fn(user, reserved, keystroke) : kNotConnected;
}

// Unnamed exports (ordinals 100-103).
DWORD WINAPI XInputGetStateEx(DWORD user, void* state)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, void*)>(ORDINAL(100));
    return fn ? fn(user, state) : kNotConnected;
}

DWORD WINAPI XInputWaitForGuideButton(DWORD user, DWORD flags, void* listen)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD, DWORD, void*)>(ORDINAL(101));
    return fn ? fn(user, flags, listen) : kNotConnected;
}

DWORD WINAPI XInputCancelGuideButtonWait(DWORD user)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD)>(ORDINAL(102));
    return fn ? fn(user) : kNotConnected;
}

DWORD WINAPI XInputPowerOffController(DWORD user)
{
    static auto fn = get<DWORD(WINAPI*)(DWORD)>(ORDINAL(103));
    return fn ? fn(user) : kNotConnected;
}

} // extern "C"

#endif
