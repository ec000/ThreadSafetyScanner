using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Web;
using System.Xml;
using System.Xml.Linq;

namespace ThreadSafetyScanner
{
    internal static class Exporter
    {
        public static readonly string[] Formats = { "csv", "json", "md", "html", "txt", "xml", "sarif", "xlsx", "pdf", "zip" };
        private static readonly string[] Columns = { "Engine", "Priority", "Category", "RuleId", "Function", "File", "Line", "Column", "Reason", "Recommendation", "Excerpt" };
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);
        private static string[] Values(Finding f) { return new[] { f.Engine, f.Priority, f.Category, f.RuleId, f.Function, f.File, f.Line.ToString(CultureInfo.InvariantCulture), f.Column.ToString(CultureInfo.InvariantCulture), f.Reason, f.Recommendation, f.Excerpt }.Select(v => v ?? "").ToArray(); }
        private static string Html(string value) { return HttpUtility.HtmlEncode(value ?? ""); }
        private static string Csv(string value) { if (value.Length > 0 && "=+-@\t\r\n".IndexOf(value[0]) >= 0) value = "'" + value; return "\"" + value.Replace("\"", "\"\"") + "\""; }
        private static string Md(string value) { return (value ?? "").Replace("\\", "\\\\").Replace("|", "\\|").Replace("`", "\\`").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\r", " ").Replace("\n", " "); }
        private static IEnumerable<string[]> Rows(ScanReport r) { yield return Columns; foreach (var f in r.Findings) yield return Values(f); }
        private static string Overview(ScanReport r)
        {
            return "ThreadSafetyScanner 离线扫描报告\n状态: " + r.Status + "\n源码: " + r.Source + "\n引擎: " + r.Engines + "\n平台: " + r.Platform + "\n开始 UTC: " + r.StartedUtc + "\n结束 UTC: " + r.FinishedUtc + "\n候选数: " + r.Findings.Count + "\n范围: " + r.Scope + "\n诊断:\n" + string.Join("\n", r.Diagnostics) + "\n";
        }
        public static void All(ScanReport report, string directory)
        {
            if (Paths.IsWithin(directory, report.Source)) throw new ArgumentException("报告必须保存在源码目录外。");
            Directory.CreateDirectory(directory);
            foreach (string ext in Formats) Save(report, Path.Combine(directory, "findings." + ext), ext);
        }
        public static void Save(ScanReport report, string path, string format)
        {
            if (Paths.IsWithin(path, report.Source)) throw new ArgumentException("报告必须保存在源码目录外。");
            if (!Formats.Contains(format)) throw new ArgumentException("不支持的格式。");
            string directory = Path.GetDirectoryName(Path.GetFullPath(path)); Directory.CreateDirectory(directory);
            string temp = Path.Combine(directory, ".tss-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                Write(report, temp, format);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
            }
            finally { if (File.Exists(temp)) File.Delete(temp); }
        }
        private static void Write(ScanReport r, string path, string format)
        {
            switch (format)
            {
                case "json": Json.Save(path, r); break;
                case "csv":
                    using (var w = new StreamWriter(path, false, new UTF8Encoding(true)))
                    {
                        w.WriteLine(string.Join(",", new[] { "RecordType", "ScanStatus", "ScanSource", "ScanEngines", "ScanDiagnostics" }.Concat(Columns).Select(Csv)));
                        w.WriteLine(string.Join(",", new[] { "Metadata", r.Status, r.Source, r.Engines, string.Join("\n", r.Diagnostics) }.Concat(Columns.Select(c => "")).Select(Csv)));
                        foreach (var f in r.Findings) w.WriteLine(string.Join(",", new[] { "Finding", r.Status, r.Source, r.Engines, "" }.Concat(Values(f)).Select(Csv)));
                    }
                    break;
                case "txt": File.WriteAllLines(path, TextLines(r), new UTF8Encoding(true)); break;
                case "md":
                    using (var w = new StreamWriter(path, false, Utf8))
                    {
                        w.WriteLine("# 离线扫描报告\n\n```text\n" + Overview(r).Replace("```", "` ` `") + "```\n");
                        w.WriteLine("|" + string.Join("|", Columns) + "|\n|" + string.Join("|", Columns.Select(c => "---")) + "|");
                        foreach (var f in r.Findings) w.WriteLine("|" + string.Join("|", Values(f).Select(Md)) + "|");
                    } break;
                case "html":
                    using (var w = new StreamWriter(path, false, Utf8))
                    {
                        w.WriteLine("<!doctype html><html lang=\"zh-CN\"><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width\"><meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'\"><title>离线扫描报告</title><style>body{font:14px sans-serif;margin:24px}pre,td{white-space:pre-wrap;overflow-wrap:anywhere}table{border-collapse:collapse;width:100%}td,th{padding:8px;border:1px solid #ccc;text-align:left}</style><h1>离线扫描报告</h1><pre>" + Html(Overview(r)) + "</pre><table><thead><tr>");
                        foreach (string col in Columns) w.Write("<th>" + Html(col) + "</th>");
                        w.Write("</tr></thead><tbody>");
                        foreach (var f in r.Findings) { w.Write("<tr>"); foreach (string cell in Values(f)) w.Write("<td>" + Html(cell) + "</td>"); w.Write("</tr>"); }
                        w.Write("</tbody></table></html>");
                    } break;
                case "xml":
                    new XDocument(new XDeclaration("1.0", "utf-8", null), new XElement("ThreadSafetyScanner", new XAttribute("status", r.Status), new XElement("Overview", CleanXml(Overview(r))), new XElement("Findings", r.Findings.Select(f => new XElement("Finding", Columns.Zip(Values(f), (c, v) => new XElement(c, CleanXml(v)))))))).Save(path); break;
                case "sarif": Sarif(r, path); break;
                case "xlsx": Xlsx(r, path); break;
                case "pdf": Pdf(r, path); break;
                case "zip":
                    using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
                    {
                        var overview = zip.CreateEntry("README.txt"); using (var w = new StreamWriter(overview.Open(), Utf8)) w.Write(Overview(r));
                        foreach (string ext in Formats.Where(e => e != "zip"))
                        {
                            string temp = path + "." + ext;
                            try { Write(r, temp, ext); zip.CreateEntryFromFile(temp, "findings." + ext, CompressionLevel.Optimal); }
                            finally { if (File.Exists(temp)) File.Delete(temp); }
                        }
                    } break;
            }
        }
        private static IEnumerable<string> TextLines(ScanReport r)
        {
            foreach (string s in Overview(r).Split('\n')) yield return s;
            foreach (var f in r.Findings)
            {
                yield return "";
                yield return "[" + f.Priority + "] " + f.Engine + " / " + f.Category + " / " + f.RuleId;
                yield return f.File + ":" + f.Line + ":" + f.Column + "  " + f.Function;
                yield return "原因: " + f.Reason;
                yield return "建议: " + f.Recommendation;
                yield return "源码: " + f.Excerpt;
            }
        }
        private static void Sarif(ScanReport r, string path)
        {
            var engines = r.Engines.Split('+');
            var runs = engines.Select(engine => {
                var findings = r.Findings.Where(f => f.Engine == engine).ToList();
                var rules = findings.Select(f => f.RuleId).Distinct().ToList();
                return new {
                    tool = new { driver = new { name = engine, rules = rules.Select(id => new { id = id, shortDescription = new { text = id } }).ToArray() } },
                    invocations = new[] { new { executionSuccessful = r.Status == "completed", properties = new { status = r.Status, source = r.Source, diagnostics = r.Diagnostics } } },
                    results = findings.Select(f => new { ruleId = f.RuleId, ruleIndex = rules.IndexOf(f.RuleId), level = f.Priority == "P1" ? "error" : f.Priority == "P2" ? "warning" : "note", message = new { text = f.Reason + "\n" + f.Recommendation },
                        locations = new[] { new { physicalLocation = new { artifactLocation = new { uri = new Uri(Path.GetFullPath(Path.Combine(r.Source, f.File))).AbsoluteUri }, region = new { startLine = f.Line, startColumn = f.Column } } } }, properties = new { category = f.Category, candidateOnly = true } }).ToArray()
                };
            }).ToArray();
            Json.Save(path, new Dictionary<string, object> { { "$schema", "https://json.schemastore.org/sarif-2.1.0.json" }, { "version", "2.1.0" }, { "runs", runs } });
        }
        private static string CleanXml(string value) { return new string((value ?? "").Where(c => XmlConvert.IsXmlChar(c)).ToArray()); }
        private static void Entry(ZipArchive zip, string path, string content)
        {
            using (var w = new StreamWriter(zip.CreateEntry(path, CompressionLevel.Optimal).Open(), Utf8)) w.Write(content);
        }
        private static void Xlsx(ScanReport r, string path)
        {
            if (r.Findings.Count > 1048575) throw new InvalidOperationException("XLSX 单表最多支持 1048575 条结果，请使用 CSV/JSON。");
            using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Entry(zip, "[Content_Types].xml", "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
                Entry(zip, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
                Entry(zip, "xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Findings\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"Summary\" sheetId=\"2\" r:id=\"rId2\"/></sheets></workbook>");
                Entry(zip, "xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
                Sheet(zip, "xl/worksheets/sheet1.xml", Rows(r));
                Sheet(zip, "xl/worksheets/sheet2.xml", Overview(r).Split('\n').Select(s => new[] { s }));
            }
        }
        private static void Sheet(ZipArchive zip, string name, IEnumerable<string[]> rows)
        {
            using (var x = XmlWriter.Create(zip.CreateEntry(name).Open(), new XmlWriterSettings { Encoding = Utf8, CloseOutput = true }))
            {
                const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                x.WriteStartDocument(); x.WriteStartElement("worksheet", ns); x.WriteStartElement("sheetData", ns);
                foreach (var row in rows)
                {
                    x.WriteStartElement("row", ns);
                    foreach (string cell in row)
                    {
                        if (cell != null && cell.Length > 32767) throw new InvalidOperationException("XLSX 单元格超过 32767 字符限制，请使用 JSON/CSV 保存完整内容。");
                        // inlineStr never evaluates source text as an Excel formula.
                        x.WriteStartElement("c", ns); x.WriteAttributeString("t", "inlineStr"); x.WriteStartElement("is", ns);
                        x.WriteStartElement("t", ns); x.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve");
                        x.WriteString(CleanXml(cell)); x.WriteEndElement(); x.WriteEndElement(); x.WriteEndElement();
                    }
                    x.WriteEndElement();
                }
                x.WriteEndElement(); x.WriteEndElement(); x.WriteEndDocument();
            }
        }
        // Raster PDF: local GDI+ renders system CJK fonts; no PDF runtime or online service.
        // All rows and diagnostics are paginated; text is an image, not searchable PDF text.
        private static void Pdf(ScanReport r, string path)
        {
            using (var stream = File.Create(path))
            using (var bitmap = new Bitmap(1200, 1700))
            using (var graphics = Graphics.FromImage(bitmap))
            using (var font = new Font("Microsoft YaHei", 18, FontStyle.Regular, GraphicsUnit.Pixel))
            {
                var offsets = new Dictionary<int, long>(); var pages = new List<int>(); int next = 3, y = 70;
                Action<string> raw = s => { byte[] b = Encoding.ASCII.GetBytes(s); stream.Write(b, 0, b.Length); };
                Action<int, string> obj = (id, s) => { offsets[id] = stream.Position; raw(id + " 0 obj\n" + s + "\nendobj\n"); };
                raw("%PDF-1.4\n"); graphics.Clear(Color.White); graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                Action flush = () => {
                    graphics.DrawString("ThreadSafetyScanner | " + (pages.Count + 1) + " | " + r.Status, font, Brushes.Gray, 60, 1630);
                    int image = next++, content = next++, page = next++; pages.Add(page);
                    using (var jpeg = new MemoryStream())
                    {
                        bitmap.Save(jpeg, ImageFormat.Jpeg); byte[] bytes = jpeg.ToArray();
                        offsets[image] = stream.Position;
                        raw(image + " 0 obj\n<< /Type /XObject /Subtype /Image /Width 1200 /Height 1700 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length " + bytes.Length + " >>\nstream\n");
                        stream.Write(bytes, 0, bytes.Length); raw("\nendstream\nendobj\n");
                    }
                    string draw = "q 595 0 0 842 0 0 cm /Im0 Do Q\n";
                    obj(content, "<< /Length " + draw.Length + " >>\nstream\n" + draw + "endstream");
                    obj(page, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /XObject << /Im0 " + image + " 0 R >> >> /Contents " + content + " 0 R >>");
                    graphics.Clear(Color.White); y = 70;
                };
                foreach (string logical in TextLines(r).SelectMany(s => s.Replace("\r", "").Split('\n')))
                {
                    string remaining = logical.Length == 0 ? " " : logical;
                    while (remaining.Length > 0)
                    {
                        int low = 1, high = remaining.Length;
                        while (low < high) { int middle = (low + high + 1) / 2; if (graphics.MeasureString(remaining.Substring(0, middle), font).Width <= 1080) low = middle; else high = middle - 1; }
                        if (low < remaining.Length && char.IsHighSurrogate(remaining[low - 1])) low--;
                        if (low <= 0) low = Math.Min(2, remaining.Length);
                        if (y > 1570) flush();
                        graphics.DrawString(remaining.Substring(0, low), font, Brushes.Black, 60, y); y += 27;
                        remaining = remaining.Substring(low);
                    }
                }
                flush();
                obj(1, "<< /Type /Catalog /Pages 2 0 R >>");
                obj(2, "<< /Type /Pages /Count " + pages.Count + " /Kids [" + string.Join(" ", pages.Select(p => p + " 0 R")) + "] >>");
                long xref = stream.Position; raw("xref\n0 " + next + "\n0000000000 65535 f \n");
                for (int id = 1; id < next; id++) raw(offsets[id].ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
                raw("trailer\n<< /Size " + next + " /Root 1 0 R >>\nstartxref\n" + xref + "\n%%EOF\n");
            }
        }
    }
}
