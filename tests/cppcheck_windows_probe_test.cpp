#include "simplecpp.h"
#include "cppcheck-path-policy.h"
#include <cassert>
#include <iostream>

std::wstring testSourceRoot;
std::vector<std::string> testDeniedProbes, testAllowedProbes;

int main() {
    try {
        // Execute upstream realFilename(), not a duplicate of its prefix loop.
        testSourceRoot = L"Z:\\parent\\project";
        assert(simplecpp::simplifyPath("Z:\\parent\\project\\src\\a.cpp") == "Z:/parent/project/src/a.cpp");
        assert(testDeniedProbes.size() == 1); // Parent must fail without filesystem access.
        assert(testAllowedProbes.size() == 3);
        testDeniedProbes.clear(); testAllowedProbes.clear();
        testSourceRoot = L"\\\\server\\share\\project";
        assert(simplecpp::simplifyPath("\\\\server\\share\\project\\src\\a.cpp") == "//server/share/project/src/a.cpp");
        assert(testDeniedProbes.size() == 2); // Server and share probes.
        assert(testAllowedProbes.size() == 3);
        // Root scope probes, ordinary missing names and typed failure semantics.
        testSourceRoot = L"\\\\host\\data";
        assert(simplecpp::simplifyPath("\\\\host\\data\\a.c") == "//host/data/a.c");
        assert(tss_metadata_lookup([]() { return 7; }, -1) == 7);
        assert(tss_metadata_lookup([]() -> int { throw TssPathDenied("denied"); }, -1) == -1);
        bool unexpectedPropagated = false;
        try { tss_metadata_lookup([]() -> int { throw std::runtime_error("unexpected"); }, -1); }
        catch (const std::runtime_error&) { unexpectedPropagated = true; }
        assert(unexpectedPropagated);
        std::cout << "PASS upstream Windows simplecpp UNC/mapped ancestor probes; scoped metadata denial\n";
    } catch (const std::exception& e) {
        std::cerr << e.what() << '\n';
        return 1;
    }
}
