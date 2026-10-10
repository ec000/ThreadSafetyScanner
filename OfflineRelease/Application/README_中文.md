# ThreadSafetyScanner 完整离线安装候选包

**状态：已构建；待 Windows 10 专业版 1709 Build 16299.15 x64 实机验证。不是验收成功的 Release。**

## 安装与使用

1. 将 `ThreadSafetyScanner_Offline_Setup_x64.exe` 复制到目标电脑，不需要其他安装文件。
2. 双击，允许安装器的 UAC 提示，选择空安装目录。默认 `C:\Program Files\ThreadSafetyScanner\`。
3. 从桌面或开始菜单打开 ThreadSafetyScanner。正常启动和扫描不需要管理员权限。
4. 选择本地 C/C++ 源码目录，勾选 ripgrep、Cppcheck 或同时勾选两个。
5. 选择**源码的目标平台**：Windows x64 为 win64，MIPS32/Linux 等 32 位项目为 unix32，64 位 Linux 为 unix64。此选项与扫描电脑架构不同。
6. 点击开始扫描；可随时取消。按优先级、引擎和分类筛选，选中结果预览代码位置。
7. 用“导出报告”选择格式；ZIP 包含其他九种报告及说明。报告始终导出全部结果，界面筛选不会缩减导出。

安装器和应用均不下载文件、不配置 PATH、不调用 pip、不依赖系统 Python、Office、Visual Studio 或用户已有的扫描工具。安装器在临时目录验证 SHA256 后才复制文件，安装后再次校验；应用启动及扫描前也校验。

## 最低环境与实际内置内容

- 原生 x64 Windows 10，最低设计目标为专业版 1709 Build 16299.15。
- Windows 自带 .NET Framework 4.7.1（Release >= 461308）；安装器检测异常并提示修复系统组件，不访问网络。
- Windows 自带 UCRT、Win32 API 和中文字体。
- 建议至少 512 MiB 可用内存；大项目需要更多内存。安装器按实际解压载荷计算两倍文件大小加 100 MiB 的可用空间要求，并检查目标磁盘及临时目录所在磁盘。
- C# WinForms 应用，未使用第三方托管 DLL。
- `Tools\ripgrep\rg.exe`：ripgrep 13.0.0 官方 x64 MSVC 静态分发。
- `Tools\cppcheck\cppcheck.exe`：Cppcheck 2.10-tss1 x64 静态构建；附完整 cfg/platforms 数据。修改了 Windows UTF-8 路径接口和构建兼容性，附原始及修改后源码。
- 不部署 VC++ Runtime/Qt/Python/Rust/现代 .NET 运行时。C++ 运行时静态链接；系统 API 的目标版本可用性仍须实测。
- Semgrep：**不支持**，未打包，无 Build 16299.15 的完整离线兼容性证据。
- 28 条内置函数候选规则迁移自用户提供的 v2，许可证和第三方源码均位于 `Licenses\`。

工具实际版本还需在目标电脑运行 `--version` 与真实扫描确认。PE/DLL 检查和编译成功不能替代运行验证。

## 用户数据和卸载

数据默认写入 `%LOCALAPPDATA%\ThreadSafetyScanner\`：

- `rules.json`：用户规则，首次启动从内置规则自动复制。
- `History\`：每次任务的 JSON 历史，包含 completed/partial/failed/cancelled 状态。
- `Logs\`：运行和错误日志。
- `Reports\`：默认导出位置。
- `Preview\`：包内报告查看缓存；可以在应用关闭后删除。

源码目录只读。为防止写入源码树，源码不能包含用户配置目录，导出路径不能位于源码内。离线扫描拒绝 UNC、网络映射盘和源码根目录重解析点；跳过子目录符号链接。Cppcheck 的文件接口也拒绝 UNC/网络驱动器/重解析路径，防止头文件 include 指向网络；此类 include 会使任务明确失败。

通过“设置 → 应用”或开始菜单卸载。请先关闭应用和扫描进程。卸载只删除安装器管理的明确文件名，不递归删除安装目录；保留源码、用户报告、规则、历史及未知用户文件。重装会复用用户配置。已有非空安装目录不会被接管，升级前请先卸载。

安装日志：`%PROGRAMDATA%\ThreadSafetyScanner\Logs\install.log`。应用无法启动时查看当前用户 LocalAppData 中的错误日志。

## 报告格式

CSV、JSON、Markdown、HTML、TXT、XML、SARIF 2.1.0、XLSX、PDF、ZIP 均在本机生成。XLSX 采用 OOXML，PDF 使用本机 GDI+ 和系统中文字体逐页生成图像 PDF，不需要 Office 或在线服务。

应用内“打开导出报告”可查看文本格式、XLSX 表格、PDF 页面和 ZIP 内文件。HTML 也可用 Windows 自带浏览器打开；应用内按文本显示其内容。PDF 为图像页面，文字不可搜索/选择。CSV 使用 UTF-8 BOM，增加 Metadata/Finding 记录类型和任务状态字段，防止把失败任务的零命中误认为成功；以公式字符开头的单元格加前导单引号。

报告会保留诊断和任务状态；partial/failed/cancelled 均不等于扫描成功。综合扫描保留各引擎来源，按引擎、规则、位置去重，不会将不同引擎的发现强行混为一个结论。

## 能力边界

规则命中是人工审查候选，不证明发生了数据竞争。glibc/MSVC/lwIP 的实现、锁、线程上下文和结果指针生命周期必须结合工程复核。ripgrep 阶段过滤注释、常规字符串和 C++ raw string，但原型、函数定义、宏包装、函数指针及预处理条件可能误报或漏报；尚未完整处理 C/C++ 的所有词法边界，例如注释行续接。UTF-8 及带 BOM 源码是默认支持范围；无 BOM GBK 等编码应先在副本上转为 UTF-8。

默认跳过超过 24 MiB 的源码文件并把任务标为 partial，遇到读权限/工具错误也不会冒充成功。Cppcheck 未加载 SDK 的完整编译数据库和头文件路径时，结果需要额外人工解释；此版本不启用任何需要 Python 的 addons。扫描进程超过 60 分钟会失败，建议按模块拆分。

## 验证

将独立 QA 文件与完整安装器放在同一目录，恢复全新的 Windows 1709 快照，断开所有外部网卡，普通权限运行 `Validate-Windows1709.cmd`。脚本不改变 ExecutionPolicy，不需要开发工具；安装/卸载步骤会弹出 UAC，扫描仍由原普通用户执行。

QA 程序会核对 exact Build/UBR/版本、默认 .NET、无预装开发工具、网卡状态，执行安装、GUI 窗口启动、三个引擎模式、真实预期命中、十种导出结构、中文空格路径、历史、卸载数据保留及重新安装后再扫描。它仍不能代替人工检查所有报告显示效果和运行中取消等交互项。参见 `WINDOWS1709-CHECKLIST.zh-CN.md`、`TEST-REPORT.zh-CN.md`。

命令行接口（无需额外环境）：

```text
ThreadSafetyScanner.exe --verify-payload
ThreadSafetyScanner.exe --scan "D:\中文 SDK" --engines both --platform unix32 --output "D:\审查报告"
ThreadSafetyScanner.exe --self-test --output "D:\验证报告"
ThreadSafetyScanner.exe --validate-reports --directory "D:\审查报告" --output "D:\格式验证"
```

`--engines` 可选 rg、cppcheck、both。退出码 0 表示该自动化操作通过；1 表示启动/参数/导出错误；2 表示扫描或自测未完整通过。`--self-test` 的报告结构通过不代表人工打开与系统兼容性验收完成。
