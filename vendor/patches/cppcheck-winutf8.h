// ThreadSafetyScanner compatibility patch for Cppcheck 2.10 (2026-10-10).
// SPDX-License-Identifier: GPL-3.0-or-later
// Force-included only when cross-compiling with llvm-mingw/libc++ for Windows.
// Treat CLI paths as UTF-8 and use Win32 W APIs and wide fstream filenames.
#pragma once
#include <windows.h>
#include <shlwapi.h>
#include <fstream>
#include <cstdio>
#include <cstring>
#include <string>
#include <vector>
#include <direct.h>
#include <stdexcept>
#ifdef ERROR
#undef ERROR
#endif
#undef TRUE
#undef FALSE
#undef VOID
static const BOOL TRUE = 1;
static const BOOL FALSE = 0;
typedef void VOID;

inline std::wstring tss_wide(const char* s) {
    if (!s || !*s) return std::wstring();
    int n = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s, -1, nullptr, 0);
    if (!n) throw std::runtime_error("Invalid UTF-8 filename");
    std::vector<wchar_t> b(static_cast<size_t>(n));
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, s, -1, b.data(), n);
    return std::wstring(b.data());
}
inline std::string tss_utf8(const wchar_t* s) {
    int n = WideCharToMultiByte(CP_UTF8, 0, s, -1, nullptr, 0, nullptr, nullptr);
    if (!n) throw std::runtime_error("Invalid wide filename");
    std::vector<char> b(static_cast<size_t>(n));
    WideCharToMultiByte(CP_UTF8, 0, s, -1, b.data(), n, nullptr, nullptr);
    return std::string(b.data());
}
inline std::wstring tss_path(const char* s) {
    std::wstring w = tss_wide(s);
    if (w.size() >= 2 && (w[0] == L'\\' || w[0] == L'/') && (w[1] == L'\\' || w[1] == L'/'))
        throw std::runtime_error("Offline policy: UNC/device filename denied");
    wchar_t full[32768];
    DWORD n = GetFullPathNameW(w.c_str(), 32768, full, nullptr);
    if (!n || n >= 32768) throw std::runtime_error("Offline policy: cannot normalize filename");
    std::wstring path(full);
    if (path.size() < 3 || path[1] != L':') throw std::runtime_error("Offline policy: local drive required");
    std::wstring root = path.substr(0, 3);
    if (GetDriveTypeW(root.c_str()) == DRIVE_REMOTE) throw std::runtime_error("Offline policy: mapped network drive denied");
    // Inspect each prefix before traversing it; never follow a link to a remote header.
    for (std::size_t i = 3; i <= path.size(); ++i) {
        if (i != path.size() && path[i] != L'\\' && path[i] != L'/') continue;
        DWORD a = GetFileAttributesW(path.substr(0, i).c_str());
        if (a != INVALID_FILE_ATTRIBUTES && (a & FILE_ATTRIBUTE_REPARSE_POINT))
            throw std::runtime_error("Offline policy: reparse-point filename denied");
    }
    return w;
}
namespace std {
class tss_ifstream : public ifstream {
public:
    tss_ifstream() = default;
    explicit tss_ifstream(const char* s, ios_base::openmode m = ios_base::in) : ifstream(tss_path(s).c_str(), m) {}
    explicit tss_ifstream(const string& s, ios_base::openmode m = ios_base::in) : tss_ifstream(s.c_str(), m) {}
    void open(const char* s, ios_base::openmode m = ios_base::in) { ifstream::open(tss_path(s).c_str(), m); }
    void open(const string& s, ios_base::openmode m = ios_base::in) { open(s.c_str(), m); }
};
class tss_ofstream : public ofstream {
public:
    tss_ofstream() = default;
    explicit tss_ofstream(const char* s, ios_base::openmode m = ios_base::out) : ofstream(tss_path(s).c_str(), m) {}
    explicit tss_ofstream(const string& s, ios_base::openmode m = ios_base::out) : tss_ofstream(s.c_str(), m) {}
    void open(const char* s, ios_base::openmode m = ios_base::out) { ofstream::open(tss_path(s).c_str(), m); }
    void open(const string& s, ios_base::openmode m = ios_base::out) { open(s.c_str(), m); }
};
}
inline FILE* tss_fopen(const char* p, const char* m) { return _wfopen(tss_path(p).c_str(), tss_wide(m).c_str()); }
struct TssFindData {
    DWORD dwFileAttributes; FILETIME ftCreationTime, ftLastAccessTime, ftLastWriteTime;
    DWORD nFileSizeHigh, nFileSizeLow, dwReserved0, dwReserved1;
    char cFileName[MAX_PATH * 3 + 1]; char cAlternateFileName[40];
};
inline void tss_find_copy(const WIN32_FIND_DATAW& w, TssFindData* a) {
    a->dwFileAttributes = w.dwFileAttributes; a->nFileSizeHigh = w.nFileSizeHigh; a->nFileSizeLow = w.nFileSizeLow;
    a->ftCreationTime = w.ftCreationTime; a->ftLastAccessTime = w.ftLastAccessTime; a->ftLastWriteTime = w.ftLastWriteTime;
    a->dwReserved0 = w.dwReserved0; a->dwReserved1 = w.dwReserved1;
    std::strcpy(a->cFileName, tss_utf8(w.cFileName).c_str());
    std::strcpy(a->cAlternateFileName, tss_utf8(w.cAlternateFileName).c_str());
}
inline HANDLE tss_find_first(const char* p, TssFindData* a) {
    WIN32_FIND_DATAW w; HANDLE h = FindFirstFileW(tss_path(p).c_str(), &w);
    if (h != INVALID_HANDLE_VALUE) tss_find_copy(w, a); return h;
}
inline BOOL tss_find_next(HANDLE h, TssFindData* a) {
    WIN32_FIND_DATAW w; BOOL r = FindNextFileW(h, &w); if (r) tss_find_copy(w, a); return r;
}
inline HANDLE tss_find_ex(const char* p, FINDEX_INFO_LEVELS i, void* a, FINDEX_SEARCH_OPS o, void* f, DWORD flags) {
    WIN32_FIND_DATAW w; HANDLE h = FindFirstFileExW(tss_path(p).c_str(), i, &w, o, f, flags);
    if (h != INVALID_HANDLE_VALUE) tss_find_copy(w, static_cast<TssFindData*>(a)); return h;
}
inline DWORD tss_module(HMODULE m, char* b, DWORD n) {
    wchar_t w[32768]; DWORD r = GetModuleFileNameW(m, w, 32768);
    if (!r || r >= 32768) return 0;
    std::string s = tss_utf8(w); if (s.size() >= n) return 0;
    std::memcpy(b, s.c_str(), s.size() + 1); return static_cast<DWORD>(s.size());
}
inline char* tss_getcwd(char* b, int n) {
    wchar_t w[32768]; if (!_wgetcwd(w, 32768)) return nullptr;
    std::string s = tss_utf8(w); if (static_cast<int>(s.size()) >= n) return nullptr;
    std::memcpy(b, s.c_str(), s.size() + 1); return b;
}
#define ifstream tss_ifstream
#define ofstream tss_ofstream
#define fopen tss_fopen
#define WIN32_FIND_DATAA TssFindData
#define LPWIN32_FIND_DATAA TssFindData*
#define FindFirstFileA tss_find_first
#define FindNextFileA tss_find_next
#define FindFirstFileExA tss_find_ex
#define GetModuleFileNameA tss_module
#define PathIsDirectoryA(p) PathIsDirectoryW(tss_path(p).c_str())
#define PathFileExistsA(p) PathFileExistsW(tss_path(p).c_str())
#define _getcwd tss_getcwd
