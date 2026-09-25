// Proxy loader for Dark Souls II: Scholar of the First Sin. Built as dinput8.dll or
// xinput1_3.dll (see Loader.vcxproj, property Proxy); the forwarded exports live in
// proxy_<name>.cpp, everything else is shared.
//
// In DllMain it asks dearxan to neuter Arxan; dearxan invokes our callback at the
// game's entry point, where we load the managed core and let it install hooks.
// Under Seamless Co-op the start is different, see the Seamless Co-op section below.
//
// Everything here must fail soft: if anything is missing the game runs vanilla.

#include "loader.h"
#include <stdio.h>
#include <string>
#include <intrin.h>

#include "dearxan.h"
#include "MinHook.h"
#include "loader_api.h"

static HMODULE g_self;
static std::wstring g_mod_dir;
static SRWLOCK g_log_lock = SRWLOCK_INIT;
static FILE* g_log;
static DkpLoaderApi g_api;

void log_line(const char* line)
{
    AcquireSRWLockExclusive(&g_log_lock);
    if (g_log) {
        SYSTEMTIME t;
        GetLocalTime(&t);
        fprintf(g_log, "%02d:%02d:%02d.%03d [%5lu] %s\n", t.wHour, t.wMinute, t.wSecond,
                t.wMilliseconds, GetCurrentThreadId(), line);
        fflush(g_log);
    }
    ReleaseSRWLockExclusive(&g_log_lock);
}

void logf(const char* fmt, ...)
{
    char buf[1024];
    va_list ap;
    va_start(ap, fmt);
    vsnprintf(buf, sizeof buf, fmt, ap);
    va_end(ap);
    log_line(buf);
}

HMODULE load_system_dll(const wchar_t* name)
{
    wchar_t path[MAX_PATH];
    UINT n = GetSystemDirectoryW(path, MAX_PATH);
    if (n == 0 || n + 1 + wcslen(name) >= MAX_PATH)
        return nullptr;
    wcscat_s(path, L"\\");
    wcscat_s(path, name);
    return LoadLibraryW(path);
}

// ---------------------------------------------------------------- hooks API for the core

static int api_create_hook(void* target, void* detour, void** original)
{
    return MH_CreateHook(target, detour, original);
}
static int api_enable_hook(void* target) { return MH_EnableHook(target); }
static int api_disable_hook(void* target) { return MH_DisableHook(target); }

// ---------------------------------------------------------------- core exception handler guard
//
// The .NET NativeAOT runtime in the core registers a process-wide vectored exception handler
// (for faults in managed code). It then sees every first-chance exception of the game and of
// other mods, and on some systems it turns exceptions that the game or Seamless Co-op handle
// themselves into a crash (STATUS_INVALID_DISPOSITION). While the core initialises, its
// registration is replaced by a filter that forwards only exceptions raised inside the core.

using RtlAddVehFn = PVOID(NTAPI*)(ULONG, PVECTORED_EXCEPTION_HANDLER);
static RtlAddVehFn g_real_add_veh;
static void* g_add_veh_target;
static DWORD g_core_init_thread;
static PVECTORED_EXCEPTION_HANDLER g_core_veh;
static volatile uintptr_t g_core_lo, g_core_hi;

static LONG CALLBACK core_veh_filter(PEXCEPTION_POINTERS p)
{
    uintptr_t ip = static_cast<uintptr_t>(p->ContextRecord->Rip);
    if (ip < g_core_lo || ip >= g_core_hi || !g_core_veh)
        return EXCEPTION_CONTINUE_SEARCH;
    return g_core_veh(p);
}

static PVOID NTAPI add_veh_hook(ULONG first, PVECTORED_EXCEPTION_HANDLER handler)
{
    if (GetCurrentThreadId() == g_core_init_thread && !g_core_veh) {
        g_core_veh = handler;
        log_line("loader: core exception handler registered behind a filter");
        return g_real_add_veh(first, core_veh_filter);
    }
    return g_real_add_veh(first, handler);
}

static void guard_core_veh(bool on)
{
    if (on) {
        HMODULE ntdll = GetModuleHandleW(L"ntdll.dll");
        g_add_veh_target = ntdll ? reinterpret_cast<void*>(GetProcAddress(ntdll, "RtlAddVectoredExceptionHandler")) : nullptr;
        g_core_init_thread = GetCurrentThreadId();
        if (!g_add_veh_target
            || MH_CreateHook(g_add_veh_target, reinterpret_cast<void*>(&add_veh_hook), reinterpret_cast<void**>(&g_real_add_veh)) != MH_OK
            || MH_EnableHook(g_add_veh_target) != MH_OK)
            log_line("loader: WARNING: cannot guard the core exception handler");
    } else if (g_add_veh_target) {
        MH_DisableHook(g_add_veh_target);
        MH_RemoveHook(g_add_veh_target);
        g_core_init_thread = 0;
    }
}

static void set_core_range(HMODULE m)
{
    auto base = reinterpret_cast<uintptr_t>(m);
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + reinterpret_cast<const IMAGE_DOS_HEADER*>(base)->e_lfanew);
    g_core_hi = base + nt->OptionalHeader.SizeOfImage;
    g_core_lo = base;
}

// ---------------------------------------------------------------- Seamless Co-op
//
// Seamless Co-op's launcher starts the game suspended and loads ds2sc.dll from a remote thread;
// that same thread loads us first while it initialises the process. With dearxan Seamless
// crashes on some systems, so under Seamless we wait until that thread exits (ds2sc.dll is
// loaded by then and the game's main thread is still suspended) and start without dearxan.
//
// [Loader] section of DynamicKeyPrompts.ini, for diagnosing conflicts:
//   Defer=0     under Seamless, start the usual way (dearxan, right away)
//   Dearxan=1   under Seamless, still use dearxan (after ds2sc.dll has loaded)

static int ini_int(const wchar_t* key, int def)
{
    std::wstring ini = g_mod_dir + L"\\DynamicKeyPrompts.ini";
    return static_cast<int>(GetPrivateProfileIntW(L"Loader", key, def, ini.c_str()));
}

// Detected by the parent process: ds2sc.dll itself is loaded only after our DllMain has run.
static bool started_by_seamless()
{
    if (GetModuleHandleW(L"ds2sc.dll"))
        return true;

    struct BasicInfo { LONG exit; PVOID peb; ULONG_PTR affinity; LONG prio; ULONG_PTR pid, parent; } info{};
    using QueryFn = LONG(NTAPI*)(HANDLE, int, PVOID, ULONG, PULONG);
    auto query = reinterpret_cast<QueryFn>(GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "NtQueryInformationProcess"));
    if (!query || query(GetCurrentProcess(), 0, &info, sizeof info, nullptr) < 0)
        return false;

    HANDLE parent = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, static_cast<DWORD>(info.parent));
    if (!parent)
        return false;
    wchar_t path[MAX_PATH];
    DWORD n = MAX_PATH;
    bool seamless = false;
    if (QueryFullProcessImageNameW(parent, 0, path, &n)) {
        const wchar_t* name = wcsrchr(path, L'\\');
        seamless = _wcsicmp(name ? name + 1 : path, L"ds2sc_launcher.exe") == 0;
    }
    CloseHandle(parent);
    return seamless;
}

// ---------------------------------------------------------------- startup

static HMODULE g_core_preloaded;

static void load_core(bool arxan_detected, int arxan_status)
{
    MH_STATUS mh = MH_Initialize();
    if (mh != MH_OK && mh != MH_ERROR_ALREADY_INITIALIZED) {
        logf("loader: MH_Initialize failed: %d", mh);
        return;
    }

    std::wstring core = g_mod_dir + L"\\DynamicKeyPrompts.dll";
    guard_core_veh(true);
    HMODULE m = g_core_preloaded ? g_core_preloaded : LoadLibraryW(core.c_str());
    if (!m) {
        guard_core_veh(false);
        logf("loader: cannot load core DLL (error %lu) - running vanilla", GetLastError());
        return;
    }
    set_core_range(m);
    auto init = reinterpret_cast<DkpInitFn>(GetProcAddress(m, "DKP_Init"));
    if (!init) {
        guard_core_veh(false);
        log_line("loader: core has no DKP_Init export");
        return;
    }

    g_api.size = sizeof g_api;
    g_api.version = DKP_LOADER_API_VERSION;
    g_api.game_base = GetModuleHandleW(nullptr);
    g_api.mod_dir = g_mod_dir.c_str();
    g_api.arxan_detected = arxan_detected ? 1 : 0;
    g_api.arxan_status = arxan_status;
    g_api.log = log_line;
    g_api.create_hook = api_create_hook;
    g_api.enable_hook = api_enable_hook;
    g_api.disable_hook = api_disable_hook;

    int rc = init(&g_api); // the .NET runtime starts here and registers its exception handler
    guard_core_veh(false);
    logf("loader: core init returned %d", rc);
}

static void start_mod()
{
    // dearxan must be called before the game's entry point runs; the callback runs at
    // the entry point on the main thread, after DllMain has returned.
    try {
        dearxan::neuter_arxan([](const dearxan::DearxanResult& r) {
            logf("loader: dearxan status=%d arxan_detected=%d blocking_entrypoint=%d%s%s",
                 r.status(), r.is_arxan_detected(), r.is_executing_entrypoint(),
                 r.status() == dearxan::detail::DearxanSuccess ? "" : " error=",
                 r.status() == dearxan::detail::DearxanSuccess ? "" : r.error_msg().c_str());
            try {
                load_core(r.is_arxan_detected(), r.status());
            } catch (...) {
                log_line("loader: exception while loading core");
            }
        });
    } catch (...) {
        log_line("loader: exception from neuter_arxan");
    }
}

// Under Seamless Co-op the core is started without dearxan (on some systems dearxan and Seamless
// crash together) and without patching the game (SteamStub checks its own entry code). Instead
// the first GetSystemTimeAsFileTime call made from the game's .text is caught: that is the CRT's
// security cookie setup at the real entry point, after SteamStub has unpacked the game.
using GetTimeFn = void(WINAPI*)(LPFILETIME);
static GetTimeFn g_real_get_time;
static void* g_get_time_target;
static uintptr_t g_text_lo, g_text_hi;
static volatile LONG g_startup_caught;

static void WINAPI get_time_hook(LPFILETIME ft)
{
    g_real_get_time(ft);
    auto caller = reinterpret_cast<uintptr_t>(_ReturnAddress());
    if (caller < g_text_lo || caller >= g_text_hi || InterlockedExchange(&g_startup_caught, 1))
        return;
    MH_DisableHook(g_get_time_target);
    log_line("loader: game code reached - starting without dearxan");
    try {
        load_core(false, 0);
    } catch (...) {
        log_line("loader: exception while loading core");
    }
}

static void start_at_game_code()
{
    auto base = reinterpret_cast<uintptr_t>(GetModuleHandleW(nullptr));
    auto nt = reinterpret_cast<const IMAGE_NT_HEADERS*>(base + reinterpret_cast<const IMAGE_DOS_HEADER*>(base)->e_lfanew);
    const IMAGE_SECTION_HEADER* s = IMAGE_FIRST_SECTION(nt);
    for (WORD i = 0; i < nt->FileHeader.NumberOfSections; i++, s++) {
        if (memcmp(s->Name, ".text", 6) == 0) {
            g_text_lo = base + s->VirtualAddress;
            g_text_hi = g_text_lo + s->Misc.VirtualSize;
            break;
        }
    }
    g_get_time_target = reinterpret_cast<void*>(GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "GetSystemTimeAsFileTime"));
    MH_STATUS mh = MH_Initialize();
    if (g_text_lo && g_get_time_target && (mh == MH_OK || mh == MH_ERROR_ALREADY_INITIALIZED)
        && MH_CreateHook(g_get_time_target, reinterpret_cast<void*>(&get_time_hook), reinterpret_cast<void**>(&g_real_get_time)) == MH_OK
        && MH_EnableHook(g_get_time_target) == MH_OK)
        log_line("loader: waiting for the game code to start");
    else
        log_line("loader: cannot catch the game start - running vanilla");
}

static DWORD g_deferred_thread;

static void attach()
{
    // Both variants (dinput8.dll and xinput1_3.dll) may be installed by mistake: only the
    // first one to load starts the mod, the other just forwards its exports.
    wchar_t mutexName[64];
    swprintf_s(mutexName, L"Local\\DynamicKeyPrompts.Loader.%lu", GetCurrentProcessId());
    CreateMutexW(nullptr, FALSE, mutexName);
    if (GetLastError() == ERROR_ALREADY_EXISTS)
        return;

    wchar_t path[MAX_PATH];
    DWORD n = GetModuleFileNameW(g_self, path, MAX_PATH);
    std::wstring dir(path, n);
    dir.resize(dir.find_last_of(L"\\/"));
    g_mod_dir = dir + L"\\DynamicKeyPrompts";
    CreateDirectoryW(g_mod_dir.c_str(), nullptr);

    std::wstring log_path = g_mod_dir + L"\\DynamicKeyPrompts.log";
    g_log = _wfsopen(log_path.c_str(), L"w", _SH_DENYWR);
    logf("loader: DynamicKeyPrompts loader attached as %s", g_proxy_name);

    if (started_by_seamless() && ini_int(L"Defer", 1)) {
        g_deferred_thread = GetCurrentThreadId();
        log_line("loader: started by Seamless Co-op - waiting for it to finish loading");
        return;
    }
    start_mod();
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        attach();
        if (!g_deferred_thread)
            DisableThreadLibraryCalls(inst);
    } else if (reason == DLL_THREAD_DETACH && g_deferred_thread == GetCurrentThreadId()) {
        g_deferred_thread = 0;
        logf("loader: Seamless Co-op loaded (ds2sc.dll %s)", GetModuleHandleW(L"ds2sc.dll") ? "present" : "not found");
        if (ini_int(L"Dearxan", 0)) {
            start_mod();
        } else {
            // Loading the core at the game's start fails without dearxan (error 18), so it is
            // mapped now; the .NET runtime only starts when DKP_Init is called.
            std::wstring core = g_mod_dir + L"\\DynamicKeyPrompts.dll";
            g_core_preloaded = LoadLibraryW(core.c_str());
            if (!g_core_preloaded)
                logf("loader: cannot load core DLL (error %lu) - running vanilla", GetLastError());
            else
                start_at_game_code();
        }
        DisableThreadLibraryCalls(inst);
    }
    return TRUE;
}
