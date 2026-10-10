# 修改记录

## 2026-10-10：完整离线安装要求升级

- 导入用户提供的 `Win10_1709_Offline_Thread_Safety_Kit_v2` 作为历史参考。
- 将全新 Windows 10 1709 Build 16299.15 x64 断网安装设为最高优先级。
- 明确禁止空 tools、下载说明、开发机 PATH 或未验证安装包替代完整交付。
- 建立独立 `OfflineRelease/` 收集目录。
- 已下载官方 ripgrep 13.0.0 x64、Cppcheck 2.10 x64 MSI 及对应 Cppcheck 源码，正在提取和审计；未宣称 1709 兼容性已通过。
- 开发机为 macOS ARM64，目前没有可用的 Windows 10 1709 测试环境。
- 创建公开 Git 仓库记录后续源码、构建、依赖和验证变化；不提交本机工具链缓存或凭据。

## 2026-10-10：C# 离线安装候选实现

- 新增 .NET Framework 4.7.1/x64 WinForms 应用，迁移 28 条函数规则；工具均使用应用私有路径。
- 实现 ripgrep JSON 候选定位、注释/字符串过滤、Cppcheck XML 深度检查、综合模式、规则管理、筛选、预览、取消和本地历史。
- 新增十种本地报告导出及无需 Office 的 XLSX/PDF/ZIP 查看器；明确区分 completed/partial/failed/cancelled。
- Cppcheck 2.10-tss1 采用 llvm-mingw 静态构建；修复 UTF-8/宽字符文件路径、libc++ allocator rebind 和交叉构建兼容性，附原始/修改后完整源码与许可。
- 收集 ripgrep 56 个锁定 Rust 组件许可证，核对 Cargo.lock checksum；补齐 LLVM/MinGW、simplecpp、tinyxml2、picojson、NSIS 许可及对应源码。
- 新增严格离线载荷门禁、SHA256 清单、PE/DLL 检查、NSIS 一键安装/卸载；未修改系统 PATH，用户数据保留。
- 新增独立 Windows x64 QA EXE/CMD，精确系统基线、实际三模式扫描/导出、卸载/重装测试，以及人工验收和被测 EXE 哈希绑定门禁。
- 完成安装器实际解包与逐文件校验。目标 Windows 1709 运行测试仍为“待实机验证”，没有创建验收成功的 Release。
