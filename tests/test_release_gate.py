"""Exercise release blockers using real collected binaries and controlled corruptions."""
import copy
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT/'scripts'))
import build_release as gate
from promote_release import validate, MANUAL

class PayloadGateTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.stage=Path(self.temp.name)/'OfflineRelease'
        shutil.copytree(ROOT/'OfflineRelease',self.stage)
    def tearDown(self):self.temp.cleanup()
    def test_actual_payload_is_complete(self):
        errors,details=gate.audit(self.stage);self.assertEqual([],errors);self.assertEqual('0x8664',details['Tools/cppcheck/cppcheck.exe']['machine'])
    def test_missing_cppcheck_blocks(self):
        (self.stage/'Tools/cppcheck/cppcheck.exe').unlink();self.assertTrue(any('cppcheck.exe' in e for e in gate.audit(self.stage)[0]))
    def test_missing_cppcheck_data_blocks(self):
        (self.stage/'Tools/cppcheck/cfg/std.cfg').unlink();self.assertTrue(any('std.cfg' in e for e in gate.audit(self.stage)[0]))
    def test_missing_license_blocks(self):
        (self.stage/'Licenses/LLVM-LICENSE.TXT').unlink();self.assertTrue(any('LLVM-LICENSE' in e for e in gate.audit(self.stage)[0]))
    def test_no_enabled_rules_blocks(self):
        p=self.stage/'Rules/rules.json';rules=json.loads(p.read_text())
        for r in rules:r['Enabled']=False
        p.write_text(json.dumps(rules));self.assertTrue(any('enabled' in e for e in gate.audit(self.stage)[0]))
    def test_tool_corruption_blocks(self):
        p=self.stage/'Tools/ripgrep/rg.exe';b=bytearray(p.read_bytes());b[-32]^=1;p.write_bytes(b)
        self.assertTrue(any('hash mismatch' in e for e in gate.audit(self.stage)[0]))
    def test_wrong_architecture_blocks_even_if_hash_is_updated(self):
        p=self.stage/'Tools/ripgrep/rg.exe';b=bytearray(p.read_bytes());offset=struct.unpack_from('<I',b,0x3c)[0];struct.pack_into('<H',b,offset+4,0xaa64);p.write_bytes(b)
        m=self.stage/'Manifest/dependencies.json';data=json.loads(m.read_text());data['ripgrep']['sha256']=gate.sha256(p);m.write_text(json.dumps(data))
        self.assertTrue(any('Not AMD64' in e for e in gate.audit(self.stage)[0]))
    def test_symlink_cannot_enter_payload(self):
        (self.stage/'Application/external.txt').symlink_to('/etc/hosts')
        with self.assertRaisesRegex(ValueError,'Symlinks'):gate.payload_files(self.stage)
    def test_no_recursive_uninstall(self):
        files=gate.payload_files(self.stage);inc=Path(self.temp.name)/'payload.nsh';gate.make_include(files,inc,Path(self.temp.name)/gate.SETUP_NAME)
        text=inc.read_text();self.assertNotIn('RMDir /r',text);self.assertNotIn('Delete "$INSTDIR\\*',text)

class PromotionTests(unittest.TestCase):
    def setUp(self):
        self.e={'baseline':{'build':'16299','ubr':15,'edition':'Professional','architecture':'x64','netRelease':461308,'offlineAdapters':True,'elevatedHarness':False,'preinstalledDevelopmentProducts':[],'pathTools':[]},'offlineAfterTest':True,'installerSha256':'expected','automatedPassed':True,'installation':True,'guiWindowStarted':True,'uninstallPreservesUserData':True,'reinstallation':True}
        self.e['engineAndExports']={'passed':True,**{m:{'passed':True,'reports':{e:'structural-pass' for e in ('csv','json','md','html','txt','xml','sarif','xlsx','pdf','zip')}} for m in ('rg','cppcheck','both')}}
        self.e['engineAndExports']['toolVersions']={'ripgrep':'ripgrep 13.0.0','cppcheck':'Cppcheck 2.10'}
        self.m={'operatorName':'test operator','status':'passed','installerSha256':'expected','checks':{k:True for k in MANUAL}}
    def test_complete_evidence_required(self):self.assertEqual([],validate(self.e,self.m,'expected'))
    def test_newer_windows_is_not_equivalent(self):
        self.e['baseline']['build']='19045';self.assertTrue(validate(self.e,self.m,'expected'))
    def test_pending_visual_checks_block_release(self):
        self.m['checks']['openAllTenFormatsAndChineseContent']=False;self.assertTrue(validate(self.e,self.m,'expected'))
    def test_different_binary_blocks_release(self):self.assertTrue(validate(self.e,self.m,'different'))
    def test_online_environment_blocks_release(self):
        self.e['offlineAfterTest']=False;self.assertTrue(validate(self.e,self.m,'expected'))
    def test_clean_baseline_cannot_be_omitted(self):
        del self.e['baseline']['pathTools'];self.assertTrue(validate(self.e,self.m,'expected'))
    def test_real_engine_report_evidence_cannot_be_omitted(self):
        del self.e['engineAndExports']['cppcheck']['reports']['pdf'];self.assertTrue(validate(self.e,self.m,'expected'))
    def test_tool_version_execution_evidence_required(self):
        del self.e['engineAndExports']['toolVersions'];self.assertTrue(validate(self.e,self.m,'expected'))
    def test_manual_signature_is_bound_to_binary(self):
        self.m['installerSha256']='another';self.assertTrue(validate(self.e,self.m,'expected'))

if __name__=='__main__':unittest.main()
