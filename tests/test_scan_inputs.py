"""Execute shared production discovery/policy code on the build host; SMB I/O remains Windows QA."""
import json
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]


class ScanInputTests(unittest.TestCase):
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
