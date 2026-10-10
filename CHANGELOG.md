# 修改记录

## 2026-10-10：完整离线安装要求升级

- 导入用户提供的 `Win10_1709_Offline_Thread_Safety_Kit_v2` 作为历史参考。
- 将全新 Windows 10 1709 Build 16299.15 x64 断网安装设为最高优先级。
- 明确禁止空 tools、下载说明、开发机 PATH 或未验证安装包替代完整交付。
- 建立独立 `OfflineRelease/` 收集目录。
- 已下载官方 ripgrep 13.0.0 x64、Cppcheck 2.10 x64 MSI 及对应 Cppcheck 源码，正在提取和审计；未宣称 1709 兼容性已通过。
- 开发机为 macOS ARM64，目前没有可用的 Windows 10 1709 测试环境。
- 创建公开 Git 仓库记录后续源码、构建、依赖和验证变化；不提交本机工具链缓存或凭据。
