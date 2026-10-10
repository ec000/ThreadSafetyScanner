#!/usr/bin/env python3
"""Build-host ONLY: collect locked ripgrep crate notices from official crates.io archives."""
from concurrent.futures import ThreadPoolExecutor
import hashlib
import io
import json
from pathlib import Path
import re
import subprocess
import tarfile
import tomllib
import zipfile

ROOT = Path(__file__).resolve().parents[1]

def main():
    with tarfile.open(ROOT/'vendor/downloads/ripgrep-13.0.0-source.tar.gz') as src:
        lock_bytes = src.extractfile('ripgrep-13.0.0/Cargo.lock').read()
        lock = tomllib.loads(lock_bytes.decode())
    packages = [p for p in lock['package'] if p.get('source', '').startswith('registry+')]
    cache = ROOT/'vendor/downloads/rust-crates'; cache.mkdir(parents=True, exist_ok=True)
    def fetch(p):
        name = p['name']+'-'+p['version']; path = cache/(name+'.crate')
        if not path.is_file() or hashlib.sha256(path.read_bytes()).hexdigest() != p['checksum']:
            url = 'https://static.crates.io/crates/'+p['name']+'/'+name+'.crate'
            subprocess.run(['curl', '-fsSL', '--retry', '2', '--connect-timeout', '15', '--max-time', '90', url, '-o', str(path)], check=True)
        if hashlib.sha256(path.read_bytes()).hexdigest() != p['checksum']: raise ValueError('Cargo.lock checksum mismatch: '+name)
        notices = {}
        with tarfile.open(path) as t:
            meta = tomllib.loads(t.extractfile(name+'/Cargo.toml').read().decode())['package']
            for f in t.getmembers():
                if f.isfile() and re.match(r'(?i)^(licen[cs]e|copying|copyright|notice|unlicense|authors)', Path(f.name).name): notices[f.name] = t.extractfile(f).read()
        if not notices and p['name'] in ('winapi-i686-pc-windows-gnu','winapi-x86_64-pc-windows-gnu'):
            # These optional GNU import-library crates explicitly inherit the winapi-rs license.
            parent = next(x for x in packages if x['name']=='winapi')
            parent_path = cache/('winapi-'+parent['version']+'.crate')
            fetch_parent = fetch(parent)  # includes checksum verification; no circular dependency
            notices = {name+'/inherited-winapi-'+parent['version']+'/'+Path(f).name:data for f,data in fetch_parent[1].items()}
        if not notices: raise ValueError('No bundled license notice: '+name+'; manual upstream collection required')
        return {'name':p['name'], 'version':p['version'], 'sha256':p['checksum'], 'license':meta.get('license'), 'authors':meta.get('authors',[]), 'repository':meta.get('repository'), 'licenseFiles':list(notices)}, notices
    inherited = {'winapi-i686-pc-windows-gnu','winapi-x86_64-pc-windows-gnu'}
    with ThreadPoolExecutor(max_workers=6) as pool: results = list(pool.map(fetch, [p for p in packages if p['name'] not in inherited]))
    results += [fetch(p) for p in packages if p['name'] in inherited]
    dest = ROOT/'OfflineRelease/Licenses/ripgrep-dependency-licenses.zip'; dest.parent.mkdir(parents=True, exist_ok=True)
    with zipfile.ZipFile(dest, 'w', zipfile.ZIP_DEFLATED) as z:
        z.writestr('Cargo.lock', lock_bytes)
        z.writestr('packages.json', json.dumps([r[0] for r in results], ensure_ascii=False, indent=2)+'\n')
        z.writestr('README.txt', 'Superset of all registry packages in ripgrep 13.0.0 Cargo.lock, including optional/build/test dependencies. Crate bytes verified against published Cargo.lock checksums. Only notice texts are installed, not a Rust runtime.\n')
        for metadata, notices in results:
            for name, data in notices.items(): z.writestr(name, data)
    print('Collected notices for',len(results),'locked packages:',dest)

if __name__ == '__main__': main()
