// Standalone .NET Framework 4.7.1 validation harness; no Python, PowerShell policy or Office.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32;

internal static class Windows1709Validation
{
    static readonly JavaScriptSerializer Json = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
    static string Output;
    static Dictionary<string, object> Evidence = new Dictionary<string, object>();
    static string Hash(string file) { using (var sha = SHA256.Create()) using (var s = File.OpenRead(file)) return BitConverter.ToString(sha.ComputeHash(s)).Replace("-", "").ToLowerInvariant(); }
    static void Save(string name, object value) { File.WriteAllText(Path.Combine(Output, name), Json.Serialize(value), new UTF8Encoding(false)); }
    static void Require(bool condition, string error) { if (!condition) throw new InvalidOperationException(error); }
    static string Arg(string[] args, string key, string fallback) { int i = Array.IndexOf(args,key); return i < 0 ? fallback : i+1 < args.Length ? args[i+1] : throwArg(key); }
    static string throwArg(string key) { throw new ArgumentException("Missing value for " + key); }
    static string Quote(string value)
    {
        var b = new StringBuilder("\""); int slash = 0;
        foreach(char c in value) { if(c=='\\') { slash++; continue; } b.Append('\\', c=='"' ? slash*2+1 : slash); b.Append(c); slash=0; }
        b.Append('\\',slash*2); return b.Append('"').ToString();
    }
    static int Run(string file, string arguments, bool elevated)
    {
        Console.WriteLine("RUN " + Path.GetFileName(file));
        using(var p = Process.Start(new ProcessStartInfo(file,arguments) { UseShellExecute = elevated, Verb = elevated ? "runas" : "", CreateNoWindow = !elevated }))
        {
            if (!p.WaitForExit(30*60*1000)) { try { p.Kill(); } catch { } throw new TimeoutException("Process exceeded 30 minutes: " + file); }
            return p.ExitCode;
        }
    }
    static bool Offline()
    {
        return !NetworkInterface.GetAllNetworkInterfaces().Any(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback);
    }
    static Dictionary<string,object> Baseline()
    {
        var baseline = new Dictionary<string,object>();
        using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            baseline["build"] = Convert.ToString(k.GetValue("CurrentBuildNumber"));
            baseline["ubr"] = Convert.ToInt32(k.GetValue("UBR",-1));
            baseline["edition"] = Convert.ToString(k.GetValue("EditionID"));
            baseline["product"] = Convert.ToString(k.GetValue("ProductName"));
        }
        baseline["architecture"] = Environment.Is64BitOperatingSystem && Environment.Is64BitProcess ? "x64" : "unsupported";
        baseline["offlineAdapters"] = Offline();
        baseline["networkAdapters"] = NetworkInterface.GetAllNetworkInterfaces().Select(n=>new { name=n.Name, type=n.NetworkInterfaceType.ToString(), status=n.OperationalStatus.ToString() }).ToArray();
        baseline["elevatedHarness"] = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);
        using(var k=Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) baseline["netRelease"] = k==null?0:Convert.ToInt32(k.GetValue("Release",0));
        var installed = new List<string>();
        foreach(var hive in new[]{RegistryHive.LocalMachine,RegistryHive.CurrentUser}) foreach(var view in new[]{RegistryView.Registry64,RegistryView.Registry32})
        using(var root=RegistryKey.OpenBaseKey(hive,view)) using(var key=root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"))
        {
            if(key==null) continue;
            foreach(string name in key.GetSubKeyNames()) using(var child=key.OpenSubKey(name))
            {
                string display=Convert.ToString(child.GetValue("DisplayName",""));
                if(new[]{"python","cppcheck","ripgrep","semgrep","visual studio","threadsafetyscanner"}.Any(s=>display.IndexOf(s,StringComparison.OrdinalIgnoreCase)>=0)) installed.Add(display);
            }
        }
        var tools = new List<string>();
        foreach(string dir in (Environment.GetEnvironmentVariable("PATH")??"").Split(';')) foreach(string name in new[]{"python.exe","python3.exe","py.exe","rg.exe","cppcheck.exe","semgrep.exe","devenv.exe","cl.exe"})
        {
            try { string file=Path.Combine(dir.Trim('"'),name); if(File.Exists(file)) tools.Add(file); } catch { }
        }
        baseline["preinstalledDevelopmentProducts"]=installed; baseline["pathTools"]=tools;
        baseline["utc"]=DateTime.UtcNow.ToString("o");
        return baseline;
    }
    static int Main(string[] args)
    {
        Output=Path.GetFullPath(Arg(args,"--output",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ThreadSafetyScanner-Validation",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"))));
        Directory.CreateDirectory(Output); Evidence["status"]="pending";
        try
        {
            string setup=Path.GetFullPath(Arg(args,"--setup",Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ThreadSafetyScanner_Offline_Setup_x64.exe")));
            Require(File.Exists(setup),"Copy the complete offline setup beside this validation EXE, or supply --setup.");
            Evidence["installerSha256"]=Hash(setup);
            var baseline=Baseline(); Evidence["baseline"]=baseline; Save("baseline.json",baseline);
            Require(Convert.ToString(baseline["build"])=="16299" && Convert.ToInt32(baseline["ubr"])==15 && Convert.ToString(baseline["edition"])=="Professional","Requires exact Windows 10 Pro Build 16299.15; newer systems cannot pass this gate.");
            Require(Convert.ToString(baseline["architecture"])=="x64","Requires x64.");
            Require(Convert.ToInt32(baseline["netRelease"])==461308,"Requires the clean 1709 inbox .NET 4.7.1 baseline.");
            Require(!(bool)baseline["elevatedHarness"],"Run validation as an ordinary non-elevated user; only setup/uninstall will request UAC.");
            Require((bool)baseline["offlineAdapters"],"Disconnect/disable every external network adapter before validation.");
            Require(((List<string>)baseline["preinstalledDevelopmentProducts"]).Count==0 && ((List<string>)baseline["pathTools"]).Count==0,"Pre-existing scanner/development tools detected; restore a clean VM snapshot.");
            string install=Arg(args,"--install-dir",Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"ThreadSafetyScanner"));
            Evidence["installPath"]=install;
            Require(!Directory.Exists(install) || !Directory.EnumerateFileSystemEntries(install).Any(),"Installation directory must be empty.");
            Require(Run(setup,"/S /D="+install,true)==0,"Offline setup failed.");
            string app=Path.Combine(install,"ThreadSafetyScanner.exe"); Require(File.Exists(app),"Installed GUI missing.");
            Require(Run(app,"--verify-payload",false)==0,"Installed payload failed SHA256/runtime verification.");
            Evidence["installation"]=true;
            // Exercise real GUI startup under the unprivileged caller.
            using(var gui=Process.Start(new ProcessStartInfo(app) { UseShellExecute=false }))
            {
                gui.WaitForInputIdle(20000); Thread.Sleep(1500); gui.Refresh();
                Require(!gui.HasExited && gui.MainWindowHandle!=IntPtr.Zero,"GUI did not create a window.");
                gui.CloseMainWindow(); if(!gui.WaitForExit(10000)) { gui.Kill(); throw new InvalidOperationException("GUI failed to exit."); }
            }
            Evidence["guiWindowStarted"]=true;
            string reports=Path.Combine(Output,"Reports");
            Require(Run(app,"--self-test --output "+Quote(reports),false)==0,"Real engine/export self-test failed; inspect self-test.json and history.");
            var self=Json.Deserialize<Dictionary<string,object>>(File.ReadAllText(Path.Combine(reports,"self-test.json")));
            Require(Convert.ToBoolean(self["passed"]),"Self-test did not pass."); Evidence["engineAndExports"]=self;
            Require(Offline(),"Network adapter became active during scan.");
            string sdk=Path.Combine(Output,"SDK 中文 空格"); Directory.CreateDirectory(sdk);
            string sentinel=Path.Combine(sdk,"user-source.txt"); File.WriteAllText(sentinel,"preserve source",Encoding.UTF8); string sentinelHash=Hash(sentinel);
            string reportHash=Hash(Path.Combine(reports,"both","findings.json"));
            string history=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ThreadSafetyScanner","History");
            Require(Directory.Exists(history) && Directory.EnumerateFiles(history,"*.json").Any(),"History was not stored in the current user's profile.");
            string uninstall=Path.Combine(install,"Uninstall.exe"); Require(Run(uninstall,"/S",true)==0,"Uninstall failed.");
            var timer=Stopwatch.StartNew(); while(File.Exists(app) && timer.Elapsed.TotalSeconds<30) Thread.Sleep(200);
            Require(!File.Exists(app),"Uninstall left managed application files.");
            Require(Hash(sentinel)==sentinelHash && Hash(Path.Combine(reports,"both","findings.json"))==reportHash && Directory.Exists(history),"Uninstall removed or changed source/report/history.");
            Evidence["uninstallPreservesUserData"]=true;
            Require(Run(setup,"/S /D="+install,true)==0 && Run(app,"--verify-payload",false)==0,"Reinstallation failed.");
            Require(Run(app,"--self-test --output "+Quote(Path.Combine(Output,"ReinstallReports")),false)==0,"Scan/export failed after reinstall.");
            Evidence["reinstallation"]=true; Evidence["offlineAfterTest"]=Offline(); Require(Offline(),"Network adapter became active.");
            Evidence["automatedPassed"]=true;
            Evidence["status"]="automated-pass-manual-checks-pending";
            Console.WriteLine("Automated checks passed. Manual GUI/report-open/cancel checks are still required.");
        }
        catch(Exception ex) { Evidence["automatedPassed"]=false; Evidence["status"]="failed-or-wrong-baseline"; Evidence["error"]=ex.ToString(); Console.Error.WriteLine(ex.Message); }
        finally
        {
            Evidence["utc"]=DateTime.UtcNow.ToString("o"); Save("evidence.json",Evidence);
            Save("manual-checklist.json",new { operatorName="", status="pending", installerSha256=Evidence.ContainsKey("installerSha256")?Evidence["installerSha256"]:"", checks=new Dictionary<string,bool> { {"interactiveInstallPathAndShortcuts",false},{"guiNormalUserScanThreeModes",false},{"ruleEditFilterPreviewHistory",false},{"cancelDuringRunningScan",false},{"openAllTenFormatsAndChineseContent",false},{"preserveSourceReportsConfiguration",false},{"noNetworkTrafficObserved",false} } });
            Console.WriteLine("Evidence: "+Output);
        }
        return Evidence.ContainsKey("automatedPassed") && (bool)Evidence["automatedPassed"]?0:1;
    }
}
