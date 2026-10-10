#!/usr/bin/env python3
"""Fast, read-only first-pass audit of risky C/C++ libc/lwIP calls.

Scans an existing path (including a mounted CIFS/SMB share) using ripgrep --json.
Findings are *candidates*, not confirmed concurrency defects.
"""
import argparse
import collections
import csv
import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys

# Severity = triage priority for embedded/network devices, not a proven vulnerability.
RULES = {
    'strtok': ('P1', 'Internal parsing cursor shared across calls', 'strtok_r; verify buffer ownership'),
    'inet_ntoa': ('P2', 'Library-managed result lifetime; portability caveat', 'inet_ntop'),
    'ipaddr_ntoa': ('P1', 'lwIP shared static output buffer', 'ipaddr_ntoa_r'),
    'ip4addr_ntoa': ('P1', 'lwIP shared static output buffer', 'ip4addr_ntoa_r'),
    'ip6addr_ntoa': ('P1', 'lwIP shared static output buffer', 'ip6addr_ntoa_r'),
    'localtime': ('P2', 'Internal tm result and timezone interactions', 'localtime_r'),
    'gmtime': ('P2', 'Internal tm result', 'gmtime_r'),
    'asctime': ('P1', 'Library-managed static output', 'strftime'),
    'ctime': ('P1', 'Library-managed static output', 'localtime_r + strftime'),
    'strerror': ('P2', 'MT-safety depends on libc/version', 'strerror_r (check GNU/POSIX variant)'),
    'gethostbyname': ('P1', 'Legacy resolver; static result storage', 'getaddrinfo'),
    'gethostbyname2': ('P1', 'Legacy resolver; static result storage', 'getaddrinfo'),
    'gethostbyaddr': ('P1', 'Legacy resolver; static result storage', 'getnameinfo'),
    'getpwnam': ('P1', 'Internal passwd result may be overwritten', 'getpwnam_r'),
    'getpwuid': ('P1', 'Internal passwd result may be overwritten', 'getpwuid_r'),
    'readdir': ('P2', 'Same DIR* requires synchronization; different DIR* generally OK', 'Independent DIR* or lock and copy'),
    'rand': ('REVIEW', 'Predictable PRNG; global state in some libc', 'CSPRNG for secrets; per-thread PRNG otherwise'),
    'srand': ('REVIEW', 'Resets PRNG state; global behavior in some libc', 'Initialize PRNG appropriately; CSPRNG for secrets'),
    'getenv': ('P2', 'Result lifetime unsafe when env changes concurrently', 'Cache values before starting threads'),
    'setenv': ('P2', 'May race with concurrent environment reads/writes', 'Initialize environment before threads'),
    'putenv': ('P2', 'May race; supplied storage must remain valid', 'Initialize environment before threads'),
    'unsetenv': ('P2', 'May race with concurrent environment reads', 'Initialize environment before threads'),
    'setlocale': ('P2', 'Changes process-wide locale state', 'Set before threads; use uselocale if supported'),
    'getopt': ('P2', 'Shared parsing cursor/optind state', 'Parse arguments in startup phase'),
    'getopt_long': ('P2', 'Shared parsing cursor/optind state', 'Parse arguments in startup phase'),
    'getopt_long_only': ('P2', 'Shared parsing cursor/optind state', 'Parse arguments in startup phase'),
    'system': ('REVIEW', 'Shell execution, blocking and child-process effects', 'posix_spawn or explicit exec + argv'),
    'exit': ('REVIEW', 'Terminates process rather than individual thread', 'return / pthread_exit for worker thread'),
}
SORT = {'P1': 0, 'P2': 1, 'REVIEW': 2}
PATTERN = r'\b(?:' + '|'.join(sorted(map(re.escape, RULES), key=len, reverse=True)) + r')\s*\('
FUNCTION_RE = re.compile(r'\b(' + '|'.join(sorted(map(re.escape, RULES), key=len, reverse=True)) + r')\s*\(')


def main():
    parser = argparse.ArgumentParser(description='Read-only first-pass thread-safety scan of C/C++ (supports CIFS mounted paths)')
    parser.add_argument('source', help='Source directory, e.g. /mnt/sdk_source')
    parser.add_argument('--out', required=True, help='LOCAL report directory, e.g. ~/sdk_scan_results')
    parser.add_argument('--exclude', action='append', default=[], metavar='GLOB', help='Extra exclusion glob, repeatable; e.g. --exclude "**/third_party/**"')
    args = parser.parse_args()
    src = Path(args.source).expanduser().resolve()
    out = Path(args.out).expanduser().resolve()
    if not src.is_dir():
        parser.error('source directory does not exist: ' + str(src))
    if shutil.which('rg') is None:
        parser.error('ripgrep executable "rg" is missing; install ripgrep first')
    # Refuse report output nested below share/source, to avoid accidental writes into source.
    if out == src or src in out.parents:
        parser.error('--out must NOT be inside the scanned source (use a LOCAL Linux directory)')
    out.mkdir(parents=True, exist_ok=True)
    args_rg = ['rg', '--json', '--no-ignore', '--hidden', '--color', 'never',
               '--glob', '*.{c,h,cc,cpp,cxx,hpp,hh,hxx}',
               '--glob', '!**/.git/**', '--glob', '!**/.svn/**',
               '--glob', '!**/node_modules/**']
    for glob in args.exclude:
        args_rg.extend(['--glob', ('!' + glob.lstrip('!'))])
    args_rg.extend(['--', PATTERN, str(src)])
    findings = []
    stderr_path = out / 'rg_stderr.log'
    with stderr_path.open('w', encoding='utf-8') as err_file:
        proc = subprocess.Popen(args_rg, stdout=subprocess.PIPE, stderr=err_file)
        assert proc.stdout is not None
        for raw in proc.stdout:
            try:
                obj = json.loads(raw)
            except (json.JSONDecodeError, UnicodeDecodeError):
                continue
            if obj.get('type') != 'match':
                continue
            data = obj['data']
            source_file = data.get('path', {}).get('text', '')
            if not source_file:
                continue
            textline = data.get('lines', {}).get('text', '').strip()
            for sm in data.get('submatches', []):
                match_text = sm.get('match', {}).get('text', '')
                m = FUNCTION_RE.search(match_text)
                if not m:
                    continue
                fn = m.group(1)
                severity, reason, replacement = RULES[fn]
                findings.append({
                    'severity': severity,
                    'function': fn,
                    'file': os.path.relpath(source_file, str(src)),
                    'line': data.get('line_number', 0),
                    'reason': reason,
                    'recommendation': replacement,
                    'source_excerpt': textline[:500],
                })
        ret = proc.wait()
    if ret not in (0, 1):
        print(f'WARNING: rg exited with code {ret}; inspect {stderr_path}, results may be incomplete.', file=sys.stderr)
    findings.sort(key=lambda f: (SORT[f['severity']], f['file'], f['line'], f['function']))
    cols = ['severity', 'function', 'file', 'line', 'reason', 'recommendation', 'source_excerpt']
    with (out / 'findings.csv').open('w', encoding='utf-8-sig', newline='') as file:
        writer = csv.DictWriter(file, fieldnames=cols)
        writer.writeheader()
        writer.writerows(findings)
    with (out / 'findings.json').open('w', encoding='utf-8') as file:
        json.dump(findings, file, ensure_ascii=False, indent=2)
    fn_counts = collections.Counter(f['function'] for f in findings)
    priority_counts = collections.Counter(f['severity'] for f in findings)
    with (out / 'summary.md').open('w', encoding='utf-8') as file:
        file.write('# C/C++ 函数级线程安全审查（候选结果）\n\n')
        file.write(f'- 源码根目录：`{src}`\n')
        file.write(f'- 命中总量：**{len(findings)}**；P1：{priority_counts["P1"]}；P2：{priority_counts["P2"]}；需上下文复核：{priority_counts["REVIEW"]}\n')
        file.write(f'- 扫描返回码：`{ret}`，详见 `rg_stderr.log`\n\n')
        file.write('> 注意：本报告是文本搜索产生的待审查清单，不代表已确认的数据竞争；注释、字符串、函数定义可能产生误报。还可能漏掉宏包装或函数指针调用。\n\n')
        file.write('## 按函数汇总\n\n| 函数 | 候选次数 | 优先级 | 建议替代 |\n|---|---:|---|---|\n')
        for fn, cnt in fn_counts.most_common():
            sev, _, repl = RULES[fn]
            file.write(f'| `{fn}` | {cnt} | {sev} | {repl.replace("|", "/")} |\n')
        file.write('\n## 优先审查的前 300 处调用\n\n| 优先级 | 文件:行号 | 函数 | 源码片段 |\n|---|---|---|---|\n')
        for f in findings[:300]:
            snippet = f['source_excerpt'].replace('|', '\\|').replace('`','\\`').replace('\n',' ')
            file.write(f"| {f['severity']} | `{f['file']}:{f['line']}` | `{f['function']}` | `{snippet}` |\n")
        if len(findings) > 300:
            file.write(f'\n其余 {len(findings) - 300} 处参见 `findings.csv` 或 `findings.json`。\n')
        file.write('\n## 工程复核注意\n\n')
        file.write('- `inet_ntoa`、`strerror`、`rand` 的 MT-safety 与 libc 实现有关；勿直接断言为线程竞争。\n')
        file.write('- `readdir` 在不同 `DIR*` 流上的并发与同一流上的并发不同。\n')
        file.write('- `system`、`exit` 主要关注语义、生命周期、副作用及线程调用上下文。\n')
        file.write('- lwIP `ipaddr_ntoa` 等共享返回缓冲区，不应在多次调用后继续依赖之前的指针内容。\n')
        file.write('- 对所有 P1 结果检查调用线程、返回值生命周期及缓冲区是否用于安全关键决策。\n')
    print(f'Source: {src}\nCandidate matches: {len(findings)} (P1={priority_counts["P1"]}, P2={priority_counts["P2"]}, REVIEW={priority_counts["REVIEW"]})')
    print(f'Reports: {out / "summary.md"}, {out / "findings.csv"}, {out / "findings.json"}')
    return 0 if ret in (0, 1) else 2


if __name__ == '__main__':
    sys.exit(main())
