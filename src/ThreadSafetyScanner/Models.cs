using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

namespace ThreadSafetyScanner
{
    public sealed class Rule
    {
        public string Function { get; set; }
        public string Priority { get; set; }
        public string Category { get; set; }
        public string Reason { get; set; }
        public string Recommendation { get; set; }
        public bool Enabled { get; set; }
    }
    public sealed class Finding
    {
        public string Engine { get; set; }
        public string Priority { get; set; }
        public string Category { get; set; }
        public string RuleId { get; set; }
        public string Function { get; set; }
        public string File { get; set; }
        public int Line { get; set; }
        public int Column { get; set; }
        public string Reason { get; set; }
        public string Recommendation { get; set; }
        public string Excerpt { get; set; }
    }
    public sealed class ScanReport
    {
        public string Source { get; set; }
        public string StartedUtc { get; set; }
        public string FinishedUtc { get; set; }
        public string Engines { get; set; }
        public string Platform { get; set; }
        public string Status { get; set; }
        public string Scope { get; set; }
        public List<Finding> Findings { get; set; }
        public List<string> Diagnostics { get; set; }
        public ScanReport()
        {
            Findings = new List<Finding>(); Diagnostics = new List<string>();
            Scope = "候选静态检查，不证明实际数据竞争；检查所有预处理分支。Cppcheck 未使用工程编译参数时分析精度有限。";
        }
    }
    internal static class Paths
    {
        public static readonly string Root = GetRoot();
        public static readonly string User = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ThreadSafetyScanner");
        public static readonly string History = Path.Combine(User, "History");
        public static readonly string Reports = Path.Combine(User, "Reports");
        public static readonly string Logs = Path.Combine(User, "Logs");
        public static string Rules { get { return Path.Combine(User, "rules.json"); } }
        private static string GetRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
            return Path.GetFileName(dir).Equals("Application", StringComparison.OrdinalIgnoreCase) ? Directory.GetParent(dir).FullName : dir;
        }
        public static void Initialize()
        {
            foreach (string d in new[] { User, History, Reports, Logs }) Directory.CreateDirectory(d);
            if (!File.Exists(Rules)) File.Copy(Path.Combine(Root, "Rules", "rules.json"), Rules, false);
        }
        public static string Tool(string name) { return Path.Combine(Root, "Tools", name, name == "ripgrep" ? "rg.exe" : "cppcheck.exe"); }
        public static bool IsWithin(string path, string root)
        {
            string p = Path.GetFullPath(path).TrimEnd('\\', '/');
            string r = Path.GetFullPath(root).TrimEnd('\\', '/');
            return p.Equals(r, StringComparison.OrdinalIgnoreCase) || p.StartsWith(r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }
    }
    internal static class Json
    {
        public static JavaScriptSerializer Serializer() { return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 128 }; }
        public static string Write(object value) { return Serializer().Serialize(value); }
        public static T Read<T>(string value) { return Serializer().Deserialize<T>(value); }
        public static void Save(string path, object value) { File.WriteAllText(path, Write(value), new UTF8Encoding(false)); }
        public static Dictionary<string, object> Obj(object value) { return (Dictionary<string, object>)value; }
        public static string Text(object value)
        {
            var obj = Obj(value);
            if (obj.ContainsKey("text")) return Convert.ToString(obj["text"]);
            if (obj.ContainsKey("bytes")) return Encoding.UTF8.GetString(Convert.FromBase64String(Convert.ToString(obj["bytes"])));
            throw new InvalidDataException("ripgrep JSON 缺少 text/bytes。");
        }
    }
    internal static class RuleStore
    {
        public static List<Rule> Load() { return Validate(Json.Read<List<Rule>>(File.ReadAllText(Paths.Rules, Encoding.UTF8))); }
        public static List<Rule> Validate(List<Rule> rules)
        {
            if (rules == null || rules.Count == 0 || rules.Count > 5000) throw new InvalidDataException("规则数量必须为 1–5000。");
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var r in rules)
            {
                if (r == null || !Regex.IsMatch(r.Function ?? "", @"^[A-Za-z_][A-Za-z0-9_]*$")) throw new InvalidDataException("规则函数名无效。");
                if (!names.Add(r.Function)) throw new InvalidDataException("重复函数规则：" + r.Function);
                if (!new[] { "P1", "P2", "REVIEW" }.Contains(r.Priority)) throw new InvalidDataException("优先级仅允许 P1/P2/REVIEW。");
                if (string.IsNullOrWhiteSpace(r.Reason) || string.IsNullOrWhiteSpace(r.Category) || string.IsNullOrWhiteSpace(r.Recommendation)) throw new InvalidDataException("规则原因、分类和建议不能为空。");
            }
            return rules;
        }
        public static void Save(List<Rule> rules)
        {
            Validate(rules); string temp = Paths.Rules + ".tmp";
            Json.Save(temp, rules);
            if (File.Exists(Paths.Rules)) File.Replace(temp, Paths.Rules, Paths.Rules + ".bak"); else File.Move(temp, Paths.Rules);
        }
    }
}
