#!/usr/bin/env python3
"""Collect licenses, source and provenance from explicitly downloaded inputs; no network."""
import hashlib
import json
from pathlib import Path
import shutil
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    stage = ROOT/'OfflineRelease'; licenses = stage/'Licenses'
    cpp = ROOT/'build/cppcheck-source/cppcheck-2.10'
    tc = ROOT/'build/toolchain/llvm-mingw-20231128-ucrt-macos-universal'
    nsis = ROOT/'build/toolchain/nsis-dist/nsis-3.10'
    def copy(src, dst):
        dst.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(src, dst)
    copy(cpp/'COPYING', licenses/'GPL-3.0.txt')
    copy(cpp/'COPYING', ROOT/'LICENSE')
    for name in ('COPYING','LICENSE-MIT','UNLICENSE'): copy(stage/'Tools/ripgrep'/name, licenses/'ripgrep'/name)
    for component, name in (('simplecpp','simplecpp_license.txt'),('tinyxml2','tinyxml2_license.txt'),('picojson','picojson_license.txt')):
        copy(cpp/'externals'/component/'LICENSE', licenses/'cppcheck'/name)
    copy(cpp/'AUTHORS', licenses/'cppcheck/AUTHORS')
    copy(ROOT/'vendor/downloads/cppcheck-2.10-source.zip', licenses/'cppcheck-2.10-original-source.zip')
    copy(tc/'LICENSE.TXT', licenses/'LLVM-LICENSE.TXT')
    for path in (tc/'x86_64-w64-mingw32/share/mingw32').glob('COPYING*'): copy(path, licenses/'mingw'/path.name)
    copy(nsis/'COPYING', licenses/'NSIS-COPYING.txt')
    copy(ROOT/'vendor/downloads/nsis-3.10-src.tar.bz2', licenses/'NSIS-3.10-source.tar.bz2')
    (stage/'Runtime/README.txt').write_text('应用使用 Windows 10 1709 默认的 .NET Framework 4.7.1、UCRT 和 Win32 系统组件。Cppcheck 静态链接 C++ 运行时；ripgrep 官方 MSVC 静态构建。不部署系统级 Python、VC++、Qt、Rust、.NET 8。未打包开发机工具链。需要在 Build 16299.15 验证系统 API 实际可用性。\n')
    notice = '''ThreadSafetyScanner 第三方组件声明

新应用、安装和兼容性补丁：GPL-3.0-or-later，见 GPL-3.0.txt 和随附 ThreadSafetyScanner-source.zip。
ripgrep 13.0.0：MIT OR Unlicense，见 ripgrep/；Rust 依赖锁定版本的完整许可证集合见 ripgrep-dependency-licenses.zip（包括可选和构建依赖的超集）。
Cppcheck 2.10-tss1：GPL-3.0-or-later，原始及修改后的对应源码均随包提供。修改：wmain UTF-8 参数、Win32 W 文件路径、libc++ allocator rebind、移除交叉编译中无关的 dmake 运行、静态运行时，以及拒绝 UNC、映射网络盘和重解析点文件读取。未改变检查规则逻辑。
simplecpp：LGPL-3.0-or-later，见 cppcheck/simplecpp_license.txt；对应源码在 Cppcheck 源码包中。可按随附构建脚本重编译和替换组件，再生成校验清单。
tinyxml2：zlib，见 cppcheck/tinyxml2_license.txt。
picojson：BSD-2-Clause，见 cppcheck/picojson_license.txt。
LLVM libc++/libc++abi/compiler-rt/unwind：Apache-2.0 WITH LLVM-exception（工具链对应 LICENSE.TXT 完整附上）。
MinGW-w64/UCRT import libraries/winpthreads：各部分许可见 mingw/COPYING*。UCRT 本身是 Windows 系统组件，不将开发机系统 DLL 复制到安装包。
NSIS 3.10 安装器及内置插件：zlib/libpng；压缩模块另见 NSIS-COPYING.txt，LZMA 为 CPL-1.0；对应官方源代码 NSIS-3.10-source.tar.bz2 随包提供。
Windows、.NET Framework、系统中文字体由目标操作系统提供，不包含 Microsoft Office、Python、Qt、Semgrep 或系统级运行库安装器。

不使用遥测、在线服务或更新下载。许可证/源代码在 Licenses 目录，可离线查看。
文件校验用于检测损坏；用户自行修改和重编译后可使用随附构建脚本更新清单，不限制 GPL/LGPL 允许的修改和调试。
'''
    (licenses/'THIRD-PARTY-NOTICES.txt').write_text(notice)
    inputs = {
        'ripgrep-13.0.0-x64.zip':'https://github.com/BurntSushi/ripgrep/releases/download/13.0.0/ripgrep-13.0.0-x86_64-pc-windows-msvc.zip',
        'ripgrep-13.0.0-source.tar.gz':'https://codeload.github.com/BurntSushi/ripgrep/tar.gz/refs/tags/13.0.0',
        'cppcheck-2.10-source.zip':'https://downloads.sourceforge.net/project/cppcheck/cppcheck/2.10/cppcheck-2.10.zip',
        'cppcheck-2.10-x64-Setup.msi':'https://downloads.sourceforge.net/project/cppcheck/cppcheck/2.10/cppcheck-2.10-x64-Setup.msi',
        'llvm-mingw-20231128.tar.xz':'https://github.com/mstorsjo/llvm-mingw/releases/download/20231128/llvm-mingw-20231128-ucrt-macos-universal.tar.xz',
        'nsis-3.10.zip':'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.10/nsis-3.10.zip',
        'nsis-3.10-src.tar.bz2':'https://downloads.sourceforge.net/project/nsis/NSIS%203/3.10/nsis-3.10-src.tar.bz2',
        'dotnet-runtime-osx-arm64.tar.gz':'https://builds.dotnet.microsoft.com/dotnet/Runtime/8.0.11/dotnet-runtime-8.0.11-osx-arm64.tar.gz',
        'roslyn-4.12.0.zip':'https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/4.12.0/microsoft.net.compilers.toolset.4.12.0.nupkg',
        'net471-reference.zip':'https://api.nuget.org/v3-flatcontainer/microsoft.netframework.referenceassemblies.net471/1.0.3/microsoft.netframework.referenceassemblies.net471.1.0.3.nupkg',
        'scons.tar.gz':'https://files.pythonhosted.org/packages/source/S/SCons/scons-4.8.1.tar.gz'
    }
    lock = []
    for name, url in inputs.items():
        path = ROOT/'vendor/downloads'/name
        lock.append({'file':name, 'url':url, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(), 'sizeBytes':path.stat().st_size, 'usage':'inspection-only-not-shipped' if name.endswith('.msi') else 'build-input; only explicit collected payload is shipped'})
    lock_path = ROOT/'vendor/dependencies.lock.json'
    if lock_path.is_file():
        expected = {i['file']:i['sha256'] for i in json.loads(lock_path.read_text())}
        mismatches = [i['file'] for i in lock if expected.get(i['file']) != i['sha256']]
        if mismatches: raise SystemExit('Build inputs differ from committed lock; refusing to rewrite hashes: '+', '.join(mismatches))
    else: lock_path.write_text(json.dumps(lock,indent=2)+'\n')
    deps = {}
    for key, version, rel, license_id in (('ripgrep','13.0.0','Tools/ripgrep/rg.exe','MIT OR Unlicense'),('cppcheck','2.10-tss1','Tools/cppcheck/cppcheck.exe','GPL-3.0-or-later')):
        path = stage/rel
        deps[key] = {'version':version, 'path':rel, 'sha256':hashlib.sha256(path.read_bytes()).hexdigest(), 'sizeBytes':path.stat().st_size, 'license':license_id, 'win1709Execution':'pending'}
    deps['application'] = {'framework':'.NET Framework 4.7.1', 'architecture':'x64', 'thirdPartyManagedDlls':[]}
    deps['system'] = ['Windows 10 Pro 1709 x64 .NET 4.7.1','Windows UCRT and Win32 APIs','Windows system CJK fonts']
    deps['staticRuntimes'] = ['ripgrep MSVC static CRT','Cppcheck llvm-mingw 20231128 libc++/libc++abi/compiler-rt/unwind and MinGW support']
    deps['semgrep'] = {'status':'unsupported', 'reason':'No validated Windows Build 16299.15 complete offline distribution'}
    deps['python'] = {'required':False,'shipped':False}
    deps['office'] = {'required':False,'shipped':False}
    (stage/'Manifest/dependencies.json').write_text(json.dumps(deps,ensure_ascii=False,indent=2)+'\n')
    # Refresh exact modified source and its rebuild recipe after any recipe edits.
    patched = ROOT/'build/cppcheck-patched/cppcheck-2.10'
    shutil.copy2(ROOT/'scripts/build_cppcheck.py', patched/'build_cppcheck.py')
    with zipfile.ZipFile(licenses/'cppcheck-2.10-patched-source.zip','w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(patched.rglob('*')):
            if path.is_file(): z.write(path,path.relative_to(patched.parent).as_posix())
    with zipfile.ZipFile(licenses/'ThreadSafetyScanner-source.zip','w',zipfile.ZIP_DEFLATED) as z:
        for directory in ('src','scripts','tests','docs','vendor/patches','OfflineRelease/Rules','OfflineRelease/Installer'):
            for path in sorted((ROOT/directory).rglob('*')):
                if path.is_file() and '__pycache__' not in path.parts and path.suffix != '.pyc' and 'results' not in path.parts: z.write(path,path.relative_to(ROOT).as_posix())
        for name in ('README.md','CHANGELOG.md','LICENSE','vendor/dependencies.lock.json'): z.write(ROOT/name,name)
    print('Collected explicit payload licenses, source archives, tool hashes and build-input lock.')

if __name__ == '__main__': main()
