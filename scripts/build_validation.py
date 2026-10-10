#!/usr/bin/env python3
"""Compile the standalone target-machine QA executable, using the same reference set."""
import argparse
from pathlib import Path
import shutil
import subprocess
from build_application import ROOT, REFERENCES

def main():
    p=argparse.ArgumentParser();p.add_argument('--dotnet',type=Path,required=True);p.add_argument('--csc',type=Path,required=True);p.add_argument('--references',type=Path,required=True);a=p.parse_args()
    output=ROOT/'artifacts/validation';output.mkdir(parents=True,exist_ok=True)
    options=['/nostdlib+','/langversion:5','/target:exe','/platform:x64','/optimize+','/deterministic+','/utf8output','/out:"'+str(output/'ThreadSafetyScanner_Validation_x64.exe')+'"']
    options += ['/reference:"'+str(a.references.resolve()/(n+'.dll'))+'"' for n in REFERENCES]
    options += ['"'+str(ROOT/'tests/Windows1709Validation.cs')+'"']
    rsp=ROOT/'build/validation.rsp';rsp.write_text('\n'.join(options)+'\n')
    subprocess.run([str(a.dotnet.resolve()),str(a.csc.resolve()),'/noconfig','@'+str(rsp)],cwd=ROOT,check=True)
    shutil.copy2(ROOT/'src/ThreadSafetyScanner/App.config',output/'ThreadSafetyScanner_Validation_x64.exe.config')
    shutil.copy2(ROOT/'tests/Validate-Windows1709.cmd',output/'Validate-Windows1709.cmd')
    print('Standalone x64 validation harness compiled; target execution pending.')

if __name__=='__main__':main()
