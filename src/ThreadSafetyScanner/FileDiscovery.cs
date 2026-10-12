using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace ThreadSafetyScanner
{
    internal sealed class SourceInventory
    {
        public readonly List<string> Files = new List<string>();
        public long Entries;
    }

    internal static class FileDiscovery
    {
        internal const int FileWarningLimit = 10000;
        internal const long EntryWarningLimit = 100000;
        private const long MaxBytes = 24L * 1024 * 1024;
        private static readonly HashSet<string> Extensions = new HashSet<string>(new[] { ".c", ".h", ".cc", ".cpp", ".cxx", ".hpp", ".hxx", ".hh" }, StringComparer.OrdinalIgnoreCase);
        internal static readonly HashSet<string> Ignored = new HashSet<string>(new[] { ".git", ".svn", ".hg", ".vs", "node_modules", "CMakeFiles" }, StringComparer.OrdinalIgnoreCase);

        internal static void Collect(string source, SourceInventory inventory, List<string> diagnostics, CancellationToken cancel,
            Action<string> progress, Func<string, bool> confirmLargeScan, int fileWarningLimit = FileWarningLimit, long entryWarningLimit = EntryWarningLimit)
        {
            if (fileWarningLimit < 1 || entryWarningLimit < 1) throw new ArgumentOutOfRangeException("fileWarningLimit");
            var todo = new Stack<DirectoryInfo>(); todo.Push(new DirectoryInfo(source));
            var timer = Stopwatch.StartNew(); bool warned = false;
            while (todo.Count > 0)
            {
                cancel.ThrowIfCancellationRequested(); var dir = todo.Pop();
                try
                {
                    // FileSystemInfo caches the directory listing metadata, avoiding a separate SMB lookup for each attribute/size.
                    foreach (var child in dir.EnumerateFileSystemInfos())
                    {
                        cancel.ThrowIfCancellationRequested(); inventory.Entries++;
                        try
                        {
                            var attrs = child.Attributes;
                            if ((attrs & FileAttributes.ReparsePoint) != 0) diagnostics.Add("INCOMPLETE: 跳过符号链接/重解析点 " + child.FullName);
                            else if ((attrs & FileAttributes.Directory) != 0) { if (!Ignored.Contains(child.Name)) todo.Push((DirectoryInfo)child); }
                            else if (Extensions.Contains(child.Extension))
                            {
                                if (((FileInfo)child).Length > MaxBytes) diagnostics.Add("INCOMPLETE: 跳过超过 24 MiB 的文件 " + child.FullName);
                                else inventory.Files.Add(child.FullName);
                            }
                        }
                        catch (IOException ex) { diagnostics.Add("INCOMPLETE: 无法读取文件属性 " + child.FullName + ": " + ex.Message); }
                        catch (UnauthorizedAccessException ex) { diagnostics.Add("INCOMPLETE: 无法读取文件属性 " + child.FullName + ": " + ex.Message); }

                        if (!warned && (inventory.Files.Count >= fileWarningLimit || inventory.Entries >= entryWarningLimit))
                        {
                            warned = true;
                            string message = string.Format("文件数量较多：已发现 {0:N0} 个可扫描 C/C++ 文件，已检查 {1:N0} 个目录项。\n\n扫描可能需要较长时间并占用较多内存；SMB 共享的耗时还取决于网络速度。建议选择更具体的源码子目录。\n\n是否继续扫描？选择“否”将取消本次任务。", inventory.Files.Count, inventory.Entries);
                            diagnostics.Add("WARNING: " + message);
                            progress(message);
                            cancel.ThrowIfCancellationRequested();
                            if (confirmLargeScan != null && !confirmLargeScan(message)) throw new OperationCanceledException("用户取消大目录扫描。", cancel);
                            cancel.ThrowIfCancellationRequested();
                        }
                        if (timer.ElapsedMilliseconds >= 500)
                        {
                            progress(string.Format("正在统计源码…已检查 {0:N0} 个目录项，发现 {1:N0} 个 C/C++ 文件。", inventory.Entries, inventory.Files.Count));
                            timer.Restart();
                        }
                    }
                }
                catch (IOException ex) { diagnostics.Add("INCOMPLETE: 无法读取目录 " + dir.FullName + ": " + ex.Message); }
                catch (UnauthorizedAccessException ex) { diagnostics.Add("INCOMPLETE: 无法读取目录 " + dir.FullName + ": " + ex.Message); }
            }
            cancel.ThrowIfCancellationRequested();
            progress(string.Format("源码统计完成：{0:N0} 个 C/C++ 文件，{1:N0} 个目录项。", inventory.Files.Count, inventory.Entries));
        }
    }
}
