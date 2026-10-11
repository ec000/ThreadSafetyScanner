"""Publication regression checks: hash gate and draft-before-upload ordering."""
import hashlib
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch

sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
import publish_candidate as pub

class PublicationTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory();self.root=Path(self.temp.name)
        self.dest=self.root/'artifacts/Deliverables';self.dest.mkdir(parents=True);(self.root/'build').mkdir()
        setup=self.dest/'ThreadSafetyScanner_Offline_Setup_x64.exe';setup.write_bytes(b'simulated installer for publication unit test')
        digest=hashlib.sha256(setup.read_bytes()).hexdigest()
        (self.dest/'EMBEDDED-PAYLOAD-CHECK.json').write_text(json.dumps({'installerSha256':digest,'inventoryMatches':True}))
        (self.dest/'BUILD-REPORT.json').write_text(json.dumps({'sha256':digest}))
        (self.dest/'SHA256SUMS.txt').write_text(''.join(hashlib.sha256(p.read_bytes()).hexdigest()+'  '+p.name+'\n' for p in self.dest.iterdir()))
        z=self.root/'artifacts/ThreadSafetyScanner_Offline_Delivery_x64.zip';z.write_bytes(b'simulated delivery')
        (self.root/'artifacts/DELIVERY-ZIP-SHA256.txt').write_text(hashlib.sha256(z.read_bytes()).hexdigest()+'  '+z.name+'\n')
        self.env={'GITHUB_REF_NAME':'v0.1.0-test','GITHUB_SHA':'abc123'}
    def tearDown(self):self.temp.cleanup()
    def test_upload_before_publication_and_always_prerelease(self):
        with patch.object(pub,'ROOT',self.root),patch.dict(pub.os.environ,self.env),patch.object(pub.subprocess,'run') as run:
            pub.main()
            args=[c.args[0] for c in run.call_args_list]
            self.assertEqual(['create','upload','edit'],[a[2] for a in args])
            self.assertIn('--draft',args[0]);self.assertIn('--draft=false',args[2]);self.assertIn('--prerelease',args[2]);self.assertNotIn('--clobber',args[1])
    def test_corrupt_delivery_cannot_create_release(self):
        (self.dest/'ThreadSafetyScanner_Offline_Setup_x64.exe').write_bytes(b'corrupt')
        with patch.object(pub,'ROOT',self.root),patch.dict(pub.os.environ,self.env),patch.object(pub.subprocess,'run') as run:
            with self.assertRaisesRegex(SystemExit,'hash mismatch'):pub.main()
            run.assert_not_called()
    def test_branch_name_cannot_publish(self):
        with patch.dict(pub.os.environ,{'GITHUB_REF_NAME':'main'}),patch.object(pub.subprocess,'run') as run:
            with self.assertRaises(SystemExit):pub.main()
            run.assert_not_called()

if __name__=='__main__':unittest.main()
