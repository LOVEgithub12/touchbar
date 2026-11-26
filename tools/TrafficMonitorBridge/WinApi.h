#pragma once
// Minimal fixed-width Win32 declarations; no CRT, MFC, or managed runtime is needed.
using DWORD = unsigned long;
using LONG = long;
using HANDLE = void*;
using BOOL = int;
using U64 = unsigned long long;
extern "C" {
__declspec(dllimport) HANDLE __stdcall CreateFileMappingW(HANDLE, void*, DWORD, DWORD, DWORD, const wchar_t*);
__declspec(dllimport) void* __stdcall MapViewOfFile(HANDLE, DWORD, DWORD, DWORD, unsigned long long);
__declspec(dllimport) BOOL __stdcall UnmapViewOfFile(const void*);
__declspec(dllimport) BOOL __stdcall CloseHandle(HANDLE);
__declspec(dllimport) DWORD __stdcall GetCurrentProcessId();
__declspec(dllimport) DWORD __stdcall GetLastError();
__declspec(dllimport) HANDLE __stdcall GetModuleHandleW(const wchar_t*);
__declspec(dllimport) void* __stdcall GetProcAddress(HANDLE,const char*);
__declspec(dllimport) BOOL __stdcall QueryPerformanceCounter(long long*);
__declspec(dllimport) BOOL __stdcall QueryPerformanceFrequency(long long*);
__declspec(dllimport) DWORD __stdcall GetPrivateProfileStringW(const wchar_t*,const wchar_t*,const wchar_t*,wchar_t*,DWORD,const wchar_t*);
}
extern "C" long _InterlockedIncrement(volatile long*);
extern "C" long _InterlockedExchange(volatile long*,long);
#pragma intrinsic(_InterlockedIncrement)
#pragma intrinsic(_InterlockedExchange)
