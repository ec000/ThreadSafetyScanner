#include "cppcheck-path-policy.h"
#include <cassert>
#include <iostream>

int main() {
    assert(tss_network_read_allowed(L"Z:\\项目 空格\\a.cpp", L"Z:\\项目 空格"));
    assert(tss_network_read_allowed(L"z:\\SDK\\include\\a.h", L"Z:\\sdk\\"));
    assert(tss_network_read_allowed(L"Z:/sdk/include/a.h", L"Z:\\sdk"));
    assert(tss_network_read_allowed(L"Z:\\sdk\\a.h", L"Z:\\"));
    assert(!tss_network_read_allowed(L"Z:\\sdk-other\\a.h", L"Z:\\sdk"));
    assert(!tss_network_read_allowed(L"Y:\\sdk\\a.h", L"Z:\\sdk"));
    assert(!tss_network_read_allowed(L"Z:\\sdk\\a.h", L"C:\\sdk"));
    assert(!tss_network_read_allowed(L"Z:\\sdk\\a.h", L""));
    assert(tss_network_read_allowed(L"\\\\server\\share\\项目\\a.h", L"\\\\server\\share\\项目"));
    assert(tss_network_read_allowed(L"\\\\SERVER\\Share\\a.h", L"\\\\server\\share\\"));
    assert(!tss_network_read_allowed(L"\\\\other\\share\\a.h", L"\\\\server\\share"));
    assert(!tss_network_read_allowed(L"\\\\server\\share-other\\a.h", L"\\\\server\\share"));
    assert(!tss_network_read_allowed(L"\\\\server\\share\\outside\\a.h", L"\\\\server\\share\\project"));
    assert(tss_device_path(L"\\\\?\\UNC\\server\\share\\a.h"));
    assert(tss_device_path(L"//./pipe/input"));
    assert(!tss_network_read_allowed(L"\\\\?\\Z:\\sdk\\a.h", L"\\\\?\\Z:\\sdk"));
    assert(!tss_network_read_allowed(L"\\\\.\\pipe\\input", L"\\\\.\\pipe"));
    std::cout << "PASS 17 SMB path boundary/device assertions\n";
}
