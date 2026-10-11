# 自动上传离线安装包

工作流：`.github/workflows/offline-release.yml`。使用 GitHub 官方标准 macOS ARM64 runner（macos-15）运行现有交叉编译流程，Python 3.12 是构建机工具，不是目标 Windows 依赖。

推送 `v*` 标签后自动执行：固定 SHA256 依赖收集、私有工具链准备、Cppcheck/C# 编译、发布门禁测试、完整 NSIS 安装包生成、实际安装器解包 SHA256 核对、完整交付 ZIP 生成及校验、Release 附件上传。上传时为草稿，全部完成后才公开为预发布。

使用仓库内置 GITHUB_TOKEN，不需要配置个人访问令牌。构建失败会停止发布，并保留可用诊断日志；既有公开版本不会被覆盖。上传失败时草稿保留，可检查后删除草稿并重新运行。

例：`git tag v0.1.0-candidate.3`，然后 `git push origin v0.1.0-candidate.3`。版本标签需要指向包含工作流的新提交，不能复用旧标签。main 普通提交不触发发布。Actions 页的 workflow_dispatch 可用于只构建、测试和保存产物。

附件：完整离线 Setup EXE、全部交付 ZIP、交付文件 SHA256 清单、ZIP SHA256 清单。CI 产物额外保留 30 天。

当前自动流程始终保留预发布状态，不生成 Windows 1709 实机测试证据，也不绕过 promote_release.py 验收门禁。目标 Build 16299.15 干净断网环境真实安装、扫描、导出、卸载重装与人工验证仍必须执行。

官方 runner 说明：https://docs.github.com/en/actions/reference/runners/github-hosted-runners
