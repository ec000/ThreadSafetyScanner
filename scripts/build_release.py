#!/usr/bin/env python3
"""Fail-closed offline payload audit and NSIS packaging; never downloads files."""
import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import struct
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
STAGE = ROOT / 'OfflineRelease'
SETUP_NAME = 'ThreadSafetyScanner_Offline_Setup_x64.exe'
SYSTEM_DLLS = set(('kernel32.dll ntdll.dll user32.dll gdi32.dll advapi32.dll shell32.dll shlwapi.dll '
                  'ws2_32.dll crypt32.dll bcrypt.dll bcryptprimitives.dll secur32.dll userenv.dll '
                  'ole32.dll oleaut32.dll rpcrt4.dll version.dll winmm.dll comdlg32.dll comctl32.dll '
                  'msvcrt.dll ucrtbase.dll mscoree.dll dbghelp.dll setupapi.dll').split())
REQUIRED = [
    'Application/ThreadSafetyScanner.exe', 'Application/ThreadSafetyScanner.exe.config',
    'Tools/ripgrep/rg.exe', 'Tools/cppcheck/cppcheck.exe', 'Tools/cppcheck/cfg/std.cfg',
    'Tools/cppcheck/platforms/mips32.xml', 'Rules/rules.json', 'Runtime/README.txt',
    'Licenses/GPL-3.0.txt', 'Licenses/ripgrep/COPYING', 'Licenses/ripgrep/LICENSE-MIT',
    'Licenses/ripgrep/UNLICENSE', 'Licenses/cppcheck/simplecpp_license.txt',
    'Licenses/cppcheck/tinyxml2_license.txt', 'Licenses/cppcheck/picojson_license.txt',
    'Licenses/cppcheck-2.10-original-source.zip', 'Licenses/cppcheck-2.10-patched-source.zip',
    'Licenses/LLVM-LICENSE.TXT', 'Licenses/mingw/COPYING.MinGW-w64-runtime.txt',
    'Licenses/NSIS-COPYING.txt', 'Licenses/THIRD-PARTY-NOTICES.txt',
    'Licenses/NSIS-3.10-source.tar.bz2', 'Licenses/ripgrep-dependency-licenses.zip',
    'Licenses/ThreadSafetyScanner-source.zip', 'Manifest/dependencies.json',
    'Installer/ThreadSafetyScanner.nsi'
]

def sha256(path):
    h = hashlib.sha256()
    with open(path, 'rb') as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b''): h.update(chunk)
    return h.hexdigest()

def pe_info(path):
    """Inspect PE architecture and normal/delay-load imports without executing it."""
    b = Path(path).read_bytes()
    if len(b) < 256 or b[:2] != b'MZ': raise ValueError(f'Not a PE image: {path}')
    pe = struct.unpack_from('<I', b, 0x3c)[0]
    if b[pe:pe+4] != b'PE\0\0': raise ValueError(f'Invalid PE signature: {path}')
    machine, sections = struct.unpack_from('<HH', b, pe+4)
    opt_size = struct.unpack_from('<H', b, pe+20)[0]
    opt = pe+24
    magic = struct.unpack_from('<H', b, opt)[0]
    directory = opt + (112 if magic == 0x20b else 96 if magic == 0x10b else 0)
    if directory == opt: raise ValueError('Unsupported optional header')
    table = opt + opt_size
    section_map = []
    for i in range(sections):
        vs, va, rawsize, raw = struct.unpack_from('<IIII', b, table+i*40+8)
        section_map.append((va, max(vs, rawsize), raw))
    def offset(rva):
        for va, size, raw in section_map:
            if va <= rva < va+size: return raw+rva-va
        if rva < table: return rva
        raise ValueError(f'Unmapped RVA {rva:x}: {path}')
    def string(rva):
        start = offset(rva); end = b.index(b'\0', start)
        return b[start:end].decode('ascii')
    imports = {}
    for index, size in ((1, 20), (13, 32)):
        rva, length = struct.unpack_from('<II', b, directory+index*8)
        if not rva: continue
        pos = offset(rva)
        while True:
            fields = struct.unpack_from('<'+'I'*(size//4), b, pos)
            if not any(fields): break
            if index == 1: thunk, name = fields[0] or fields[4], fields[3]
            else:
                if not fields[0] & 1: raise ValueError('Unsupported VA-based delay import')
                name, thunk = fields[1], fields[4]
            dll = string(name).lower(); symbols = []
            if thunk:
                t = offset(thunk); width = 8 if magic == 0x20b else 4
                while True:
                    value = int.from_bytes(b[t:t+width], 'little'); t += width
                    if not value: break
                    symbols.append('#'+str(value & 0xffff) if value & (1 << (width*8-1)) else string(value+2))
            imports[dll] = symbols; pos += size
    clr = struct.unpack_from('<II', b, directory+14*8)[0]
    return {'machine': hex(machine), 'managed': bool(clr), 'imports': imports}

def payload_files(stage=STAGE):
    result = {}
    for directory in ('Application', 'Tools', 'Runtime', 'Rules', 'Licenses', 'Manifest'):
        for path in sorted((stage/directory).rglob('*')):
            if path.is_symlink(): raise ValueError(f'Symlinks are not allowed: {path}')
            if not path.is_file() or path.name == '.DS_Store': continue
            rel = path.relative_to(stage).as_posix()
            if rel in ('Manifest/payload.sha256',): continue
            installed = path.relative_to(stage/'Application').as_posix() if directory == 'Application' else rel
            if installed.lower() in {p.lower() for p in result}: raise ValueError(f'Duplicate install path: {installed}')
            result[installed] = path
    return result

def audit(stage=STAGE):
    errors = [f'Missing or empty required file: {rel}' for rel in REQUIRED if not (stage/rel).is_file() or (stage/rel).stat().st_size == 0]
    details = {}
    if errors: return errors, details
    try:
        # NSIS compiles plug-in calls without validating their argument grammar.
        # System::Int64Op requires ARG1 OP ARG2, not ARG1 ARG2 OP.
        binary_ops = {'+', '-', '*', '/', '%', '<<', '>>', '>>>', '|', '&', '^', '||', '&&', '<', '=', '>'}
        for line in (stage/'Installer/ThreadSafetyScanner.nsi').read_text().splitlines():
            tokens = line.split(';', 1)[0].split()
            if tokens and tokens[0] == 'System::Int64Op':
                valid = (len(tokens) == 4 and tokens[2] in binary_ops) or (len(tokens) == 3 and tokens[2] in {'~', '!'})
                if not valid: errors.append('Invalid System::Int64Op argument order: ' + line.strip())
        dependencies = json.loads((stage/'Manifest/dependencies.json').read_text())
        if dependencies['semgrep']['status'] != 'unsupported': errors.append('Semgrep has no verified 1709 redistribution; must be unsupported')
        for name, expected_version in (('ripgrep', '13.0.0'), ('cppcheck', '2.10-tss2')):
            item = dependencies[name]
            if item['version'] != expected_version: errors.append(f'Unreviewed tool version: {name}')
            if sha256(stage/item['path']) != item['sha256']: errors.append(f'Dependency hash mismatch: {name}')
        rules = json.loads((stage/'Rules/rules.json').read_text())
        if not rules or len({r['Function'] for r in rules}) != len(rules): errors.append('Empty or duplicate rules')
        if not any(r.get('Enabled') is True for r in rules): errors.append('No enabled ripgrep rules')
        for r in rules:
            if not re.fullmatch(r'[A-Za-z_][A-Za-z0-9_]*', r['Function']) or r['Priority'] not in ('P1','P2','REVIEW'): errors.append('Invalid rule')
            if not all(isinstance(r.get(key),str) and r[key].strip() for key in ('Category','Reason','Recommendation')) or not isinstance(r.get('Enabled'),bool): errors.append('Incomplete rule schema')
        if 'Version=v4.7.1' not in (stage/'Application/ThreadSafetyScanner.exe.config').read_text(): errors.append('Incorrect .NET target')
        for binary in (stage/'Application/ThreadSafetyScanner.exe', stage/'Tools/ripgrep/rg.exe', stage/'Tools/cppcheck/cppcheck.exe'):
            todo = [binary]; visited = set()
            while todo:
                path = todo.pop()
                if path in visited: continue
                visited.add(path); info = pe_info(path); details[path.relative_to(stage).as_posix()] = info
                if info['machine'] != '0x8664': errors.append(f'Not AMD64: {path}')
                for dll in info['imports']:
                    # These contracts must still be load-tested on the exact Windows image.
                    if dll in SYSTEM_DLLS or dll.startswith('api-ms-win-crt-') or dll.startswith('api-ms-win-core-'): continue
                    local = next((p for p in path.parent.iterdir() if p.name.lower() == dll), None)
                    if local is None: errors.append(f'Unresolved runtime DLL: {path.name} -> {dll}')
                    else: todo.append(local)
        # This recipe deliberately uses a static Cppcheck: no redistributable MSVC DLLs.
        native = details.get('Tools/cppcheck/cppcheck.exe', {}).get('imports', {})
        if any(d.startswith(('msvcp', 'vcruntime', 'libc++', 'libwinpthread')) for d in native): errors.append('Cppcheck is not the audited static build')
    except (KeyError, ValueError, OSError, struct.error) as e: errors.append(str(e))
    return errors, details

def nsi_quote(value):
    return str(value).replace('$', '$$').replace('"', '$\\"').replace('\n', '$\\n').replace('\r', '$\\r')

def make_include(files, dest, output):
    total = sum(p.stat().st_size for p in files.values())
    required = total*2+100*1024*1024
    lines = [f'!define OUTPUT_FILE "{nsi_quote(output)}"', f'!define REQUIRED_BYTES {required}', f'!define REQUIRED_MB {(required+1048575)//1048576}']
    for macro, base in (('ExtractPayload', '$PLUGINSDIR\\payload'), ('CopyPayload', '$INSTDIR')):
        lines.append('!macro '+macro); lines.append('  ClearErrors')
        for rel, src in files.items():
            parent = str(PurePosixPath(rel).parent).replace('/', '\\')
            folder = base if parent == '.' else base+'\\'+nsi_quote(parent)
            if macro == 'ExtractPayload':
                lines += [f'  SetOutPath "{folder}"', f'  File "/oname={nsi_quote(PurePosixPath(rel).name)}" "{nsi_quote(src)}"']
            else:
                source = '$PLUGINSDIR\\payload\\'+nsi_quote(rel.replace('/', '\\'))
                lines += [f'  CreateDirectory "{folder}"', f'  CopyFiles /SILENT "{source}" "{folder}"']
        lines.append('!macroend')
    lines.append('!macro DeletePayload')
    for rel in files: lines.append('  Delete "$INSTDIR\\'+nsi_quote(rel.replace('/', '\\'))+'"')
    parents = {str(p) for rel in files for p in PurePosixPath(rel).parents if str(p) != '.'}
    for directory in sorted(parents, key=lambda p: (p.count('/'),p), reverse=True): lines.append('  RMDir "$INSTDIR\\'+nsi_quote(directory.replace('/', '\\'))+'"')
    lines.append('!macroend'); dest.write_text('\n'.join(lines)+'\n', encoding='utf-8')

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--check-only', action='store_true')
    p.add_argument('--makensis', type=Path)
    p.add_argument('--stage', type=Path, default=STAGE)
    a = p.parse_args(); stage = a.stage.resolve()
    errors, details = audit(stage)
    (ROOT/'build').mkdir(exist_ok=True)
    (ROOT/'build/payload-audit.json').write_text(json.dumps({'passed': not errors, 'errors': errors, 'pe': details, 'windows1709Execution': 'pending'}, indent=2)+'\n')
    if errors: raise SystemExit('OFFLINE BUILD BLOCKED:\n'+'\n'.join(errors))
    if a.check_only: print('Collected payload audit passed. Windows 1709 execution pending.'); return
    if not a.makensis or not a.makensis.is_file(): raise SystemExit('Missing explicit --makensis compiler; no PATH fallback')
    status = {'status': 'candidate-awaiting-windows-1709-validation', 'target': 'Windows 10 Pro 1709 16299.15 x64', 'releasePassed': False}
    (stage/'Manifest/release-status.json').write_text(json.dumps(status, indent=2)+'\n')
    files = payload_files(stage)
    manifest = stage/'Manifest/payload.sha256'
    manifest.write_text(''.join(sha256(path)+'  '+rel+'\n' for rel,path in files.items()), encoding='utf-8')
    files['Manifest/payload.sha256'] = manifest
    output_dir = ROOT/'artifacts/candidate'; output_dir.mkdir(parents=True, exist_ok=True)
    output = output_dir/SETUP_NAME
    include = ROOT/'build/payload.nsh'; make_include(files, include, output)
    env = dict(os.environ); env['NSISDIR'] = str(a.makensis.resolve().parent)
    cmd = [str(a.makensis.resolve()), '-WX', '-INPUTCHARSET', 'UTF8', '-DPAYLOAD_INCLUDE='+str(include), str(stage/'Installer/ThreadSafetyScanner.nsi')]
    if os.name == 'nt': cmd = [cmd[0]]+[x.replace('-', '/', 1) if x.startswith('-') else x for x in cmd[1:]]
    # Remove stale artifacts before attempting a new compile, preventing false success.
    output.unlink(missing_ok=True)
    for name in ('BUILD-REPORT.json','SHA256SUMS.txt','EMBEDDED-PAYLOAD-CHECK.json'):
        (output_dir/name).unlink(missing_ok=True)
    with open(ROOT/'build/installer-build.log', 'w') as log: subprocess.run(cmd, cwd=stage/'Installer', env=env, stdout=log, stderr=subprocess.STDOUT, check=True)
    if not output.is_file() or pe_info(output)['machine'] != '0x14c': raise SystemExit('NSIS output missing or invalid')
    summary = dict(status, file=SETUP_NAME, sizeBytes=output.stat().st_size, sha256=sha256(output), payloadFileCount=len(files), peAudit=details)
    (output_dir/'BUILD-REPORT.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2)+'\n')
    (output_dir/'SHA256SUMS.txt').write_text(sha256(output)+'  '+SETUP_NAME+'\n')
    print(json.dumps(summary, ensure_ascii=False, indent=2))

if __name__ == '__main__': main()
