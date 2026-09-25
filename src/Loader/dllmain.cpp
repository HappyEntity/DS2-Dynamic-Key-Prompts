// Proxy loader for Dark Souls II: Scholar of the First Sin. Built as dinput8.dll or
// xinput1_3.dll (see Loader.vcxproj, property Proxy); the forwarded exports live in
// proxy_<name>.cpp, everything else is shared.
//
// In DllMain it asks dearxan to neuter Arxan; dearxan invokes our callback at the
// game's entry point, where we load the managed core and let it install hooks.
//
// Everything here must fail soft: if anything is missing the game runs vanilla.

#include "loader.h"
#include <stdio.h>
#include <string>

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

// ---------------------------------------------------------------- startup

static void load_core(const dearxan::DearxanResult& r)
{
    logf("loader: dearxan status=%d arxan_detected=%d blocking_entrypoint=%d%s%s",
         r.status(), r.is_arxan_detected(), r.is_executing_entrypoint(),
         r.status() == dearxan::detail::DearxanSuccess ? "" : " error=",
         r.status() == dearxan::detail::DearxanSuccess ? "" : r.error_msg().c_str());

    MH_STATUS mh = MH_Initialize();
    if (mh != MH_OK && mh != MH_ERROR_ALREADY_INITIALIZED) {
        logf("loader: MH_Initialize failed: %d", mh);
        return;
    }

    std::wstring core = g_mod_dir + L"\\DynamicKeyPrompts.dll";
    HMODULE m = LoadLibraryW(core.c_str());
    if (!m) {
        logf("loader: cannot load core DLL (error %lu) - running vanilla", GetLastError());
        return;
    }
    auto init = reinterpret_cast<DkpInitFn>(GetProcAddress(m, "DKP_Init"));
    if (!init) {
        log_line("loader: core has no DKP_Init export");
        return;
    }

    g_api.size = sizeof g_api;
    g_api.version = DKP_LOADER_API_VERSION;
    g_api.game_base = GetModuleHandleW(nullptr);
    g_api.mod_dir = g_mod_dir.c_str();
    g_api.arxan_detected = r.is_arxan_detected() ? 1 : 0;
    g_api.arxan_status = r.status();
    g_api.log = log_line;
    g_api.create_hook = api_create_hook;
    g_api.enable_hook = api_enable_hook;
    g_api.disable_hook = api_disable_hook;

    int rc = init(&g_api);
    logf("loader: core init returned %d", rc);
}

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

    // dearxan must be called before the game's entry point runs; the callback runs at
    // the entry point on the main thread, after DllMain has returned.
    try {
        dearxan::neuter_arxan([](const dearxan::DearxanResult& r) {
            try {
                load_core(r);
            } catch (...) {
                log_line("loader: exception while loading core");
            }
        });
    } catch (...) {
        log_line("loader: exception from neuter_arxan");
    }
}

BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID)
{
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        DisableThreadLibraryCalls(inst);
        attach();
    }
    return TRUE;
}
