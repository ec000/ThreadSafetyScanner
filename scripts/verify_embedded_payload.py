#!/usr/bin/env python3
"""Extract the actual compiled installer and verify every embedded payload hash."""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
from build_release import ROOT, sha256

def main():
    p=argparse.ArgumentParser();p.add_argument('--sevenzip',type=Path,required=True);p.add_argument('--installer',type=Path,required=True);a=p.parse_args()
    installer=a.installer.resolve()
    with tempfile.TemporaryDirectory(prefix='tss-installer-',dir=ROOT/'build') as temp:
        subprocess.run([str(a.sevenzip.resolve()),'x','-y','-o'+temp,str(installer)],stdout=subprocess.PIPE,stderr=subprocess.STDOUT,check=True)
        payload=Path(temp)/'$PLUGINSDIR/payload';manifest=payload/'Manifest/payload.sha256'
        if not manifest.is_file():raise SystemExit('Compiled installer has no embedded payload manifest')
        expected={'Manifest/payload.sha256'};count=0
        for line in manifest.read_text().splitlines():
            digest,rel=line.split('  ',1);path=(payload/rel).resolve()
            if not path.is_relative_to(payload.resolve()):raise ValueError('Unsafe manifest path')
            if sha256(path)!=digest:raise ValueError('Embedded payload SHA256 mismatch: '+rel)
            expected.add(rel);count+=1
        actual={p.relative_to(payload).as_posix() for p in payload.rglob('*') if p.is_file()}
        if expected!=actual:raise ValueError('Embedded inventory differs: '+str(expected^actual))
        report={'installerSha256':sha256(installer),'verifiedEmbeddedFiles':count,'inventoryMatches':True,'windows1709Execution':'pending'}
        (installer.parent/'EMBEDDED-PAYLOAD-CHECK.json').write_text(json.dumps(report,indent=2)+'\n')
        print(json.dumps(report,indent=2))

if __name__=='__main__':main()
