"""Execute shared production discovery/policy code on the build host; SMB I/O remains Windows QA."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest
import zipfile

ROOT = Path(__file__).resolve().parents[1]


class ScanInputTests(unittest.TestCase):
    def test_cppcheck_upstream_windows_probes(self):
        compiler = shutil.which('clang++') or shutil.which('g++')
        source_zip = ROOT / 'vendor/downloads/cppcheck-2.10-source.zip'
        if not compiler or not source_zip.is_file():
            self.skipTest('Host compiler or locked upstream Cppcheck source unavailable')
        with tempfile.TemporaryDirectory() as temp:
            directory = Path(temp)
            with zipfile.ZipFile(source_zip) as archive:
                for name in ('simplecpp.cpp', 'simplecpp.h'):
                    entry = next(n for n in archive.namelist() if n.endswith('/externals/simplecpp/' + name))
                    (directory / name).write_bytes(archive.read(entry))
            args = [compiler, '-std=c++11', '-DSIMPLECPP_WINDOWS', '-I', str(ROOT / 'tests/cppcheck_windows_stub'),
                    '-I', str(ROOT / 'vendor/patches'), '-I', str(directory), str(directory / 'simplecpp.cpp'),
                    str(ROOT / 'tests/cppcheck_windows_probe_test.cpp')]
            legacy = directory / 'legacy-probes'
            subprocess.run(args + ['-DTSS_TEST_LEGACY_PROBE', '-o', str(legacy)], check=True)
            failure = subprocess.run([str(legacy)], capture_output=True, text=True)
            self.assertEqual(failure.returncode, 1)
            self.assertIn('network access outside selected source root', failure.stderr)
            fixed = directory / 'fixed-probes'
            subprocess.run(args + ['-o', str(fixed)], check=True)
            subprocess.run([str(fixed)], check=True)

    def test_cppcheck_network_scope(self):
        compiler = shutil.which('clang++') or shutil.which('g++')
        if not compiler:
            self.skipTest('C++ build-host compiler unavailable')
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / 'path-policy'
            subprocess.run([compiler, '-std=c++11', '-Wall', '-Wextra', '-Werror', '-I', str(ROOT / 'vendor/patches'),
                            str(ROOT / 'tests/cppcheck_path_policy_test.cpp'), '-o', str(output)], check=True)
            subprocess.run([str(output)], check=True)

    def test_real_file_discovery(self):
        tool = ROOT / 'build/toolchain'
        dotnet = tool / 'dotnet/dotnet'
        csc = tool / 'roslyn/tasks/netcore/bincore/csc.dll'
        runtimes = sorted((tool / 'dotnet/shared/Microsoft.NETCore.App').glob('*'))
        if not dotnet.is_file() or not csc.is_file() or not runtimes:
            self.skipTest('Private .NET build-host runtime/compiler unavailable')
        runtime = runtimes[-1]
        with tempfile.TemporaryDirectory() as temp:
            output = Path(temp) / 'discovery.dll'
            response = Path(temp) / 'compile.rsp'
            args = ['/nostdlib+', '/langversion:5', '/target:exe', '/warnaserror+', '/out:"' + str(output) + '"']
            args += ['/reference:"' + str(p) + '"' for p in sorted(runtime.glob('*.dll'))]
            args += ['"' + str(ROOT / p) + '"' for p in ('src/ThreadSafetyScanner/FileDiscovery.cs', 'tests/FileDiscoveryTests.cs')]
            response.write_text('\n'.join(args) + '\n')
            subprocess.run([str(dotnet), str(csc), '/noconfig', '@' + str(response)], check=True)
            output.with_suffix('.runtimeconfig.json').write_text(json.dumps({'runtimeOptions': {
                'tfm': 'net8.0', 'framework': {'name': 'Microsoft.NETCore.App', 'version': runtime.name}}}))
            subprocess.run([str(dotnet), str(output)], check=True)


if __name__ == '__main__':
    unittest.main()
