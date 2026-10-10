Windows 10 Pro 1709 - 离线扫描工具包更新版（2026-10-10）
=================================================
针对以下已安装环境：
  Python 3.11.9
  ripgrep: D:\ThreadSafetyScanner\tools\rg.exe
  Cppcheck: 已安装（可能未加入 PATH）

本包不包含第三方 EXE/MSI 安装程序。没有必要重新安装已有软件。

本次改动：
1. CHECK_ENV.cmd 直接检查 tools\rg.exe，同时检查 Cppcheck 常见安装位置。
2. 新增 START_FAST_SCAN.cmd，一键执行 Python + ripgrep 扫描。
3. optional\RUN_CPPCHECK.cmd 改进检测逻辑，可以从 Program Files 或 PATH 找到 Cppcheck。

一、解压与文件布局
建议将本 ZIP 内容覆盖解压到 D:\ThreadSafetyScanner\；保留你原来的 tools\rg.exe。
重要：原来的 rg.exe 不包含在本 ZIP 里，请不要删除。

D:\ThreadSafetyScanner\
    CHECK_ENV.cmd
    START_FAST_SCAN.cmd
    START_SCAN.cmd
    tools\rg.exe
    optional\scan_thread_unsafe.py
    optional\RUN_CPPCHECK.cmd
    examples\test_sample.c

二、验证环境
双击 CHECK_ENV.cmd。
预计能看到 Python 3.11.9、rg 版本号、Cppcheck 版本号。
如果 Cppcheck 没有被发现，先查找 cppcheck.exe 的实际安装位置。

三、自测扫描
打开 CMD，输入：
D:\ThreadSafetyScanner\START_FAST_SCAN.cmd "D:\ThreadSafetyScanner\examples" "D:\scan_selftest"

四、扫描真实源码（建议先小范围）
D:\ThreadSafetyScanner\START_FAST_SCAN.cmd "D:\SDK\app" "D:\scan_results"

输出报告：findings.csv、findings.json、summary.md、rg_stderr.log。
Python+rg 属于候选函数扫描；注释、函数声明、#if 未启用代码可能误报。
不要把候选结果当作已证实的线程竞态漏洞。

五、独立 Cppcheck 扫描
MIPS32 Linux：
D:\ThreadSafetyScanner\optional\RUN_CPPCHECK.cmd "D:\SDK\app" "D:\cppcheck_results" unix32

ARM64 Linux：
D:\ThreadSafetyScanner\optional\RUN_CPPCHECK.cmd "D:\SDK\app" "D:\cppcheck_results" unix64

六、无需 Python / ripgrep 的旧方案仍可运行：
D:\ThreadSafetyScanner\START_SCAN.cmd "D:\SDK\app" "D:\scan_ps"
此脚本是 PowerShell 5.1 版，支持基本的注释和字符串过滤。

注意：源代码目录与报告目录必须分开。建议先扫描 app/cli/mqtt/http 等模块，
不要立即扫描所有第三方库和完整内核，避免生成过多低价值候选项。
本工具包尚未在你的 Win10 1709 实机执行验证，请先使用 examples 自测。
