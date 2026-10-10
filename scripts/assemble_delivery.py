#!/usr/bin/env python3
"""Assemble an inspectable offline delivery, retaining candidate status."""
import csv
import json
from pathlib import Path
import shutil
import zipfile
from build_release import ROOT, STAGE, SETUP_NAME, sha256, payload_files

def main():
    dest=ROOT/'artifacts/Deliverables';dest.mkdir(parents=True,exist_ok=True)
    candidate=ROOT/'artifacts/candidate'
    embedded=json.loads((candidate/'EMBEDDED-PAYLOAD-CHECK.json').read_text())
    if embedded['installerSha256']!=sha256(candidate/SETUP_NAME) or embedded['inventoryMatches'] is not True: raise SystemExit('Compiled installer payload verification missing/stale')
    for source,name in [(candidate/SETUP_NAME,SETUP_NAME),(candidate/'BUILD-REPORT.json','BUILD-REPORT.json'),(candidate/'EMBEDDED-PAYLOAD-CHECK.json','EMBEDDED-PAYLOAD-CHECK.json'),(STAGE/'Licenses/ThreadSafetyScanner-source.zip','ThreadSafetyScanner_Source.zip'),(STAGE/'Manifest/dependencies.json','OfflineDependencies.json'),(STAGE/'Manifest/payload.sha256','PAYLOAD-SHA256SUMS.txt')]:shutil.copy2(source,dest/name)
    for name in ('ThreadSafetyScanner_Validation_x64.exe','ThreadSafetyScanner_Validation_x64.exe.config','Validate-Windows1709.cmd'):shutil.copy2(ROOT/'artifacts/validation'/name,dest/name)
    for path in (ROOT/'docs').glob('*'):
        if path.is_file():shutil.copy2(path,dest/path.name)
    with zipfile.ZipFile(dest/'BuildScripts.zip','w',zipfile.ZIP_DEFLATED) as z:
        for directory in ('scripts','vendor/patches','OfflineRelease/Installer'):
            for path in sorted((ROOT/directory).rglob('*')):
                if path.is_file() and '__pycache__' not in path.parts:z.write(path,path.relative_to(ROOT).as_posix())
        z.write(ROOT/'vendor/dependencies.lock.json','vendor/dependencies.lock.json')
    with zipfile.ZipFile(dest/'ThirdPartyLicensesAndSources.zip','w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted((STAGE/'Licenses').rglob('*')):
            if path.is_file():z.write(path,path.relative_to(STAGE).as_posix())
    with (dest/'PayloadInventory.csv').open('w',encoding='utf-8-sig',newline='') as f:
        w=csv.writer(f);w.writerow(['InstalledPath','SizeBytes','SHA256'])
        files=payload_files();files['Manifest/payload.sha256']=STAGE/'Manifest/payload.sha256'
        for rel,path in sorted(files.items()):w.writerow([rel,path.stat().st_size,sha256(path)])
    (dest/'SHA256SUMS.txt').write_text(''.join(sha256(p)+'  '+p.name+'\n' for p in sorted(dest.iterdir()) if p.is_file() and p.name!='SHA256SUMS.txt'))
    archive=ROOT/'artifacts/ThreadSafetyScanner_Offline_Delivery_x64.zip'
    with zipfile.ZipFile(archive,'w',zipfile.ZIP_DEFLATED) as z:
        for path in sorted(dest.iterdir()):
            if path.is_file():z.write(path,path.name)
    (archive.parent/'DELIVERY-ZIP-SHA256.txt').write_text(sha256(archive)+'  '+archive.name+'\n')
    print(json.dumps({'setup':str(dest/SETUP_NAME),'setupSizeBytes':(dest/SETUP_NAME).stat().st_size,'setupSha256':sha256(dest/SETUP_NAME),'deliveryZip':str(archive),'zipSizeBytes':archive.stat().st_size,'status':'待 Windows 10 1709 Build 16299.15 实机验证'},ensure_ascii=False,indent=2))

if __name__=='__main__':main()
