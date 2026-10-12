# ThreadSafetyScanner

面向全新、完全离线 Windows 10 专业版 1709（Build 16299.15，x64）的 C/C++ 线程安全候选扫描工具。

**当前状态：已生成包含实际依赖的完整离线安装候选包，待 Windows 10 1709 实机验证。没有可宣称验收成功的 Release。**

最高优先级是：新电脑不安装 Python、ripgrep、Cppcheck、Visual Studio，不配置 PATH，断网后通过一个完整安装程序完成安装、扫描及报告导出。

## 目标交付

- `ThreadSafetyScanner_Offline_Setup_x64.exe`，包含 GUI、两个扫描引擎、规则、许可证和全部必要依赖。
- C# WinForms，目标 .NET Framework 4.7.1；使用应用私有工具路径。
- ripgrep / Cppcheck / 综合扫描，规则管理、预览、筛选、取消和历史记录。
- 支持本地目录、SMB 映射盘和 UNC 共享；10,000 个源码文件或 100,000 个目录项时提示继续/取消。
- CSV、JSON、Markdown、HTML、TXT、XML、SARIF、XLSX、PDF、ZIP 本地导出，无需 Office。
- 构建脚本、离线依赖清单、SHA256、中文说明和可审计测试报告。

## 目录

`Win10_1709_Offline_Thread_Safety_Kit_v2/` 是用户提供的旧版参考文件，保持原样。旧版是脚本工具包，未包含厂商二进制，不能作为新版完整离线安装包交付。

`src/`、`scripts/`、`tests/`、`docs/` 是新版源码、构建、验证及文档目录。

`OfflineRelease/` 是唯一安装器输入目录，按 Application、Tools、Runtime、Rules、Licenses、Manifest、Installer 收集。下载缓存和本机编译器不提交到 Git；依赖的版本、来源和哈希必须记录。

## 发布条件

缺少任何必需文件、许可证、运行库或哈希校验失败时必须阻止构建。即使生成 EXE，在相同 Build 16299.15 干净断网系统完成真实扫描与安装/卸载测试之前，也只能标记为“待实机验证”，不能发布为验收成功的 Release。较新 Windows、macOS 上的测试不能替代目标系统验证。

详见 [修改记录](CHANGELOG.md)、[离线要求](docs/OFFLINE_REQUIREMENTS.zh-CN.md)。

## 当前实现与验证

已实现 C# WinForms GUI、三个扫描模式、规则管理、代码位置预览、分类筛选、取消、历史以及十种本地报告导出。内置 ripgrep 13.0.0 与 Cppcheck 2.10-tss3 静态构建。支持已连接的 SMB 映射盘/UNC 共享，并对大目录提示；SMB 模式需要局域网共享可达，本地模式仍可完全断网运行。安装器由 NSIS 3.10 编译，只有明确收集并审计的 OfflineRelease 文件会进入安装器。

C# 和 Cppcheck 已完成交叉编译；发布门禁自动测试通过，并已从实际安装器解包核对内置 SHA256。尚无 Windows 1709 运行测试证据，功能和系统兼容性均不能以编译成功替代实测。

- [中文安装和使用说明](docs/README_中文.md)
- [完整构建流程](docs/BUILDING.zh-CN.md)
- [离线依赖及构建输入锁定清单](vendor/dependencies.lock.json)
- [实际载荷依赖清单](OfflineRelease/Manifest/dependencies.json)
- [测试报告](docs/TEST-REPORT.zh-CN.md)
- [Windows 1709 验收清单](docs/WINDOWS1709-CHECKLIST.zh-CN.md)

## 直接下载安装包（无需编译）

`git clone` 和 GitHub 的 Download ZIP 只获取源码，不含生成的 EXE。普通用户请从预发布附件下载完整安装包，无需安装编译工具或 Python：

- [完整离线安装程序 ThreadSafetyScanner_Offline_Setup_x64.exe](https://github.com/ec000/ThreadSafetyScanner/releases/download/v0.1.0-candidate.4/ThreadSafetyScanner_Offline_Setup_x64.exe)
- [全部交付文件 ThreadSafetyScanner_Offline_Delivery_x64.zip](https://github.com/ec000/ThreadSafetyScanner/releases/download/v0.1.0-candidate.4/ThreadSafetyScanner_Offline_Delivery_x64.zip)（安装包、源码、构建脚本、许可证、校验清单、中文说明及验证程序）
- [候选版本说明和校验文件](https://github.com/ec000/ThreadSafetyScanner/releases/tag/v0.1.0-candidate.4)

candidate.4 修复 Cppcheck 在规范化 SMB/UNC 文件名时因父目录元数据探测被拦截而终止的问题；保留 candidate.3 的 SMB 扫描、大目录提示及按钮换行，以及 candidate.2 的磁盘空间误判修复。实际大小和 SHA256 见预发布页面附件。

**这是待 Windows 10 Pro 1709 Build 16299.15 实机验证的预发布候选，并非已通过最终验收的正式版。** 必需工具二进制已内置，但目标系统上的安装、真实扫描及报告导出仍待验证。

将 EXE 复制到目标电脑，双击并完成安装向导，然后使用桌面快捷方式。目标电脑不需要自行编译或下载依赖。

开发者需要修改并重建时，参见[构建说明](docs/BUILDING.zh-CN.md)。下载缓存和生成文件不提交到 Git；联网只发生在构建电脑预收集依赖阶段。

## 后续版本自动上传

已配置 [GitHub Actions 离线构建发布流程](.github/workflows/offline-release.yml)。后续推送 `v*` 版本标签，会自动收集并校验依赖、编译、执行门禁测试、解包验证安装器、生成完整交付 ZIP，再上传 GitHub Release 附件。附件上传完整前保持草稿，成功后以 **Pre-release** 公开；失败不会发布为成功版本，也不覆盖已有公开附件。

例如发布新的候选：`git tag v0.1.0-candidate.4`，然后 `git push origin v0.1.0-candidate.4`。仅推送 main 不会产生新 Release。也可在 Actions 页面手动运行构建检查，手动运行只保存构建产物，不发布新版本。

Windows 1709 实机验收仍是独立步骤；自动构建成功不会将候选提升为已验证正式版。详见[自动发布说明](docs/AUTOMATIC-RELEASE.zh-CN.md)。
