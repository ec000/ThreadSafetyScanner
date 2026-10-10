#Requires -Version 5.1
<#
 Windows 10 1709 / PowerShell 5.1: fully offline, read-only C/C++ candidate scanner.
 No Python, rg, WSL, administrator privileges or network needed.
 Reports always go outside the source tree.
 Detection is a triage aid; a match does not prove a race.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory=$true,Position=0)][string]$Source,
    [Parameter(Position=1)][string]$Out = (Join-Path $env:USERPROFILE 'thread_safety_reports'),
    [int]$MaxFileMB = 24,
    [switch]$IncludeGenerated
)
Set-StrictMode -Version 2.0
$ErrorActionPreference = 'Stop'

function CsvValue([object]$v) {
    if ($null -eq $v) { return '""' }
    return '"' + ([string]$v).Replace('"','""') + '"'
}
function CsvLine([object[]]$columns) {
    return (($columns | ForEach-Object { CsvValue $_ }) -join ',')
}
function EscapeMd([string]$s) {
    return $s.Replace('|', '\|').Replace('`','\`').Replace("`r",' ').Replace("`n",' ')
}
function AddError([string]$message) {
    $script:FailureCount++
    if ($script:FailureCount -le 300) { $script:Problems.Add($message) }
}

$Source = [IO.Path]::GetFullPath($Source)
$Out = [IO.Path]::GetFullPath($Out)
# Normalize trailing separators, but preserve drive root (e.g. D:\).
if ($Source.Length -gt [IO.Path]::GetPathRoot($Source).Length) { $Source = $Source.TrimEnd([char[]]@('\','/')) }
if ($Out.Length -gt [IO.Path]::GetPathRoot($Out).Length) { $Out = $Out.TrimEnd([char[]]@('\','/')) }
if (-not [IO.Directory]::Exists($Source)) { throw "Source path not found: $Source" }
if (($Out -ieq $Source) -or ($Out.StartsWith($Source.TrimEnd([char[]]@('\','/')) + [IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase))) {
    throw 'Report directory must be OUTSIDE source directory.'
}
[IO.Directory]::CreateDirectory($Out) | Out-Null
$reportCsv = Join-Path $Out 'findings.csv'
$reportMd  = Join-Path $Out 'summary.md'
$reportErr = Join-Path $Out 'scan_errors.txt'

# P1/P2 mean review priority, never confirmed security severity.
$rules = @(
    @('strtok','P1','Shared parse cursor','strtok_r'),
    @('inet_ntoa','P2','Library-managed result lifetime; libc differences','inet_ntop'),
    @('ipaddr_ntoa','P1','lwIP shared address string buffer','ipaddr_ntoa_r'),
    @('ip4addr_ntoa','P1','lwIP shared IPv4 string buffer','ip4addr_ntoa_r'),
    @('ip6addr_ntoa','P1','lwIP shared IPv6 string buffer','ip6addr_ntoa_r'),
    @('localtime','P2','Internal struct tm and timezone concerns','localtime_r'),
    @('gmtime','P2','Internal struct tm result','gmtime_r'),
    @('asctime','P1','Static time string result','strftime'),
    @('ctime','P1','Static time string result','localtime_r + strftime'),
    @('strerror','P2','Depends on libc/version','strerror_r; check GNU/POSIX differences'),
    @('gethostbyname','P1','Legacy resolver static data','getaddrinfo'),
    @('gethostbyname2','P1','Legacy resolver static data','getaddrinfo'),
    @('gethostbyaddr','P1','Legacy resolver static data','getnameinfo'),
    @('getpwnam','P1','Internal passwd data','getpwnam_r'),
    @('getpwuid','P1','Internal passwd data','getpwuid_r'),
    @('readdir','P2','Same DIR* concurrency/lifetime','Independent DIR* or protect and copy'),
    @('rand','REVIEW','Predictable PRNG; shared state depends on libc','CSPRNG for secrets; local PRNG otherwise'),
    @('srand','REVIEW','Resets PRNG state','Initialize once; CSPRNG for secrets'),
    @('getenv','P2','Lifetime may race with environment mutation','Cache before threads'),
    @('setenv','P2','Environment mutation','Initialize before threads'),
    @('putenv','P2','Environment mutation and caller storage','Initialize before threads'),
    @('unsetenv','P2','Environment mutation','Initialize before threads'),
    @('setlocale','P2','Process-wide locale changes','Set before threads or use uselocale'),
    @('getopt','P2','Shared argument parsing state','Parse once at startup'),
    @('getopt_long','P2','Shared argument parsing state','Parse once at startup'),
    @('getopt_long_only','P2','Shared argument parsing state','Parse once at startup'),
    @('system','REVIEW','Shell/blocking/process side effects','posix_spawn / exec with explicit argv'),
    @('exit','REVIEW','Ends process, not just worker thread','return / pthread_exit in thread')
)
$ruleMap = @{}
foreach ($r in $rules) { $ruleMap[$r[0]] = $r }
$ruleNames = @($rules | ForEach-Object { [regex]::Escape([string]$_[0]) } | Sort-Object Length -Descending)
$callPattern = '\b(?<fn>' + ($ruleNames -join '|') + ')\s*\('
$opt = [Text.RegularExpressions.RegexOptions]::Compiled
$callRegex = [Text.RegularExpressions.Regex]::new($callPattern, $opt)
# Remove C/C++ comments, character and string literals while retaining byte offsets/newlines.
# Best effort: C++ raw strings, preprocessor indirection, macro-generated calls require review.
$commentStringPattern = '/\*[\s\S]*?\*/|//[^\r\n]*|"(?:\\.|[^"\\])*"|''(?:\\.|[^''\\])*'''
$maskRegex = [Text.RegularExpressions.Regex]::new($commentStringPattern, $opt)
$maskEval = [Text.RegularExpressions.MatchEvaluator] {
    param($m)
    # Keep newline positions and original string length intact.
    return [regex]::Replace($m.Value, '[^\r\n]', ' ')
}
$allowedExt = @('.c','.h','.cc','.cpp','.cxx','.hpp','.hxx','.hh')
$skipDirs = @('.git','.svn','.hg','.vs','node_modules','.idea')
if (-not $IncludeGenerated) { $skipDirs += @('CMakeFiles') }
$stack = New-Object 'System.Collections.Generic.Stack[string]'
$stack.Push($Source)
$utf8BOM = [Text.UTF8Encoding]::new($true)
$csv = [IO.StreamWriter]::new($reportCsv, $false, $utf8BOM)
$csv.WriteLine((CsvLine @('priority','function','file','line','column','reason','suggestion','source_excerpt')))
$totalFiles = 0
$scannedFiles = 0
$skippedFiles = 0
$findings = 0
$FailureCount = 0
$Problems = New-Object 'System.Collections.Generic.List[string]'
$preview = New-Object 'System.Collections.Generic.List[object]'
$counts = @{}
$priorityCounts = @{'P1'=0;'P2'=0;'REVIEW'=0}
$start = Get-Date
Write-Host "[Start] source: $Source" -ForegroundColor Cyan
Write-Host "[Start] output: $Out"
try {
    while ($stack.Count -gt 0) {
        $dir = $stack.Pop()
        try {
            foreach ($subdir in [IO.Directory]::EnumerateDirectories($dir)) {
                try {
                    $dn = [IO.Path]::GetFileName($subdir)
                    if ($skipDirs -contains $dn) { continue }
                    $attrs = [IO.File]::GetAttributes($subdir)
                    if (($attrs -band [IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
                    $stack.Push($subdir)
                } catch { AddError "DIR: $subdir : $($_.Exception.Message)" }
            }
        } catch { AddError "DIR: $dir : $($_.Exception.Message)" }
        try {
            foreach ($file in [IO.Directory]::EnumerateFiles($dir)) {
                if ($allowedExt -notcontains [IO.Path]::GetExtension($file).ToLowerInvariant()) { continue }
                $totalFiles++
                try {
                    $info = [IO.FileInfo]::new($file)
                    if ($info.Length -gt ([int64]$MaxFileMB * 1024 * 1024)) {
                        $skippedFiles++
                        AddError "SKIP oversized: $file ($($info.Length) bytes)"
                        continue
                    }
                    # StreamReader automatically recognizes BOMs incl. UTF-16; default UTF-8.
                    $sr = [IO.StreamReader]::new($file, [Text.Encoding]::UTF8, $true)
                    try { $code = $sr.ReadToEnd() } finally { $sr.Dispose() }
                    $scannedFiles++
                    if (-not $callRegex.IsMatch($code)) { continue }
                    $stripped = $maskRegex.Replace($code, $maskEval)
                    $relative = $file.Substring($Source.Length).TrimStart([char[]]@('\','/'))
                    $line = 1
                    $pos = 0
                    foreach ($m in $callRegex.Matches($stripped)) {
                        $fn = $m.Groups['fn'].Value
                        # Skip macro definition declarations, including function-like macros.
                        $prevNL = $code.LastIndexOf("`n", [Math]::Max(0, $m.Index - 1))
                        $lineStart = $prevNL + 1
                        $lineEnd = $code.IndexOf("`n", $m.Index)
                        if ($lineEnd -lt 0) { $lineEnd = $code.Length }
                        $sourceLine = $code.Substring($lineStart, $lineEnd - $lineStart).TrimEnd("`r")
                        if ($sourceLine -match '^\s*#\s*define\s+') { continue }
                        while ($true) {
                            $nextNL = $stripped.IndexOf("`n", $pos)
                            if (($nextNL -lt 0) -or ($nextNL -ge $m.Index)) { break }
                            $line++
                            $pos = $nextNL + 1
                        }
                        $r = $ruleMap[$fn]
                        $pri = [string]$r[1]
                        $snippet = $sourceLine.Trim()
                        if ($snippet.Length -gt 320) { $snippet = $snippet.Substring(0,320) }
                        $csv.WriteLine((CsvLine @($pri, $fn, $relative, $line, ($m.Index - $lineStart + 1), $r[2], $r[3], $snippet)))
                        $findings++
                        $priorityCounts[$pri]++
                        if (-not $counts.ContainsKey($fn)) { $counts[$fn] = 0 }
                        $counts[$fn]++
                        if ($preview.Count -lt 200) {
                            $preview.Add(@($pri, $relative, $line, $fn, $snippet))
                        }
                    }
                } catch { AddError "FILE: $file : $($_.Exception.Message)" }
            }
        } catch { AddError "FILES: $dir : $($_.Exception.Message)" }
        if (($totalFiles -gt 0) -and (($totalFiles % 3000) -eq 0)) {
            Write-Host "[Progress] $totalFiles files visited, $findings candidate calls"
        }
    }
} finally { $csv.Dispose() }
$elapsed = [math]::Round(((Get-Date)-$start).TotalSeconds,2)
$md = [IO.StreamWriter]::new($reportMd, $false, $utf8BOM)
try {
    $md.WriteLine('# Windows offline C/C++ thread-safety function audit')
    $md.WriteLine('')
    $md.WriteLine("- Source: ``$Source``")
    $md.WriteLine("- Result count (candidates, not proven races): **$findings**")
    $md.WriteLine("- P1: $($priorityCounts['P1']), P2: $($priorityCounts['P2']), REVIEW: $($priorityCounts['REVIEW'])")
    $md.WriteLine("- Files: scanned $scannedFiles; skipped oversized $skippedFiles; warnings $FailureCount; seconds $elapsed")
    $md.WriteLine('')
    $md.WriteLine('## Counts by function')
    $md.WriteLine('')
    $md.WriteLine('| Function | Candidate calls | Priority | Suggested replacement |')
    $md.WriteLine('|---|---:|---|---|')
    foreach ($entry in @($counts.GetEnumerator() | Sort-Object Value -Descending)) {
        $r = $ruleMap[$entry.Key]
        $md.WriteLine('| ' + $entry.Key + ' | ' + $entry.Value + ' | ' + $r[1] + ' | ' + (EscapeMd([string]$r[3])) + ' |')
    }
    $md.WriteLine('')
    $md.WriteLine('## First 200 candidates (unsorted traversal order)')
    $md.WriteLine('')
    $md.WriteLine('| Priority | File:Line | Function | Snippet |')
    $md.WriteLine('|---|---|---|---|')
    foreach ($p in $preview) {
        $loc = [string]$p[1] + ':' + [string]$p[2]
        $md.WriteLine('| '+$p[0]+' | '+(EscapeMd($loc))+' | '+$p[3]+' | '+(EscapeMd([string]$p[4]))+' |')
    }
    $md.WriteLine('')
    $md.WriteLine('## Important limitations')
    $md.WriteLine('')
    $md.WriteLine('- This is textual/static detection with C-style comment and string filtering. Function definitions, prototypes, C++ raw strings, preprocessor macros and function-pointer calls may cause false positives or misses.')
    $md.WriteLine('- Multi-thread unsafety cannot be established from a function name alone. Check libc flavor/version, execution context, shared pointers and locking.')
    $md.WriteLine('- inet_ntoa / strerror / rand may be MT-Safe on some libc versions. readdir is usually safe when each thread uses its own DIR*.')
    $md.WriteLine('- In FreeRTOS/lwIP, using raw APIs outside tcpip_thread/core locking and pbuf/PCB lifetimes require separate analysis.')
    $md.WriteLine('- This scanner never changes source files. The report folder must not be inside source tree.')
} finally { $md.Dispose() }
[IO.File]::WriteAllLines($reportErr, @($Problems), $utf8BOM)
Write-Host "[Done] files=$scannedFiles findings=$findings errors=$FailureCount time=${elapsed}s" -ForegroundColor Green
Write-Host "CSV: $reportCsv"
Write-Host "Summary: $reportMd"
Write-Host "Errors: $reportErr"
if ($FailureCount -gt 0) { exit 2 }
exit 0
