#!/usr/bin/env python3
"""Mark ONLY an already-tested, exact installer as verified. Does not create a GitHub release."""
import argparse
import json
from pathlib import Path
from build_release import sha256, SETUP_NAME

MANUAL = {'interactiveInstallPathAndShortcuts','guiNormalUserScanThreeModes','ruleEditFilterPreviewHistory','cancelDuringRunningScan','openAllTenFormatsAndChineseContent','preserveSourceReportsConfiguration','noNetworkTrafficObserved'}

def validate(evidence, manual, expected_hash):
    b=evidence.get('baseline',{})
    engine=evidence.get('engineAndExports',{})
    formats={'csv','json','md','html','txt','xml','sarif','xlsx','pdf','zip'}
    required = [
        (str(b.get('build'))=='16299' and b.get('ubr')==15 and b.get('edition')=='Professional','Exact Windows 10 Pro 16299.15 baseline missing'),
        (b.get('architecture')=='x64' and b.get('netRelease')==461308,'Wrong architecture or inbox runtime'),
        (b.get('offlineAdapters') is True and evidence.get('offlineAfterTest') is True,'Offline baseline missing'),
        (b.get('elevatedHarness') is False,'Unprivileged scan evidence missing'),
        (b.get('preinstalledDevelopmentProducts')==[] and b.get('pathTools')==[],'Clean tool baseline missing'),
        (evidence.get('installerSha256')==expected_hash,'Installer SHA256 differs from tested file'),
        (evidence.get('automatedPassed') is True,'Automated target tests incomplete'),
        (str(engine.get('toolVersions',{}).get('ripgrep','')).startswith('ripgrep 13.0.0') and str(engine.get('toolVersions',{}).get('cppcheck','')).startswith('Cppcheck 2.10'),'Bundled tool version execution evidence missing'),
        (engine.get('passed') is True and all(engine.get(mode,{}).get('passed') is True and set(engine.get(mode,{}).get('reports',{}))==formats for mode in ('rg','cppcheck','both')),'Real three-engine/all-format evidence missing'),
        (all(evidence.get(k) is True for k in ('installation','guiWindowStarted','uninstallPreservesUserData','reinstallation')),'Install/GUI/uninstall/reinstall evidence incomplete'),
        (bool(manual.get('operatorName','').strip()) and manual.get('status')=='passed','Named manual test signoff missing'),
        (manual.get('installerSha256')==expected_hash,'Manual signoff is not bound to this installer'),
        (all(manual.get('checks',{}).get(k) is True for k in MANUAL),'Manual report/cancel/UI/network checks incomplete')]
    return [message for ok,message in required if not ok]

def main():
    p=argparse.ArgumentParser();p.add_argument('--candidate',type=Path,required=True);p.add_argument('--evidence',type=Path,required=True);p.add_argument('--manual',type=Path,required=True);a=p.parse_args()
    installer=a.candidate/SETUP_NAME
    errors=validate(json.loads(a.evidence.read_text(encoding='utf-8-sig')),json.loads(a.manual.read_text(encoding='utf-8-sig')),sha256(installer))
    if errors: raise SystemExit('RELEASE PROMOTION BLOCKED:\n'+'\n'.join(errors))
    (a.candidate/'RELEASE-VERIFIED.json').write_text(json.dumps({'status':'verified-on-windows-1709-build-16299.15','sha256':sha256(installer),'evidenceSha256':sha256(a.evidence),'manualSha256':sha256(a.manual)},indent=2)+'\n')
    print('Exact candidate installer verified; original binary and its hash are unchanged.')

if __name__=='__main__':main()
