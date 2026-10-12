// Minimal host-side Win32 metadata model for the unmodified simplecpp Windows branch.
// This models API contracts, not a Windows filesystem or a live SMB connection.
#pragma once
#include "cppcheck-path-policy.h"
#include <cstring>
#include <vector>

typedef void* HANDLE;
typedef int CRITICAL_SECTION;
#define INVALID_HANDLE_VALUE reinterpret_cast<HANDLE>(-1)
static const int FindExInfoBasic = 1, FindExSearchNameMatch = 0;
struct WIN32_FIND_DATAA { char cFileName[1024]; };
inline void InitializeCriticalSection(CRITICAL_SECTION*) {}
inline void DeleteCriticalSection(CRITICAL_SECTION*) {}
inline void EnterCriticalSection(CRITICAL_SECTION*) {}
inline void LeaveCriticalSection(CRITICAL_SECTION*) {}
inline void FindClose(HANDLE) {}

extern std::wstring testSourceRoot;
extern std::vector<std::string> testDeniedProbes, testAllowedProbes;
inline HANDLE FindFirstFileExA(const char* path, int, WIN32_FIND_DATAA* data, int, void*, int) {
    auto lookup = [&]() -> HANDLE {
        const std::string name(path);
        if (!tss_network_read_allowed(std::wstring(name.begin(), name.end()), testSourceRoot)) {
            testDeniedProbes.push_back(name);
            throw TssPathDenied("Source policy: network access outside selected source root or network write denied");
        }
        testAllowedProbes.push_back(name);
        const auto pos = name.find_last_of("/\\");
        const std::string leaf = name.substr(pos == std::string::npos ? 0 : pos + 1);
        std::strcpy(data->cFileName, leaf.c_str());
        return reinterpret_cast<HANDLE>(1);
    };
#ifdef TSS_TEST_LEGACY_PROBE
    return lookup(); // Fault injection: reproduce the candidate.3 throwing adapter.
#else
    return tss_metadata_lookup(lookup, INVALID_HANDLE_VALUE);
#endif
}
