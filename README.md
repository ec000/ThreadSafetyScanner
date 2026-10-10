# ThreadSafetyScanner

面向全新、完全离线 Windows 10 专业版 1709（Build 16299.15，x64）的 C/C++ 线程安全候选扫描工具。

**当前状态：开发中，尚未完成完整安装包，待 Windows 10 1709 实机验证。没有可宣称验收成功的 Release。**

最高优先级是：新电脑不安装 Python、ripgrep、Cppcheck、Visual Studio，不配置 PATH，断网后通过一个完整安装程序完成安装、扫描及报告导出。

## 目标交付

- `ThreadSafetyScanner_Offline_Setup_x64.exe`，包含 GUI、两个扫描引擎、规则、许可证和全部必要依赖。
- C# WinForms，目标 .NET Framework 4.7.1；使用应用私有工具路径。
- ripgrep / Cppcheck / 综合扫描，规则管理、预览、筛选、取消和历史记录。
- CSV、JSON、Markdown、HTML、TXT、XML、SARIF、XLSX、PDF、ZIP 本地导出，无需 Office。
- 构建脚本、离线依赖清单、SHA256、中文说明和可审计测试报告。

## 目录

`Win10_1709_Offline_Thread_Safety_Kit_v2/` 是用户提供的旧版参考文件，保持原样。旧版是脚本工具包，未包含厂商二进制，不能作为新版完整离线安装包交付。

`src/`、`scripts/`、`tests/`、`docs/` 是新版源码、构建、验证及文档目录。

`OfflineRelease/` 是唯一安装器输入目录，按 Application、Tools、Runtime、Rules、Licenses、Manifest、Installer 收集。下载缓存和本机编译器不提交到 Git；依赖的版本、来源和哈希必须记录。

## 发布条件

缺少任何必需文件、许可证、运行库或哈希校验失败时必须阻止构建。即使生成 EXE，在相同 Build 16299.15 干净断网系统完成真实扫描与安装/卸载测试之前，也只能标记为“待实机验证”，不能发布为验收成功的 Release。较新 Windows、macOS 上的测试不能替代目标系统验证。

详见 [修改记录](CHANGELOG.md)、[离线要求](docs/OFFLINE_REQUIREMENTS.zh-CN.md)。
