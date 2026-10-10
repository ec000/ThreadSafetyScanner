Windows 10 1709 (OS Build 16299.15) — 完全离线 C/C++ 函数级线程安全扫描包
============================================================================
包版本：2026-10-10
目标系统：Windows 10 专业版 1709，内置 Windows PowerShell 5.1，优先 64 位

【非常重要：本包实际包含什么】
本压缩包是“可立即使用的离线扫描包”，不是“已包含厂商安装程序”的完整安装包。
已经包含：PowerShell 5.1 兼容扫描脚本、CMD 启动文件、Python 扫描脚本、Semgrep 规则、
Cppcheck 调用脚本、测试样例和工具下载清单。
没有包含：python-3.11.9-amd64.exe、rg.exe、Cppcheck MSI、Semgrep wheel。
原因：打包环境无法获取厂商官方二进制，不会把未下载的文件冒充已打包文件。

【1. 最简单的方法：无须安装任何软件】
1) 解压 ZIP 到 D:\ThreadSafetyScanner\ (或其他本地目录)。
2) 把 SDK 源码放到本地，例如 D:\SDK\ （不要先改动 SDK）。
3) 双击 START_SCAN.cmd；输入源码路径 D:\SDK\。
4) 扫描结束后，打开 D:\ThreadSafetyScanner\reports\findings.csv 和 summary.md。
   findings.csv 包含 priority / function / file / line / column / reason / suggestion / source_excerpt。
   Excel 可按 function、priority 筛选。报告使用 UTF-8 BOM，适合中文路径/表格。
5) 也可以在 cmd 下执行：
       D:\ThreadSafetyScanner\START_SCAN.cmd "D:\SDK" "D:\audit_results"

扫描不会写入源码目录，默认写到脚本所在目录的 reports 文件夹。
扫描不会联网、不会修改注册表、不需要 Python、管理员权限或 WSL。
如需只检查某个子模块，把源码路径换成 D:\SDK\app 或 D:\SDK\driver。

【2. 先自测工具是否正常】
用 cmd 运行：
       START_SCAN.cmd "D:\ThreadSafetyScanner\examples" "D:\ThreadSafetyScanner\selftest"
预期 findings.csv 中能找到 strtok、localtime、rand 的候选结果。
示例文件的注释和字符串中的 strtok(fake)、inet_ntoa(fake)、gethostbyname(...) 不应被当成调用。
请注意：此检查验证规则与报告生成，不等于软件在 Win10 1709 真机经过验证。

【3. 可选安装 Python 3.11.9（仅在需要扩展脚本时）】
官方 x64 完整安装包（在另一台可联网电脑下载后通过 U 盘带入）：
https://www.python.org/ftp/python/3.11.9/python-3.11.9-amd64.exe
安装时选择 Install Now，并勾选 Add Python to PATH；离线安装不要勾选下载调试符号。
本包主扫描器不使用 Python；安装失败不影响 START_SCAN.cmd。

【4. 可选安装 ripgrep】
官方 x64 ZIP：
https://github.com/BurntSushi/ripgrep/releases/download/14.1.1/ripgrep-14.1.1-x86_64-pc-windows-msvc.zip
在联网电脑下载后，将压缩包内部的 rg.exe 提取至 D:\ThreadSafetyScanner\tools\rg.exe。
使用 CHECK_ENV.cmd 检查环境。
满足 Python + rg.exe 时可运行：
       optional\RUN_PY_RG.cmd "D:\SDK" "D:\audit_rg"
该脚本由原 Python/ripgrep 方案演化而来，会输出 summary.md/findings.csv/findings.json。
旧版本 Windows 是否能运行请以目标机实际结果为准。

【5. 可选安装 Cppcheck】
官方 Windows 64 位 MSI：
https://sourceforge.net/projects/cppcheck/files/cppcheck/2.18/cppcheck-2.18.0-x64-Setup.msi/download
安装后运行（MIPS32 Linux）：
       optional\RUN_CPPCHECK.cmd "D:\SDK\app" "D:\audit_cppcheck" unix32
ARM64 Linux 则将最后的 unix32 改成 unix64。
Cppcheck 主要补充静态缺陷，并不能证明函数调用实际发生并发竞争。
MSI 与最新 VC++ 运行库兼容性须在 Build 16299.15 真机检查。

【6. Semgrep（高级可选，不推荐作为 1709 上的基础方案）】
Semgrep 新版具有原生 Windows 支持，但不等于已验证支持 Win10 1709 最初构建。
如已在其他兼容 Windows PC 用 pip download 准备好全部 wheels，可以离线安装：
       python -m pip install --no-index --find-links=D:\wheelhouse semgrep
确保 Python 版本和平台与依赖 wheel 匹配。
安装后运行 optional\RUN_SEMGREP.cmd "D:\SDK\app" "D:\audit_semgrep"
本包附自定义规则，仅供本地规则扫描。若无法安装或运行请直接跳过。

【7. 风险等级如何解释】
P1：优先人工审查。例如 strtok、lwIP ipaddr_ntoa。
P2：中等优先级。例如 inet_ntoa、localtime、strerror。
REVIEW：依赖使用场景。例如 rand、system、exit。
所有命中均只是“候选代码位置”，不能自动认定为数据竞争或安全漏洞。
特别注意 glibc 中 inet_ntoa、strerror、rand 的具体版本差异，readdir 的 DIR* 是否共享。
不能通过第一阶段确认真实竞态、锁覆盖、跨线程指针生命周期、lwIP Raw API 线程上下文。

【8. 旧 Windows / 老 SDK 注意事项】
- Windows 10 1709 Pro 已于 2019-04-09 停止安全维护；建议保持扫描机隔离。
- 尽量将源码和报告放到盘符较短的路径，如 D:\SDK，避免 Windows MAX_PATH 问题。
- 如果输入源码很大，先扫描 app / driver 等子目录。
- 默认逐文件读取，最多 24 MB，过大文件会跳过并写入 scan_errors.txt。
- 常规源码编码按 UTF-8 读取并自动处理 BOM；无 BOM 的 GBK 中文注释可能显示乱码，但 ASCII 函数名通常仍可检测。
- 过滤常规 C/C++ 注释和字符串；C++ raw string、宏包装、函数指针和函数定义可能误报漏报。
- 预处理器条件（#if 等）未执行，因此未启用的源码分支也会被扫描。
- 不要在包含秘密的源码目录生成公共可共享报告，结果含源码片段。

【9. 完整下载安装包的拷贝方法】
在其他可联网 PC 从 TOOLS_MANIFEST.csv 指示的官方地址下载所需文件。
核验 Windows 安装程序的数字签名（文件属性 -> 数字签名）；rg ZIP 检查官方 SHA256 文件。
将下载的 EXE/MSI/ZIP 放入 vendor_installers 文件夹，再手工整体压缩拷贝到离线机。
不要从第三方镜像站或未经校验的软件合集获得可执行文件。
