// SPDX-License-Identifier: GPL-3.0-or-later
// Paths passed here have already been made absolute by GetFullPathNameW.
#pragma once
#include <string>
#include <cwctype>

inline std::wstring tss_normalize_separators(std::wstring path) {
    for (wchar_t& c : path) if (c == L'/') c = L'\\';
    return path;
}
inline bool tss_device_path(const std::wstring& path) {
    const std::wstring p = tss_normalize_separators(path);
    return p.size() >= 4 && p[0] == L'\\' && p[1] == L'\\' &&
           (p[2] == L'?' || p[2] == L'.') && p[3] == L'\\';
}
inline bool tss_network_read_allowed(const std::wstring& path, const std::wstring& sourceRoot) {
    if (sourceRoot.empty() || tss_device_path(path) || tss_device_path(sourceRoot)) return false;
    std::wstring p = tss_normalize_separators(path), root = tss_normalize_separators(sourceRoot);
    while (!root.empty() && root.back() == L'\\') root.pop_back();
    if (root.empty() || p.size() < root.size()) return false;
    for (std::size_t i = 0; i < root.size(); ++i)
        if (std::towlower(p[i]) != std::towlower(root[i])) return false;
    return p.size() == root.size() || p[root.size()] == L'\\';
}
