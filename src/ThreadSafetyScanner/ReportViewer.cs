using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;

namespace ThreadSafetyScanner
{
    internal static class ReportViewer
    {
        private static XDocument Xml(Stream stream)
        {
            using (var x = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null })) return XDocument.Load(x);
        }
        public static void Open(IWin32Window owner, string path)
        {
            string ext = Path.GetExtension(path).TrimStart('.').ToLowerInvariant();
            if (!Exporter.Formats.Contains(ext)) throw new InvalidDataException("仅支持本应用导出的报告格式。");
            var form = new Form { Icon = AppIcon.Value, Text = "离线报告查看 — " + Path.GetFileName(path), Width = 1100, Height = 760, StartPosition = FormStartPosition.CenterParent };
            if (ext == "xlsx")
            {
                using (var zip = ZipFile.OpenRead(path))
                {
                    var tabs = new TabControl { Dock = DockStyle.Fill };
                    foreach (string sheet in new[] { "sheet1.xml", "sheet2.xml" })
                    {
                        var entry = zip.GetEntry("xl/worksheets/" + sheet); if (entry == null) throw new InvalidDataException("XLSX 缺少工作表。");
                        XDocument doc; using (var s = entry.Open()) doc = Xml(s);
                        XNamespace ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                        var rows = doc.Descendants(ns + "row").Select(r => r.Elements(ns + "c").Select(c => string.Concat(c.Descendants(ns + "t").Select(t => t.Value))).ToArray()).ToList();
                        var table = new DataTable(); int width = rows.Count == 0 ? 0 : rows.Max(r => r.Length);
                        for (int c = 0; c < width; c++) table.Columns.Add("列 " + (c + 1));
                        foreach (var row in rows) table.Rows.Add(row.Cast<object>().ToArray());
                        var tab = new TabPage(sheet == "sheet1.xml" ? "结果" : "摘要"); tab.Controls.Add(new DataGridView { Dock = DockStyle.Fill, DataSource = table, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells }); tabs.TabPages.Add(tab);
                    }
                    form.Controls.Add(tabs);
                }
            }
            else if (ext == "zip")
            {
                var list = new ListBox { Dock = DockStyle.Fill };
                using (var zip = ZipFile.OpenRead(path)) foreach (var entry in zip.Entries) list.Items.Add(entry.FullName);
                list.DoubleClick += delegate {
                    if (list.SelectedItem == null) return;
                    try
                    {
                        using (var zip = ZipFile.OpenRead(path))
                        {
                            var entry = zip.GetEntry(Convert.ToString(list.SelectedItem));
                            string directory = Path.Combine(Paths.User, "Preview", Guid.NewGuid().ToString("N")); Directory.CreateDirectory(directory);
                            string target = Path.Combine(directory, Path.GetFileName(entry.FullName));
                            entry.ExtractToFile(target); Open(form, target);
                        }
                    }
                    catch (Exception e) { MessageBox.Show(form, e.Message, "报告打开失败"); }
                };
                form.Controls.Add(list); form.Text += " — 双击查看包内文件";
            }
            else if (ext == "pdf")
            {
                // Read this app's JPEG image PDF without requiring an external PDF reader.
                byte[] bytes = File.ReadAllBytes(path); string binary = Encoding.GetEncoding(28591).GetString(bytes);
                var images = Regex.Matches(binary, @"\d+ 0 obj\n<< /Type /XObject /Subtype /Image [^\n]+ /Length (?<size>\d+) >>\nstream\n").Cast<Match>().ToList();
                if (images.Count == 0) throw new InvalidDataException("仅支持本应用导出的图像 PDF；其他 PDF 请使用系统阅读器。");
                var pages = new ComboBox { Dock = DockStyle.Top, DropDownStyle = ComboBoxStyle.DropDownList };
                var image = new PictureBox { Dock = DockStyle.Fill, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.DarkGray };
                for (int p = 1; p <= images.Count; p++) pages.Items.Add("第 " + p + " / " + images.Count + " 页");
                pages.SelectedIndexChanged += delegate {
                    var m = images[pages.SelectedIndex];
                    using (var s = new MemoryStream(bytes, m.Index + m.Length, int.Parse(m.Groups["size"].Value)))
                    using (var bitmap = Image.FromStream(s)) { var old = image.Image; image.Image = new Bitmap(bitmap); if (old != null) old.Dispose(); }
                };
                form.FormClosed += delegate { if (image.Image != null) image.Image.Dispose(); };
                form.Controls.Add(image); form.Controls.Add(pages); pages.SelectedIndex = 0;
            }
            else
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                form.Controls.Add(new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = true, Text = text, Font = new Font("Microsoft YaHei UI", 10) });
                // HTML is shown as escaped source here; its standalone file also opens in the OS browser.
            }
            using (form) form.ShowDialog(owner);
        }
        public static Dictionary<string, string> ValidateAll(string directory)
        {
            var result = new Dictionary<string, string>();
            foreach (string ext in Exporter.Formats)
            {
                string path = Path.Combine(directory, "findings." + ext);
                if (!File.Exists(path) || new FileInfo(path).Length == 0) throw new InvalidDataException("报告缺失或为空：" + path);
                if (ext == "json") { var r = Json.Read<ScanReport>(File.ReadAllText(path)); if (r == null || r.Findings == null) throw new InvalidDataException("报告 JSON 无效。"); }
                else if (ext == "sarif") { var s = Json.Obj(Json.Serializer().DeserializeObject(File.ReadAllText(path))); if (Convert.ToString(s["version"]) != "2.1.0") throw new InvalidDataException("SARIF 版本无效。"); }
                else if (ext == "xml") { using (var s = File.OpenRead(path)) Xml(s); }
                else if (ext == "xlsx" || ext == "zip")
                {
                    using (var z = ZipFile.OpenRead(path))
                    {
                        if (ext == "zip") { foreach (string e in Exporter.Formats.Where(e => e != "zip")) if (z.GetEntry("findings." + e) == null) throw new InvalidDataException("ZIP 缺少 " + e); }
                        else { foreach (var e in z.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels"))) using (var s = e.Open()) Xml(s); if (z.GetEntry("xl/workbook.xml") == null || z.GetEntry("xl/worksheets/sheet1.xml") == null) throw new InvalidDataException("XLSX 结构不完整。"); }
                    }
                }
                else if (ext == "pdf") { byte[] b = File.ReadAllBytes(path); if (!Encoding.ASCII.GetString(b.Take(8).ToArray()).StartsWith("%PDF-1.4") || !Encoding.ASCII.GetString(b.Skip(Math.Max(0, b.Length - 16)).ToArray()).Contains("%%EOF")) throw new InvalidDataException("PDF 结构不完整。"); }
                result[ext] = "structural-pass; visual-open-pending";
            }
            return result;
        }
    }
}
