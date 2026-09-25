// Shared helpers of the loader (dllmain.cpp) used by the proxy export files.
#pragma once
#define WIN32_LEAN_AND_MEAN
#include <windows.h>

void log_line(const char* line);
void logf(const char* fmt, ...);

// Loads <System32>\name. Must not be called from DllMain (loader lock).
HMODULE load_system_dll(const wchar_t* name);

// The DLL name this build impersonates, e.g. "dinput8.dll".
extern const char* const g_proxy_name;
