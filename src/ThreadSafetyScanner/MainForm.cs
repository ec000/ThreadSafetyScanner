using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ThreadSafetyScanner
{
    internal sealed class MainForm : Form
    {
        private readonly TextBox source = new TextBox { Anchor = AnchorStyles.Left | AnchorStyles.Right };
        private readonly CheckBox rg = new CheckBox { Text = "ripgrep 快速扫描", Checked = true, AutoSize = true };
        private readonly CheckBox cpp = new CheckBox { Text = "Cppcheck 深度扫描", Checked = true, AutoSize = true };
        private readonly ComboBox platform = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 90 };
        private readonly Button start = new Button { Text = "开始扫描", AutoSize = true };
        private readonly Button cancel = new Button { Text = "取消任务", Enabled = false, AutoSize = true };
        private readonly ComboBox priority = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly ComboBox engine = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 100 };
        private readonly ComboBox category = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        private readonly DataGridView grid = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells };
        private readonly RichTextBox preview = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, WordWrap = false, Font = new Font(FontFamily.GenericMonospace, 10) };
        private readonly Label status = new Label { Dock = DockStyle.Bottom, Height = 48, Padding = new Padding(8), Text = "就绪。结果是待人工复核的静态候选，不能自动证明数据竞争。Semgrep：不支持。" };
        private ScanReport report;
        private CancellationTokenSource cancellation;
        private bool busy;
        public MainForm()
        {
            Icon = AppIcon.Value;
            Text = "ThreadSafetyScanner — 离线线程安全扫描"; Width = 1280; Height = 820; MinimumSize = new Size(900, 600); StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.Dpi; Font = new Font("Microsoft YaHei UI", 9);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(8, 6, 8, 0), Margin = Padding.Empty };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.AutoSize)); layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var pathRow = new TableLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 2) };
            pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            pathRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            pathRow.Controls.Add(new Label { Text = "源码目录（本地 / SMB）", AutoSize = true }, 0, 0); pathRow.Controls.Add(source, 1, 0);
            AddButton(pathRow, "选择目录…", delegate { using (var d = new FolderBrowserDialog()) { if (d.ShowDialog(this) == DialogResult.OK) source.Text = d.SelectedPath; } });
            var scanRow = Row(); scanRow.Controls.Add(rg); scanRow.Controls.Add(cpp); scanRow.Controls.Add(new Label { Text = "源码目标平台", AutoSize = true }); scanRow.Controls.Add(platform);
            platform.Items.AddRange(new object[] { "win64", "unix32", "unix64" }); platform.SelectedIndex = 0;
            scanRow.Controls.Add(start); scanRow.Controls.Add(cancel);
            var actionsRow = Row();
            AddButton(actionsRow, "规则管理", delegate { if (!busy) using (var dialog = new RulesForm()) dialog.ShowDialog(this); });
            AddButton(actionsRow, "历史记录", LoadHistory);
            AddButton(actionsRow, "导出报告…", Export);
            AddButton(actionsRow, "打开导出报告…", delegate { using (var d = new OpenFileDialog { Filter = "离线报告|*.csv;*.json;*.md;*.html;*.txt;*.xml;*.sarif;*.xlsx;*.pdf;*.zip" }) if (d.ShowDialog(this) == DialogResult.OK) ReportViewer.Open(this, d.FileName); });
            AddButton(actionsRow, "日志目录", delegate { Process.Start(new ProcessStartInfo("explorer.exe", ProcessRunner.Quote(Paths.Logs)) { UseShellExecute = true }); });
            var filters = Row(); filters.Controls.Add(new Label { Text = "筛选", AutoSize = true });
            priority.Items.AddRange(new object[] { "全部优先级", "P1", "P2", "REVIEW" }); priority.SelectedIndex = 0;
            engine.Items.AddRange(new object[] { "全部引擎", "ripgrep", "cppcheck" }); engine.SelectedIndex = 0;
            category.Items.Add("全部分类"); category.SelectedIndex = 0;
            filters.Controls.Add(priority); filters.Controls.Add(engine); filters.Controls.Add(category);
            foreach (var filter in new[] { priority, engine, category }) filter.SelectedIndexChanged += delegate { Filter(); };
            foreach (Control row in new Control[] { pathRow, scanRow, actionsRow, filters }) AlignRow(row);
            source.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 350, Margin = new Padding(0, 4, 0, 0) }; split.Panel1.Controls.Add(grid); split.Panel2.Controls.Add(preview);
            layout.Controls.Add(pathRow, 0, 0); layout.Controls.Add(scanRow, 0, 1); layout.Controls.Add(actionsRow, 0, 2); layout.Controls.Add(filters, 0, 3); layout.Controls.Add(split, 0, 4);
            Controls.Add(layout); Controls.Add(status);
            start.Click += async delegate { await StartScan(); }; cancel.Click += delegate { if (cancellation != null) cancellation.Cancel(); };
            grid.SelectionChanged += delegate { Preview(); };
            FormClosing += delegate(object sender, FormClosingEventArgs e) { if (busy) { if (cancellation != null) cancellation.Cancel(); e.Cancel = true; status.Text = "正在取消任务，请等待进程退出后再关闭。"; } };
        }
        private static FlowLayoutPanel Row() { return new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, Margin = Padding.Empty, Padding = new Padding(0, 2, 0, 2) }; }
        private static void AlignRow(Control row)
        {
            foreach (Control control in row.Controls)
            {
                // With no vertical anchor, both flow and table rows center controls of different heights.
                control.Anchor = AnchorStyles.Left;
                control.Margin = new Padding(0, 1, 8, 1);
                var label = control as Label;
                if (label != null) { label.Padding = Padding.Empty; label.TextAlign = ContentAlignment.MiddleLeft; }
                var check = control as CheckBox;
                if (check != null) { check.TextAlign = ContentAlignment.MiddleLeft; check.CheckAlign = ContentAlignment.MiddleLeft; }
                var button = control as Button;
                if (button != null) { button.AutoSizeMode = AutoSizeMode.GrowAndShrink; button.MinimumSize = new Size(76, 26); button.Padding = new Padding(8, 0, 8, 0); }
            }
            if (row.Controls.Count > 0) row.Controls[row.Controls.Count - 1].Margin = new Padding(0, 1, 0, 1);
        }
        private void AddButton(Control panel, string text, Action click) { var b = new Button { Text = text, AutoSize = true }; b.Click += delegate { Guard(click); }; panel.Controls.Add(b); }
        private void Guard(Action action) { try { action(); } catch (Exception e) { MessageBox.Show(this, e.Message, "操作失败", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        private async Task StartScan()
        {
            if (busy) return;
            string root = source.Text; bool useRg = rg.Checked, useCpp = cpp.Checked; string target = Convert.ToString(platform.SelectedItem);
            busy = true; start.Enabled = false; source.Enabled = false; rg.Enabled = cpp.Enabled = platform.Enabled = false; cancel.Enabled = true;
            cancellation = new CancellationTokenSource();
            try
            {
                report = await Task.Run(() => Scanner.Scan(root, useRg, useCpp, target, cancellation.Token,
                    message => BeginInvoke(new Action(() => status.Text = message.Replace('\n', ' '))),
                    message => (bool)Invoke(new Func<bool>(() => !cancellation.IsCancellationRequested &&
                        MessageBox.Show(this, message, "文件数量较多", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes))));
                category.Items.Clear(); category.Items.Add("全部分类"); foreach (string c in report.Findings.Select(f => f.Category).Distinct()) category.Items.Add(c); category.SelectedIndex = 0;
                Filter();
                status.Text = "任务状态：" + report.Status + "；源码文件 " + report.SourceFileCount + "；候选 " + report.Findings.Count + "；历史已保存。";
                if (report.Status != "completed") preview.Text = string.Join(Environment.NewLine, report.Diagnostics);
            }
            catch (Exception e) { status.Text = "扫描未完成：" + e.Message; MessageBox.Show(this, e.Message, "扫描失败"); }
            finally { cancellation.Dispose(); cancellation = null; busy = false; start.Enabled = true; cancel.Enabled = false; source.Enabled = rg.Enabled = cpp.Enabled = platform.Enabled = true; }
        }
        private void Filter()
        {
            if (report == null) return;
            IEnumerable<Finding> list = report.Findings;
            if (priority.SelectedIndex > 0) list = list.Where(f => f.Priority == Convert.ToString(priority.SelectedItem));
            if (engine.SelectedIndex > 0) list = list.Where(f => f.Engine == Convert.ToString(engine.SelectedItem));
            if (category.SelectedIndex > 0) list = list.Where(f => f.Category == Convert.ToString(category.SelectedItem));
            grid.DataSource = new BindingList<Finding>(list.ToList());
        }
        private void Preview()
        {
            if (report == null || grid.CurrentRow == null) return;
            var f = grid.CurrentRow.DataBoundItem as Finding; if (f == null) return;
            Guard(delegate {
                string file = Path.GetFullPath(Path.Combine(report.Source, f.File));
                if (!Paths.IsWithin(file, report.Source)) { preview.Text = "位置在源码目录外：" + file; return; }
                int from = Math.Max(1, f.Line - 5), to = f.Line + 5;
                preview.Text = f.File + ":" + f.Line + "  " + f.RuleId + Environment.NewLine + f.Reason + Environment.NewLine + f.Recommendation + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, File.ReadLines(file).Select((line, index) => new { line, number = index + 1 }).Skip(from - 1).Take(to - from + 1).Select(v => (v.number == f.Line ? "> " : "  ") + v.number.ToString().PadLeft(6) + "  " + v.line));
            });
        }
        private void LoadHistory()
        {
            if (busy) return;
            using (var d = new OpenFileDialog { InitialDirectory = Paths.History, Filter = "扫描历史 JSON|*.json" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                report = Json.Read<ScanReport>(File.ReadAllText(d.FileName));
                if (report == null || report.Findings == null || report.Diagnostics == null) throw new InvalidDataException("历史格式无效。");
                source.Text = report.Source; category.Items.Clear(); category.Items.Add("全部分类"); foreach (string c in report.Findings.Select(f => f.Category).Distinct()) category.Items.Add(c); category.SelectedIndex = 0;
                Filter(); status.Text = "历史状态：" + report.Status + "；候选 " + report.Findings.Count;
            }
        }
        private async void Export()
        {
            if (busy || report == null) { MessageBox.Show(this, "请先完成扫描或打开历史。"); return; }
            using (var d = new SaveFileDialog { InitialDirectory = Paths.Reports, FileName = "findings.zip", Filter = "ZIP 全格式报告|*.zip|CSV|*.csv|JSON|*.json|Markdown|*.md|HTML|*.html|TXT|*.txt|XML|*.xml|SARIF|*.sarif|XLSX|*.xlsx|PDF|*.pdf" })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                busy = true; start.Enabled = false;
                try { var snapshot = report; await Task.Run(() => Exporter.Save(snapshot, d.FileName, Path.GetExtension(d.FileName).Substring(1).ToLowerInvariant())); status.Text = "导出完成：" + d.FileName + "（包含全部结果，界面筛选不缩减报告）"; }
                catch (Exception ex) { MessageBox.Show(this, ex.Message, "导出失败"); }
                finally { busy = false; start.Enabled = true; }
            }
        }
    }
    internal sealed class RulesForm : Form
    {
        public RulesForm()
        {
            Icon = AppIcon.Value;
            Text = "规则管理 — 只修改当前用户配置"; Width = 1100; Height = 550;
            var rules = new BindingList<Rule>(RuleStore.Load());
            var grid = new DataGridView { Dock = DockStyle.Fill, DataSource = rules, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.DisplayedCells, AllowUserToAddRows = true, AllowUserToDeleteRows = true };
            var row = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true };
            var save = new Button { Text = "验证并保存", AutoSize = true }; save.Click += delegate { try { grid.EndEdit(); RuleStore.Save(rules.ToList()); DialogResult = DialogResult.OK; Close(); } catch (Exception e) { MessageBox.Show(this, e.Message); } };
            var reset = new Button { Text = "恢复内置规则", AutoSize = true }; reset.Click += delegate { rules = new BindingList<Rule>(Json.Read<List<Rule>>(File.ReadAllText(Path.Combine(Paths.Root, "Rules", "rules.json")))); grid.DataSource = rules; };
            row.Controls.Add(save); row.Controls.Add(reset); Controls.Add(grid); Controls.Add(row);
        }
    }
}
