using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using System.Reflection;

[assembly: AssemblyTitle("ThreadSafetyScanner")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: System.Runtime.Versioning.TargetFramework(".NETFramework,Version=v4.7.1", FrameworkDisplayName = ".NET Framework 4.7.1")]

namespace ThreadSafetyScanner
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                Integrity.CheckSystem();
                int verified = Integrity.Verify();
                Integrity.VerifyTools();
                // Installer verification must not initialize the administrator's profile.
                if (args.Length > 0 && args[0] == "--verify-payload") return 0;
                Paths.Initialize();
                if (args.Length > 0) return CommandLine(args);
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm()); return 0;
            }
            catch (Exception ex)
            {
                string log = "";
                try { Directory.CreateDirectory(Paths.Logs); log = Path.Combine(Paths.Logs, "error-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".log"); File.WriteAllText(log, ex.ToString(), Encoding.UTF8); } catch { }
                if (args.Length == 0) MessageBox.Show(ex.Message + "\n\n日志：" + log, "ThreadSafetyScanner 启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }
        private static string Value(string[] args, string key, string fallback)
        {
            int index = Array.IndexOf(args, key); if (index < 0) return fallback;
            if (index + 1 >= args.Length) throw new ArgumentException(key + " 缺少参数。"); return args[index + 1];
        }
        private static int CommandLine(string[] args)
        {
            string output = Value(args, "--output", Path.Combine(Paths.Reports, DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")));
            if (args[0] == "--validate-reports") { Directory.CreateDirectory(output); Json.Save(Path.Combine(output, "validation.json"), ReportViewer.ValidateAll(Value(args, "--directory", output))); return 0; }
            if (args[0] == "--self-test") return SelfTest(output);
            if (args[0] == "--scan")
            {
                if (args.Length < 2) throw new ArgumentException("--scan 需要源码目录。");
                string engines = Value(args, "--engines", "both"), platform = Value(args, "--platform", "win64");
                if (!new[] { "rg", "cppcheck", "both" }.Contains(engines)) throw new ArgumentException("--engines 仅支持 rg/cppcheck/both。");
                var r = Scanner.Scan(args[1], engines != "cppcheck", engines != "rg", platform, CancellationToken.None, s => { });
                Exporter.All(r, output); Json.Save(Path.Combine(output, "validation.json"), ReportViewer.ValidateAll(output));
                return r.Status == "completed" ? 0 : 2;
            }
            throw new ArgumentException("支持 --verify-payload、--scan、--self-test、--validate-reports。");
        }
        private static int SelfTest(string output)
        {
            string source = Path.Combine(Paths.User, "SelfTest", "中文 空格 源码 " + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(source);
            File.WriteAllText(Path.Combine(source, "中文 测试.cpp"), "#include <string.h>\nint risky(char* input) {\n // strtok(fake);\n const char* literal = \"rand(fake)\";\n const char* raw = R\"tag(localtime(fake))tag\";\n char* p = strtok(input, \",\");\n int* nullPointer = 0;\n return *nullPointer + (p != 0) + (literal != 0) + (raw != 0);\n}\n", new UTF8Encoding(false));
            var evidence = new Dictionary<string, object>(); bool passed = true;
            evidence["toolVersions"] = Integrity.VerifyTools();
            foreach (string mode in new[] { "rg", "cppcheck", "both" })
            {
                var r = Scanner.Scan(source, mode != "cppcheck", mode != "rg", "win64", CancellationToken.None, s => { });
                bool rgPass = mode == "cppcheck" || (r.Findings.Count(f => f.Engine == "ripgrep" && f.Function == "strtok") == 1 && !r.Findings.Any(f => f.Engine == "ripgrep" && (f.Function == "rand" || f.Function == "localtime")));
                bool cppPass = mode == "rg" || r.Findings.Any(f => f.Engine == "cppcheck" && f.RuleId == "nullPointer");
                string dest = Path.Combine(output, mode); Exporter.All(r, dest); var reports = ReportViewer.ValidateAll(dest);
                bool ok = r.Status == "completed" && rgPass && cppPass; passed &= ok;
                evidence[mode] = new { passed = ok, status = r.Status, rgExpectedCalls = rgPass, cppExpectedDefect = cppPass, findings = r.Findings.Count, reports = reports, diagnostics = r.Diagnostics };
            }
            var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            var cancelled = Scanner.Scan(source, true, true, "win64", cancellation.Token, s => { });
            passed &= cancelled.Status == "cancelled";
            evidence["cancelBeforeStart"] = cancelled.Status;
            evidence["passed"] = passed;
            evidence["testType"] = "automated-engine-and-export-structure; GUI/visual/uninstall/network-evidence still required";
            evidence["utc"] = DateTime.UtcNow.ToString("o");
            Directory.CreateDirectory(output); Json.Save(Path.Combine(output, "self-test.json"), evidence);
            return passed ? 0 : 2;
        }
    }
}
