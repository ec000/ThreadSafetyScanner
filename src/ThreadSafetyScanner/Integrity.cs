using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace ThreadSafetyScanner
{
    internal static class Integrity
    {
        public static Dictionary<string, string> VerifyTools()
        {
            var versions = new Dictionary<string, string>();
            foreach (string name in new[] { "ripgrep", "cppcheck" })
            {
                var text = new StringBuilder(); string error;
                using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                {
                    int exit = ProcessRunner.Run(Paths.Tool(name), new[] { "--version" }, Path.GetDirectoryName(Paths.Tool(name)), line => text.AppendLine(line), out error, timeout.Token);
                    string expected = name == "ripgrep" ? "ripgrep 13.0.0" : "Cppcheck 2.10";
                    if (exit != 0 || !text.ToString().StartsWith(expected, StringComparison.Ordinal)) throw new InvalidOperationException(name + " 无法启动或版本不符；返回码=" + exit + "；" + error);
                }
                versions[name] = text.ToString().Trim();
            }
            return versions;
        }
        public static void CheckSystem()
        {
            if (!Environment.Is64BitOperatingSystem || !Environment.Is64BitProcess) throw new PlatformNotSupportedException("本程序要求 Windows x64。");
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
            {
                int build;
                if (key == null || !int.TryParse(Convert.ToString(key.GetValue("CurrentBuildNumber")), out build) || build < 16299)
                    throw new PlatformNotSupportedException("最低系统要求：Windows 10 1709 Build 16299.15 x64。");
                if (build == 16299 && Convert.ToInt32(key.GetValue("UBR", 0)) < 15) throw new PlatformNotSupportedException("Windows 16299 的最低修订号为 15。");
            }
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                if (key == null || Convert.ToInt32(key.GetValue("Release", 0)) < 461308)
                    throw new PlatformNotSupportedException("检测不到 .NET Framework 4.7.1。请修复 Windows 自带的 .NET 组件后重试。");
        }
        public static int Verify()
        {
            string manifest = Path.Combine(Paths.Root, "Manifest", "payload.sha256");
            if (!File.Exists(manifest)) throw new FileNotFoundException("安装文件校验清单缺失，请重新安装。", manifest);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string line in File.ReadAllLines(manifest))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (line.Length < 67 || line.Substring(64, 2) != "  " || line.Take(64).Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("SHA256 清单格式错误。");
                string relative = line.Substring(66).Replace('/', Path.DirectorySeparatorChar);
                string file = Path.GetFullPath(Path.Combine(Paths.Root, relative));
                if (Path.IsPathRooted(relative) || !Paths.IsWithin(file, Paths.Root) || !seen.Add(relative)) throw new InvalidDataException("校验清单路径无效：" + relative);
                if (!File.Exists(file)) throw new FileNotFoundException("必需文件缺失：" + relative, file);
                using (var hash = SHA256.Create()) using (var stream = File.OpenRead(file))
                {
                    string actual = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                    if (!actual.Equals(line.Substring(0, 64), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("安装文件损坏：" + relative + "。请重新安装。");
                }
            }
            foreach (string name in new[] { "ThreadSafetyScanner.exe", "ThreadSafetyScanner.exe.config", "Tools/ripgrep/rg.exe", "Tools/cppcheck/cppcheck.exe", "Tools/cppcheck/cfg/std.cfg", "Rules/rules.json", "Manifest/dependencies.json", "Licenses/THIRD-PARTY-NOTICES.txt" })
                if (!seen.Contains(name.Replace('/', Path.DirectorySeparatorChar))) throw new InvalidDataException("校验清单未覆盖必需文件：" + name);
            return seen.Count;
        }
    }
}
