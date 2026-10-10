using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace ThreadSafetyScanner
{
    internal static class ProcessRunner
    {
        // Windows CommandLineToArgvW quoting, including trailing backslashes.
        public static string Quote(string value)
        {
            var b = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                if (c == '"') { b.Append('\\', slashes * 2 + 1); b.Append(c); }
                else { b.Append('\\', slashes); b.Append(c); }
                slashes = 0;
            }
            b.Append('\\', slashes * 2); return b.Append('"').ToString();
        }
        public static int Run(string exe, IEnumerable<string> args, string cwd, Action<string> output, out string error, CancellationToken cancel)
        {
            var start = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote))) {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = new UTF8Encoding(false, true), StandardErrorEncoding = new UTF8Encoding(false, true), WorkingDirectory = cwd
            };
            start.EnvironmentVariables.Remove("RIPGREP_CONFIG_PATH");
            start.EnvironmentVariables["PATH"] = Environment.GetFolderPath(Environment.SpecialFolder.System);
            using (var process = new Process { StartInfo = start })
            {
                cancel.ThrowIfCancellationRequested();
                process.Start();
                var stderr = process.StandardError.ReadToEndAsync();
                var stdout = Task.Run(() => { string line; while ((line = process.StandardOutput.ReadLine()) != null) output(line); });
                var elapsed = Stopwatch.StartNew();
                try
                {
                    while (!process.WaitForExit(100))
                    {
                        cancel.ThrowIfCancellationRequested();
                        if (stdout.IsFaulted) stdout.GetAwaiter().GetResult();
                        if (stderr.IsFaulted) stderr.GetAwaiter().GetResult();
                        if (elapsed.Elapsed.TotalHours >= 1) throw new TimeoutException("扫描超过 60 分钟，请缩小扫描目录。");
                    }
                    stdout.GetAwaiter().GetResult(); error = stderr.GetAwaiter().GetResult();
                    cancel.ThrowIfCancellationRequested(); return process.ExitCode;
                }
                catch
                {
                    try { if (!process.HasExited) process.Kill(); process.WaitForExit(5000); } catch { }
                    // Both engines run single processes; Python addons/clang subprocesses are never enabled.
                    try { Task.WaitAll(new Task[] { stdout, stderr }, 5000); } catch { }
                    throw;
                }
            }
        }
    }
    internal static class Scanner
    {
        private const long MaxBytes = 24L * 1024 * 1024;
        private static readonly HashSet<string> Extensions = new HashSet<string>(new[] { ".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hxx", ".hh" }, StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> Ignored = new HashSet<string>(new[] { ".git", ".svn", ".hg", ".vs", "node_modules", "CMakeFiles" }, StringComparer.OrdinalIgnoreCase);
        public static ScanReport Scan(string source, bool rg, bool cpp, string platform, CancellationToken cancel, Action<string> progress)
        {
            source = Path.GetFullPath(source);
            if (!Directory.Exists(source)) throw new DirectoryNotFoundException("源码目录不存在。");
            if (new Uri(source).IsUnc || new DriveInfo(Path.GetPathRoot(source)).DriveType == DriveType.Network) throw new ArgumentException("离线扫描只接受本地源码目录，不访问网络共享。");
            if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new ArgumentException("请直接选择实际本地源码目录，不使用目录链接。");
            if (!rg && !cpp) throw new ArgumentException("至少选择一个扫描引擎。");
            if (!new[] { "win64", "unix32", "unix64" }.Contains(platform)) throw new ArgumentException("无效的源码目标平台。");
            if (Paths.IsWithin(Paths.User, source)) throw new ArgumentException("源码目录不能包含用户配置/报告目录，以免写入源码树。");
            Integrity.Verify();
            var rules = RuleStore.Load().Where(r => r.Enabled).ToList();
            if (rg && rules.Count == 0) throw new InvalidDataException("ripgrep 至少需要一条启用的规则。");
            var report = new ScanReport { Source = source, StartedUtc = DateTime.UtcNow.ToString("o"), Engines = rg && cpp ? "ripgrep+cppcheck" : rg ? "ripgrep" : "cppcheck", Platform = platform, Status = "running" };
            try
            {
                progress("检查源码目录…");
                var files = Enumerate(source, report, cancel);
                if (files.Count == 0) throw new InvalidDataException("没有可读取的 C/C++ 源码文件。");
                if (rg) { progress("ripgrep 快速函数扫描…"); Ripgrep(source, files, rules, report, cancel); }
                cancel.ThrowIfCancellationRequested();
                if (cpp) { progress("Cppcheck 深度扫描…"); Cppcheck(source, files, platform, report, cancel); }
                report.Findings = report.Findings.GroupBy(f => f.Engine + "\0" + f.File + "\0" + f.Line + "\0" + f.Column + "\0" + f.RuleId).Select(g => g.First()).OrderBy(f => f.Priority == "P1" ? 0 : f.Priority == "P2" ? 1 : 2).ThenBy(f => f.File).ThenBy(f => f.Line).ToList();
                report.Status = report.Diagnostics.Any(d => d.StartsWith("INCOMPLETE:")) ? "partial" : "completed";
            }
            catch (OperationCanceledException) { report.Status = "cancelled"; report.Diagnostics.Add("用户取消：结果不完整。"); }
            catch (Exception ex) { report.Status = "failed"; report.Diagnostics.Add(ex.ToString()); }
            report.FinishedUtc = DateTime.UtcNow.ToString("o");
            string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N");
            Json.Save(Path.Combine(Paths.History, id + ".json"), report);
            File.WriteAllText(Path.Combine(Paths.Logs, id + ".log"), report.Status + Environment.NewLine + report.Source + Environment.NewLine + string.Join(Environment.NewLine, report.Diagnostics), Encoding.UTF8);
            return report;
        }
        private static List<string> Enumerate(string source, ScanReport report, CancellationToken cancel)
        {
            var files = new List<string>(); var todo = new Stack<string>(); todo.Push(source);
            while (todo.Count > 0)
            {
                cancel.ThrowIfCancellationRequested(); string dir = todo.Pop();
                try
                {
                    foreach (string child in Directory.EnumerateFileSystemEntries(dir))
                    {
                        cancel.ThrowIfCancellationRequested();
                        var attrs = File.GetAttributes(child);
                        if ((attrs & FileAttributes.ReparsePoint) != 0) { report.Diagnostics.Add("INCOMPLETE: 跳过符号链接/重解析点 " + child); continue; }
                        if ((attrs & FileAttributes.Directory) != 0) { if (!Ignored.Contains(Path.GetFileName(child))) todo.Push(child); }
                        else if (Extensions.Contains(Path.GetExtension(child)))
                        {
                            if (new FileInfo(child).Length > MaxBytes) report.Diagnostics.Add("INCOMPLETE: 跳过超过 24 MiB 的文件 " + child);
                            else files.Add(child);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { report.Diagnostics.Add("INCOMPLETE: 无法读取目录 " + dir + ": " + ex.Message); }
            }
            return files;
        }
        private static void Ripgrep(string source, List<string> files, List<Rule> rules, ScanReport report, CancellationToken cancel)
        {
            string pattern = @"\b(?:" + string.Join("|", rules.Select(r => Regex.Escape(r.Function))) + @")\s*\(";
            var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var args = new List<string> { "--json", "--no-config", "--no-ignore", "--hidden", "--multiline", "--color=never", "--max-filesize=24M", "--glob=*.{c,h,cc,cpp,cxx,hpp,hh,hxx}" };
            args.AddRange(Ignored.Select(d => "--glob=!**/" + d + "/**"));
            args.Add("--"); args.Add(pattern); args.Add(source);
            string stderr;
            int exit = ProcessRunner.Run(Paths.Tool("ripgrep"), args, Path.GetDirectoryName(Paths.Tool("ripgrep")), line => {
                var obj = Json.Obj(Json.Serializer().DeserializeObject(line));
                if (Convert.ToString(obj["type"]) == "match") candidates.Add(Path.GetFullPath(Json.Text(Json.Obj(obj["data"])["path"])));
            }, out stderr, cancel);
            if (exit != 0 && exit != 1) throw new InvalidOperationException("ripgrep 失败，返回码 " + exit + ": " + stderr);
            if (!string.IsNullOrWhiteSpace(stderr)) report.Diagnostics.Add("INCOMPLETE: ripgrep: " + stderr);
            var eligible = new HashSet<string>(files, StringComparer.OrdinalIgnoreCase);
            var map = rules.ToDictionary(r => r.Function);
            var calls = new Regex(@"\b(?<fn>" + string.Join("|", map.Keys.Select(Regex.Escape)) + @")\s*\(", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));
            foreach (string file in candidates)
            {
                cancel.ThrowIfCancellationRequested();
                if (!eligible.Contains(file) || !Paths.IsWithin(file, source)) continue;
                try
                {
                    string code = ReadCode(file); string masked = Mask(code);
                    int lineNumber = 1, position = 0, lineStart = 0;
                    foreach (Match m in calls.Matches(masked))
                    {
                        cancel.ThrowIfCancellationRequested();
                        while (position < m.Index) { if (code[position] == '\n') { lineNumber++; lineStart = position + 1; } position++; }
                        int end = code.IndexOf('\n', m.Index); if (end < 0) end = code.Length;
                        string excerpt = code.Substring(lineStart, end - lineStart).Trim();
                        if (Regex.IsMatch(excerpt, @"^\s*#\s*define\b")) continue;
                        var rule = map[m.Groups["fn"].Value];
                        report.Findings.Add(new Finding { Engine = "ripgrep", Priority = rule.Priority, Category = rule.Category, RuleId = "thread-unsafe-" + rule.Function, Function = rule.Function,
                            File = Relative(source, file), Line = lineNumber, Column = m.Index - lineStart + 1, Reason = rule.Reason, Recommendation = rule.Recommendation, Excerpt = excerpt.Length > 500 ? excerpt.Substring(0, 500) : excerpt });
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { report.Diagnostics.Add("INCOMPLETE: " + file + ": " + ex.Message); }
            }
            report.Diagnostics.Add("ripgrep 返回码=" + exit + "；UTF-8/带 BOM 源码；函数原型、宏间接调用、函数指针需人工复核。");
        }
        internal static string ReadCode(string file)
        {
            using (var r = new StreamReader(file, new UTF8Encoding(false, true), true)) return r.ReadToEnd();
        }
        internal static string Mask(string code)
        {
            char[] chars = code.ToCharArray(); int i = 0;
            while (i < code.Length)
            {
                int end = i;
                if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '/') { end = code.IndexOf('\n', i + 2); if (end < 0) end = code.Length; }
                else if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '*') { int close = code.IndexOf("*/", i + 2, StringComparison.Ordinal); end = close < 0 ? code.Length : close + 2; }
                else if (i + 1 < code.Length && code[i] == 'R' && code[i + 1] == '"')
                {
                    int open = code.IndexOf('(', i + 2);
                    if (open >= 0 && open - i <= 18)
                    {
                        string terminator = ")" + code.Substring(i + 2, open - i - 2) + "\"";
                        int close = code.IndexOf(terminator, open + 1, StringComparison.Ordinal);
                        end = close < 0 ? code.Length : close + terminator.Length;
                    }
                }
                if (end == i && (code[i] == '"' || code[i] == '\''))
                {
                    char quote = code[i]; end = i + 1;
                    while (end < code.Length) { if (code[end] == '\\') { end = Math.Min(code.Length, end + 2); continue; } if (code[end++] == quote) break; }
                }
                if (end > i) { for (; i < end; i++) if (chars[i] != '\r' && chars[i] != '\n') chars[i] = ' '; }
                else i++;
            }
            return new string(chars);
        }
        private static void Cppcheck(string source, List<string> files, string platform, ScanReport report, CancellationToken cancel)
        {
            string list = Path.Combine(Paths.User, "cppcheck-files-" + Guid.NewGuid().ToString("N") + ".txt");
            try
            {
                File.WriteAllLines(list, files, new UTF8Encoding(false));
                string stderr;
                var stdout = new StringBuilder();
                int exit = ProcessRunner.Run(Paths.Tool("cppcheck"), new[] { "--quiet", "--xml", "--xml-version=2", "--enable=warning,style,performance,portability,information", "--inconclusive", "--max-configs=12", "--error-exitcode=10", "--platform=" + platform, "--library=" + Path.Combine(Paths.Root, "Tools", "cppcheck", "cfg", "std.cfg"), "--file-list=" + list },
                    Path.GetDirectoryName(Paths.Tool("cppcheck")), line => stdout.AppendLine(line), out stderr, cancel);
                if (exit != 0 && exit != 10) throw new InvalidOperationException("Cppcheck 失败，返回码 " + exit + ": " + stderr + stdout);
                XDocument xml;
                using (var reader = XmlReader.Create(new StringReader(stderr), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) xml = XDocument.Load(reader);
                if (xml.Root == null || xml.Root.Name != "results" || xml.Root.Element("errors") == null) throw new InvalidDataException("Cppcheck 输出不是完整 XML 结果。");
                foreach (var err in xml.Descendants("error"))
                {
                    cancel.ThrowIfCancellationRequested();
                    string id = (string)err.Attribute("id") ?? "unknown", severity = (string)err.Attribute("severity") ?? "information";
                    var loc = err.Element("location");
                    string message = (string)err.Attribute("verbose") ?? (string)err.Attribute("msg") ?? id;
                    if (id == "syntaxError" || id == "internalError" || id == "noValidConfiguration") report.Diagnostics.Add("INCOMPLETE: " + id + ": " + message);
                    if (loc == null || severity == "information") { report.Diagnostics.Add("Cppcheck " + id + ": " + message); continue; }
                    string file = (string)loc.Attribute("file") ?? "";
                    if (!Path.IsPathRooted(file)) file = Path.Combine(source, file);
                    int line = Math.Max(1, (int?)loc.Attribute("line") ?? 1), column = Math.Max(1, (int?)loc.Attribute("column") ?? 1);
                    string excerpt = "";
                    try { if (Paths.IsWithin(file, source)) excerpt = File.ReadLines(file).Skip(line - 1).FirstOrDefault() ?? ""; } catch { }
                    report.Findings.Add(new Finding { Engine = "cppcheck", Priority = severity == "error" ? "P1" : severity == "warning" ? "P2" : "REVIEW", Category = severity, RuleId = id, Function = "", File = Relative(source, file), Line = line, Column = column, Reason = message, Recommendation = "结合编译配置、线程上下文和源码人工复核。", Excerpt = excerpt.Length > 500 ? excerpt.Substring(0, 500) : excerpt });
                }
                report.Diagnostics.Add("Cppcheck 返回码=" + exit + "；平台=" + platform + "；未启用需要 Python 的 addons。");
            }
            finally { if (File.Exists(list)) File.Delete(list); }
        }
        private static string Relative(string root, string file)
        {
            file = Path.GetFullPath(file); string prefix = root.TrimEnd('\\', '/') + Path.DirectorySeparatorChar;
            return file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? file.Substring(prefix.Length) : file;
        }
    }
}
