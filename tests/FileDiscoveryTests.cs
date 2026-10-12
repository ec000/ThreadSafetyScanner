using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using ThreadSafetyScanner;

internal static class FileDiscoveryTests
{
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static SourceInventory Collect(string root, Func<string, bool> confirm, List<string> diagnostics, int files, long entries, CancellationToken cancel)
    {
        var inventory = new SourceInventory();
        FileDiscovery.Collect(root, inventory, diagnostics, cancel, s => { }, confirm, files, entries);
        return inventory;
    }
    private static void Test(string name, Action<string> body)
    {
        string root = Path.Combine(Path.GetTempPath(), "tss-discovery-中文 空格-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try { body(root); Console.WriteLine("PASS " + name); }
        finally { Directory.Delete(root, true); }
    }
    public static int Main()
    {
        Test("below threshold: only source files, including headers", root => {
            File.WriteAllText(Path.Combine(root, "a.CPP"), ""); File.WriteAllText(Path.Combine(root, "b.h"), ""); File.WriteAllText(Path.Combine(root, "notes.txt"), "");
            int prompts = 0;
            var result = Collect(root, m => { prompts++; return true; }, new List<string>(), 3, 100, CancellationToken.None);
            Require(result.Files.Count == 2 && result.Entries == 3 && prompts == 0, "Incorrect source filtering or premature warning.");
        });
        Test("continue once, preserve all files", root => {
            for (int i = 0; i < 8; i++) File.WriteAllText(Path.Combine(root, i + ".c"), "");
            int prompts = 0; var diagnostics = new List<string>();
            var result = Collect(root, m => { prompts++; Require(m.Contains("3") && m.Contains("SMB"), "Missing actionable warning."); return true; }, diagnostics, 3, 100, CancellationToken.None);
            Require(result.Files.Count == 8 && prompts == 1 && diagnostics.Count == 1, "Continue silently truncated files or repeated warning.");
        });
        Test("decline stops enumeration at threshold", root => {
            for (int i = 0; i < 8; i++) File.WriteAllText(Path.Combine(root, i + ".cpp"), "");
            var inventory = new SourceInventory(); int prompts = 0;
            try { FileDiscovery.Collect(root, inventory, new List<string>(), CancellationToken.None, s => { }, m => { prompts++; return false; }, 3, 100); throw new Exception("Decline was ignored."); }
            catch (OperationCanceledException) { Require(inventory.Files.Count == 3 && prompts == 1, "Enumeration continued after decline."); }
        });
        Test("many non-source entries also warn", root => {
            for (int i = 0; i < 6; i++) File.WriteAllText(Path.Combine(root, i + ".txt"), "");
            int prompts = 0;
            var result = Collect(root, m => { prompts++; return true; }, new List<string>(), 100, 4, CancellationToken.None);
            Require(result.Entries == 6 && result.Files.Count == 0 && prompts == 1, "Non-source directory size was ignored.");
        });
        Test("unattended scan logs warning and continues", root => {
            for (int i = 0; i < 4; i++) File.WriteAllText(Path.Combine(root, i + ".cc"), "");
            var diagnostics = new List<string>();
            var result = Collect(root, null, diagnostics, 2, 100, CancellationToken.None);
            Require(result.Files.Count == 4 && diagnostics.Single().StartsWith("WARNING:"), "Unattended warning missing.");
        });
        Test("cancellation while prompt is open", root => {
            File.WriteAllText(Path.Combine(root, "a.c"), ""); var cancel = new CancellationTokenSource();
            try { Collect(root, m => { cancel.Cancel(); return true; }, new List<string>(), 1, 100, cancel.Token); throw new Exception("Cancellation was ignored."); }
            catch (OperationCanceledException) { }
        });
        Test("ignore generated trees, skip oversized files and links", root => {
            Directory.CreateDirectory(Path.Combine(root, ".git")); File.WriteAllText(Path.Combine(root, ".git", "ignored.c"), "");
            Directory.CreateDirectory(Path.Combine(root, "real")); File.WriteAllText(Path.Combine(root, "real", "中文.h"), "");
            Directory.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(root, "real"));
            using (var file = File.Create(Path.Combine(root, "large.cpp"))) file.SetLength(24L * 1024 * 1024 + 1);
            var diagnostics = new List<string>();
            var result = Collect(root, null, diagnostics, 100, 100, CancellationToken.None);
            Require(result.Files.Count == 1 && result.Files[0].EndsWith("中文.h") && diagnostics.Count == 2, "Excluded files entered scan inventory.");
        });
        Test("actual 10000-file warning boundary", root => {
            for (int i = 0; i < FileDiscovery.FileWarningLimit + 1; i++) File.WriteAllText(Path.Combine(root, i + ".c"), "");
            var inventory = new SourceInventory(); int prompts = 0;
            FileDiscovery.Collect(root, inventory, new List<string>(), CancellationToken.None, s => { }, m => {
                prompts++; Require(inventory.Files.Count == 10000, "Warning did not trigger at 10000 files."); return true;
            });
            Require(inventory.Files.Count == 10001 && prompts == 1, "Production threshold lost files.");
        });
        return 0;
    }
}
