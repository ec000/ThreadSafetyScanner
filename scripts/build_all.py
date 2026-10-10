#!/usr/bin/env python3
"""Build a complete offline candidate from private tools and collected inputs."""
from pathlib import Path
import shutil
import subprocess
import sys
from build_application import ROOT

def run(script,*args):subprocess.run([sys.executable,str(ROOT/'scripts'/script)]+[str(a) for a in args],cwd=ROOT,check=True)

def main():
    tool=ROOT/'build/toolchain'
    dotnet=tool/'dotnet/dotnet';csc=tool/'roslyn/tasks/netcore/bincore/csc.dll';refs=tool/'net471-reference/build/.NETFramework/v4.7.1'
    run('fetch_dependencies.py','--verify-only')
    run('build_cppcheck.py','--toolchain',tool/'llvm-mingw-20231128-ucrt-macos-universal')
    common=['--dotnet',dotnet,'--csc',csc,'--references',refs]
    run('build_application.py',*common);run('build_validation.py',*common)
    run('collect_rust_licenses.py')
    for name in ('README_中文.md','TEST-REPORT.zh-CN.md','WINDOWS1709-CHECKLIST.zh-CN.md','BUILDING.zh-CN.md'):
        shutil.copy2(ROOT/'docs'/name,ROOT/'OfflineRelease/Application'/name)
    run('collect_payload_metadata.py')
    subprocess.run([sys.executable,'-m','unittest','discover','-s','tests','-p','test_*.py','-v'],cwd=ROOT,check=True)
    run('build_release.py','--makensis',tool/'nsis-dist/nsis-3.10/makensis')
    print('Candidate built. Exact Windows 1709 runtime/visual checks remain mandatory; no verified Release was created.')

if __name__=='__main__':main()
