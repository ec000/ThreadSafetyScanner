#!/usr/bin/env python3
"""Build a private static x64 Cppcheck from the collected official 2.10 source."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def run(args):
    subprocess.run([str(x) for x in args], cwd=ROOT, check=True)

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--toolchain', type=Path, required=True)
    p.add_argument('--cmake', default='cmake')
    args = p.parse_args()
    tc = args.toolchain.resolve()
    source = ROOT / 'build/cppcheck-patched/cppcheck-2.10'
    if source.exists():
        shutil.rmtree(source)
    with zipfile.ZipFile(ROOT / 'vendor/downloads/cppcheck-2.10-source.zip') as z:
        z.extractall(source.parent)
    header = ROOT / 'vendor/patches/cppcheck-winutf8.h'
    shutil.copy2(header, source / header.name)
    shutil.copy2(ROOT / 'vendor/patches/cppcheck-path-policy.h', source / 'cppcheck-path-policy.h')
    main_file = source / 'cli/main.cpp'
    text = main_file.read_text()
    old = 'int main(int argc, char* argv[])\n{'
    new = '''int wmain(int argc, wchar_t* wide_argv[])
{
    std::vector<std::string> utf8_args;
    for (int i = 0; i < argc; ++i) utf8_args.push_back(tss_utf8(wide_argv[i]));
    std::vector<char*> argv_storage;
    for (int i = 0; i < argc; ++i) argv_storage.push_back(&utf8_args[i][0]);
    char** argv = argv_storage.data();'''
    assert old in text, 'Unexpected Cppcheck source; entry point patch refused'
    main_file.write_text(text.replace(old, new, 1))
    small = source / 'lib/smallvector.h'
    small_text = small.read_text()
    needle = 'struct TaggedAllocator : std::allocator<T>\n{'
    assert needle in small_text, 'Unexpected allocator; patch refused'
    small.write_text(small_text.replace(needle, needle + '\n    template<class U> struct rebind { using other = TaggedAllocator<U, N>; };', 1))
    cli_cmake = source / 'cli/CMakeLists.txt'
    cli_cmake.write_text(cli_cmake.read_text().replace('add_dependencies(cppcheck run-dmake)', '# Cross-build: dmake only regenerates build project files; use shipped files.'))
    shutil.copy2(Path(__file__), source/'build_cppcheck.py')
    (source / 'TSS-MODIFICATIONS.txt').write_text('ThreadSafetyScanner 2026-10-12: UTF-8 Windows filename wrappers; allow read-only SMB mapped-drive/UNC access within TSS_SOURCE_ROOT; deny device/reparse paths and network writes; return failed metadata probes instead of throwing for out-of-scope ancestor lookup; denied file opens include requested path/root/access; wmain; libc++ allocator rebind fix; disable host dmake dependency for cross-build; static llvm-mingw build. See bundled build_cppcheck.py, cppcheck-winutf8.h and cppcheck-path-policy.h. No scanner check logic changes.\n')
    build = ROOT / 'build/cppcheck-win64'
    common = '-D_WIN32_WINNT=0x0601 -DWINVER=0x0601 -DWIN32_LEAN_AND_MEAN -DNOMINMAX'
    run([args.cmake, '-S', source, '-B', build, '-DCMAKE_POLICY_VERSION_MINIMUM=3.5', '-DCMAKE_SYSTEM_NAME=Windows', '-DCMAKE_TRY_COMPILE_TARGET_TYPE=STATIC_LIBRARY',
         '-DCMAKE_BUILD_TYPE=Release', '-DCMAKE_C_COMPILER=' + str(tc/'bin/x86_64-w64-mingw32-clang'),
         '-DCMAKE_CXX_COMPILER=' + str(tc/'bin/x86_64-w64-mingw32-clang++'),
         '-DCMAKE_RC_COMPILER=' + str(tc/'bin/x86_64-w64-mingw32-windres'),
         '-DCMAKE_C_FLAGS=' + common, '-DCMAKE_CXX_FLAGS=' + common + ' -include "' + str(source/header.name) + '"',
         '-DCMAKE_EXE_LINKER_FLAGS=-static -municode', '-DUSE_MATCHCOMPILER=Off', '-DHAVE_RULES=OFF',
         '-DBUILD_GUI=OFF', '-DBUILD_TESTS=OFF', '-DENABLE_OSS_FUZZ=OFF', '-DCMAKE_DISABLE_PRECOMPILE_HEADERS=ON',
         '-DFILESDIR=./'])
    run([args.cmake, '--build', build, '--target', 'cppcheck', '--parallel', '6'])
    dest = ROOT / 'OfflineRelease/Tools/cppcheck'
    dest.mkdir(parents=True, exist_ok=True)
    shutil.copy2(build/'bin/cppcheck.exe', dest/'cppcheck.exe')
    run([tc/'bin/x86_64-w64-mingw32-strip', '--strip-all', dest/'cppcheck.exe'])
    for d in ('cfg', 'platforms'):
        shutil.copytree(source/d, dest/d, dirs_exist_ok=True)
    license_dir = ROOT / 'OfflineRelease/Licenses'
    license_dir.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(license_dir/'cppcheck-2.10-patched-source.zip', 'w', zipfile.ZIP_DEFLATED) as z:
        for file in sorted(source.rglob('*')):
            if file.is_file(): z.write(file, str(file.relative_to(source.parent)))
    print('Static Cppcheck x64 collected; Windows 1709 execution still pending.')

if __name__ == '__main__':
    main()
