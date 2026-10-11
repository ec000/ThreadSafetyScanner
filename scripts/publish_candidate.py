#!/usr/bin/env python3
"""Publish only a fully audited candidate; upload while draft, then expose attachments."""
import hashlib
import json
import os
from pathlib import Path
import re
import subprocess

ROOT = Path(__file__).resolve().parents[1]

def main():
    tag = os.environ.get('GITHUB_REF_NAME', '')
    if not re.fullmatch(r'v[0-9][A-Za-z0-9._-]*', tag):
        raise SystemExit('Expected a version tag; refusing publication')
    dest = ROOT/'artifacts/Deliverables'
    for line in (dest/'SHA256SUMS.txt').read_text().splitlines():
        digest, name = line.split('  ', 1)
        if Path(name).name != name or hashlib.sha256((dest/name).read_bytes()).hexdigest() != digest:
            raise SystemExit('Delivery hash mismatch: '+name)
    setup = dest/'ThreadSafetyScanner_Offline_Setup_x64.exe'
    digest = hashlib.sha256(setup.read_bytes()).hexdigest()
    embedded = json.loads((dest/'EMBEDDED-PAYLOAD-CHECK.json').read_text())
    report = json.loads((dest/'BUILD-REPORT.json').read_text())
    if embedded['installerSha256'] != digest or embedded['inventoryMatches'] is not True or report['sha256'] != digest:
        raise SystemExit('Compiled payload evidence missing/stale')
    archive = ROOT/'artifacts/ThreadSafetyScanner_Offline_Delivery_x64.zip'
    expected, name = (ROOT/'artifacts/DELIVERY-ZIP-SHA256.txt').read_text().strip().split('  ', 1)
    if name != archive.name or hashlib.sha256(archive.read_bytes()).hexdigest() != expected:
        raise SystemExit('Delivery ZIP hash mismatch')
    notes = ROOT/'build/automatic-release-notes.md'
    notes.write_text(f'''完整离线安装候选包，待 Windows 10 Pro 1709 Build 16299.15 x64 实机验证，非最终验收成功版本。

直接下载 ThreadSafetyScanner_Offline_Setup_x64.exe；无需 Python、Office、额外 PATH 或手动部署扫描工具。
全部交付 ZIP 包含源码、构建脚本、离线依赖清单、第三方许可证及对应源码、中文说明、测试报告、Windows 验证程序。

EXE：{setup.stat().st_size} 字节；SHA256：`{digest}`。
源码提交：{os.environ['GITHUB_SHA']}。

已通过编译、构建门禁测试和实际安装器解包完整性验证。Windows 1709 安装、GUI、真实扫描、所有报告导出、卸载重装仍待目标环境验收。CI 构建成功不代表目标系统兼容性通过。
''')
    def gh(*args): subprocess.run(['gh', *map(str,args)], cwd=ROOT, check=True)
    # Never replace files on an existing public release. A failed upload stays draft.
    gh('release','create',tag,'--target',os.environ['GITHUB_SHA'],'--draft','--prerelease','--latest=false','--title',tag+' 完整离线候选（待实机验证）','--notes-file',notes)
    gh('release','upload',tag,setup,archive,dest/'SHA256SUMS.txt',ROOT/'artifacts/DELIVERY-ZIP-SHA256.txt')
    gh('release','edit',tag,'--draft=false','--prerelease','--latest=false')

if __name__ == '__main__': main()
