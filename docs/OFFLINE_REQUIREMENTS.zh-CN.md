# 离线部署验收约束

目标：Windows 10 专业版 1709、Build 16299.15、x64，仅默认系统组件，互联网全部断开。

1. 单个完整离线 EXE；安装过程不得访问网络。
2. 内置实际 `tools/ripgrep/rg.exe`、`tools/cppcheck/cppcheck.exe`、Cppcheck 必需数据和非系统 DLL。
3. C# WinForms/.NET Framework 4.7.1，不依赖系统 Python、Office、Visual Studio 或 PATH。安装器检查 .NET Release >= 461308，并报告异常。
4. Semgrep 无目标系统兼容性与离线分发证据时标记不支持，不影响必需引擎。
5. 可选安装路径，默认 Program Files；用户配置、报告、缓存写入 LocalAppData；扫描不需要管理员。
6. 启动前验证 SHA256；操作系统、x64、磁盘空间、工具缺失或损坏需清晰提示。
7. ripgrep、Cppcheck、综合扫描均须执行真实任务；错误不能冒充零命中成功。
8. 提供本地规则管理、代码预览、风险筛选、取消、历史与十种离线导出。
9. 卸载仅删除受管理程序文件；保留用户源码、报告和配置，不修改系统 PATH。
10. 同 Build 干净断网系统测试 GUI、三个扫描模式、全部报告、中文空格路径、安装、卸载和重装。

新增可选 SMB 扫描模式：复用当前用户已经连接的映射盘/UNC，共享访问只读且限制在所选源码范围内。此模式需要局域网可达，独立于上述完全断网的本地扫描验收；不增加互联网、下载或系统扫描工具依赖。真实 SMB 读取、网络中断及大目录确认需另行测试。

发布门禁须同时检查：二进制及数据完整、PE 架构与 DLL 闭包、许可证与对应源码、payload 哈希、安装器编译结果、目标系统实际证据。

当前目标机测试状态：**待实机验证**。

官方资料（用于设计依据，不等同于本项目实测）：

- [.NET Framework 版本及系统对应关系](https://learn.microsoft.com/en-us/dotnet/framework/install/versions-and-dependencies)
- [ripgrep 13.0.0 官方发布](https://github.com/BurntSushi/ripgrep/releases/tag/13.0.0)
- [Cppcheck 2.10 官方分发](https://sourceforge.net/projects/cppcheck/files/cppcheck/2.10/)
- [NSIS 跨平台构建文档](https://nsis.sourceforge.io/Docs/AppendixG.html)
