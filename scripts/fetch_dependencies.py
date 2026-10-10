#!/usr/bin/env python3
"""Build-host only. Fetch fixed official inputs and require committed SHA256 values."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess

ROOT=Path(__file__).resolve().parents[1]

def main():
    p=argparse.ArgumentParser();p.add_argument('--verify-only',action='store_true');a=p.parse_args()
    failures=[]
    for item in json.loads((ROOT/'vendor/dependencies.lock.json').read_text()):
        path=ROOT/'vendor/downloads'/item['file'];path.parent.mkdir(parents=True,exist_ok=True)
        ok=path.is_file() and hashlib.sha256(path.read_bytes()).hexdigest()==item['sha256']
        if not ok and not a.verify_only:
            temp=path.with_suffix(path.suffix+'.partial')
            subprocess.run(['curl','-fL','--retry','2','--connect-timeout','15','--max-time','300',item['url'],'-o',str(temp)],check=True)
            if hashlib.sha256(temp.read_bytes()).hexdigest()!=item['sha256']: raise SystemExit('SHA256 mismatch; input NOT accepted: '+item['file'])
            temp.replace(path);ok=True
        if not ok: failures.append(item['file'])
    if failures: raise SystemExit('Missing/corrupt build inputs:\n'+'\n'.join(failures))
    print('All locked build input SHA256 values verified. No downloads occur in the installer.')

if __name__=='__main__':main()
