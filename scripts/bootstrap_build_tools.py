#!/usr/bin/env python3
"""Provision a workspace-private macOS ARM64 toolchain from SHA256-locked inputs."""
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tarfile
import zipfile
from build_application import ROOT

def main():
    if platform.system()!='Darwin' or platform.machine()!='arm64': raise SystemExit('This tested cross-build recipe requires macOS ARM64. Target computers never run this script.')
    for name in ('clang','clang++','make','cmake'):
        if not shutil.which(name): raise SystemExit('Missing build-host tool: '+name)
    subprocess.run([sys.executable,str(ROOT/'scripts/fetch_dependencies.py'),'--verify-only'],check=True)
    tool=ROOT/'build/toolchain'
    for archive, dest in [('roslyn-4.12.0.zip','roslyn'),('net471-reference.zip','net471-reference'),('nsis-3.10.zip','nsis-dist')]:
        with zipfile.ZipFile(ROOT/'vendor/downloads'/archive) as z:
            for info in z.infolist():
                path=(tool/dest/info.filename).resolve()
                if not path.is_relative_to((tool/dest).resolve()): raise ValueError('Unsafe ZIP path')
            z.extractall(tool/dest)
    for archive,dest in [('dotnet-runtime-osx-arm64.tar.gz',tool/'dotnet'),('nsis-3.10-src.tar.bz2',tool/'nsis-source'),('scons.tar.gz',tool/'scons'),('llvm-mingw-20231128.tar.xz',tool)]:
        with tarfile.open(ROOT/'vendor/downloads'/archive) as t: t.extractall(dest,filter='data')
    with zipfile.ZipFile(ROOT/'vendor/downloads/cppcheck-2.10-source.zip') as z: z.extractall(ROOT/'build/cppcheck-source')
    dest=ROOT/'OfflineRelease/Tools/ripgrep';dest.mkdir(parents=True,exist_ok=True)
    with zipfile.ZipFile(ROOT/'vendor/downloads/ripgrep-13.0.0-x64.zip') as z:
        for f in z.infolist():
            if f.is_dir():continue
            relative=Path(f.filename).relative_to('ripgrep-13.0.0-x86_64-pc-windows-msvc')
            path=dest/relative;path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(z.read(f))
    env=dict(os.environ);env['PYTHONPATH']=str(tool/'scons/SCons-4.8.1')
    args=[sys.executable,'-m','SCons','VERSION=3.10','VER_PACKED=0x03010000','SKIPSTUBS=all','SKIPPLUGINS=all','SKIPUTILS=all','SKIPMISC=all','NSIS_CONFIG_CONST_DATA_PATH=no','PREFIX='+str(tool/'nsis-dist/nsis-3.10'),'install-compiler','-j4']
    subprocess.run(args,cwd=tool/'nsis-source/nsis-3.10-src',env=env,check=True)
    print('Private cross-build toolchain ready; nothing was installed globally.')

if __name__=='__main__':main()
