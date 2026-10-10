#!/usr/bin/env python3
"""Cross-compile only against collected .NET Framework 4.7.1 reference assemblies."""
import argparse
from pathlib import Path
import shutil
import subprocess

ROOT = Path(__file__).resolve().parents[1]
REFERENCES = ['mscorlib', 'System', 'System.Core', 'System.Data', 'System.Drawing', 'System.Windows.Forms', 'System.Xml', 'System.Xml.Linq', 'System.Web', 'System.Web.Extensions', 'System.IO.Compression', 'System.IO.Compression.FileSystem']

def main():
    p = argparse.ArgumentParser()
    p.add_argument('--dotnet', type=Path, required=True)
    p.add_argument('--csc', type=Path, required=True)
    p.add_argument('--references', type=Path, required=True)
    a = p.parse_args()
    source = ROOT/'src/ThreadSafetyScanner'
    output = ROOT/'OfflineRelease/Application'
    output.mkdir(parents=True, exist_ok=True)
    refs = [a.references.resolve()/(n+'.dll') for n in REFERENCES]
    missing = [str(f) for f in [a.dotnet, a.csc] + refs if not f.is_file()]
    if missing: raise SystemExit('Missing build inputs:\n'+'\n'.join(missing))
    rsp = ROOT/'build/application.rsp'
    rsp.parent.mkdir(exist_ok=True)
    options = ['/nostdlib+', '/langversion:5', '/target:winexe', '/platform:x64', '/optimize+', '/deterministic+', '/utf8output', '/out:"'+str(output/'ThreadSafetyScanner.exe')+'"', '/win32manifest:"'+str(source/'app.manifest')+'"']
    options += ['/reference:"'+str(f)+'"' for f in refs]
    options += ['"'+str(f)+'"' for f in sorted(source.glob('*.cs'))]
    rsp.write_text('\n'.join(options)+'\n')
    subprocess.run([str(a.dotnet.resolve()), str(a.csc.resolve()), '/noconfig', '@'+str(rsp)], cwd=ROOT, check=True)
    shutil.copy2(source/'App.config', output/'ThreadSafetyScanner.exe.config')
    print('Application compiled for .NET Framework 4.7.1/x64. Windows execution pending.')

if __name__ == '__main__': main()
