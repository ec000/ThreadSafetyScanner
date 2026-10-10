# Windows 10 1709 干净断网验收清单

当前执行状态：**待实机验证**。不能以 Windows 10 22H2、Windows 11、Wine 或 macOS 结果替代。

## 基线准备

- 使用合法取得的 Windows 10 专业版 1709 x64 原始安装介质，安装后确认版本为 16299.15，UBR=15。
- 创建干净快照；不安装 Python、ripgrep、Cppcheck、Semgrep、Visual Studio、Office、较新 .NET 或 VC++ Runtime。
- 记录 `winver` 截图、系统属性、已装应用列表、.NET Release DWORD 与 PATH。确认未安装过 ThreadSafetyScanner。
- 断开虚拟机所有外部网卡，禁用主机共享网络；关闭可能自动联网的集成功能。保留网络监测证据。
- 通过离线介质复制完整 Setup、SHA256 清单、独立验证 EXE/config/CMD；核对安装器 SHA256。
- 普通非提升权限运行 QA。QA 严格检查上述基线，检查失败应恢复快照，不得修改证据为通过。

## 自动测试

运行 `Validate-Windows1709.cmd`，仅安装器与卸载器确认 UAC。保存程序打印的证据目录。

| 项目 | 自动断言 | 当前 |
|---|---|---|
| 系统 | 16299.15、Professional、x64、.NET Release 461308 | 待执行 |
| 清洁性 | 无相关卸载项、PATH 中无预装工具、非管理员 QA | 待执行 |
| 断网 | 外部网卡全部断开，开始和结束状态一致 | 待执行 |
| 安装 | 静默完整安装并校验实际文件 SHA256 | 待执行 |
| GUI | 普通用户能创建真实 WinForms 主窗口 | 待执行 |
| ripgrep | 实际扫描，只命中一次 strtok，过滤注释和字符串 | 待执行 |
| Cppcheck | 实际扫描，检测 nullPointer | 待执行 |
| 综合扫描 | 两个来源均有预期发现，任务 completed | 待执行 |
| 中文路径 | 源码目录和文件名含中文与空格 | 待执行 |
| 报告 | 9 个独立文件和 ZIP，JSON/XML/OOXML/SARIF/PDF/ZIP 结构可读取 | 待执行 |
| 历史 | 写入普通用户 LocalAppData | 待执行 |
| 卸载 | 管理文件删除，源码哨兵、报告哈希和历史保留 | 待执行 |
| 重装 | 同一离线包重装后重新完成扫描及导出 | 待执行 |

## 必须人工执行并签名的项目

1. 交互安装：默认与自选路径各测一次，桌面/开始菜单快捷方式、UAC、磁盘空间不足和清晰错误提示。
2. 普通用户 GUI：仅 ripgrep、仅 Cppcheck、综合模式分别扫描真实示例；验证分类和优先级筛选、行号及代码预览。
3. 规则管理：禁用 strtok 后只对 ripgrep 生效，保存、恢复内置规则、非法/重复函数名的错误提示；历史可打开。
4. **运行中取消**：对足够大的 SDK 分别运行两引擎，在进程运行时取消；确认界面保持响应、子进程退出、状态为 cancelled、历史不标为 completed；再发起新任务。
5. 逐一打开 CSV、JSON、Markdown、HTML、TXT、XML、SARIF、XLSX、PDF、ZIP。无需安装 Office；使用应用查看器或系统组件。检查中文文字、长文件名、特殊字符、分页以及全部结果/状态/诊断，PDF 每页无截断，XLSX 内容与 JSON 一致，ZIP 含所有九种格式。
6. 卸载重装：保留已有 SDK、报告和配置。在安装目录额外放一个用户文件，确认卸载不会删除；应用在运行时卸载应拒绝并说明原因。
7. 从开始安装到扫描、导出、卸载重装全过程观察网络；确认没有任何下载、更新或遥测请求。仅网卡状态不能证明无网络尝试。

将 `manual-checklist.json` 中 operatorName、status 与 checks 按真实结果填写，附截图或屏幕录像及测试环境说明。失败或未执行的项目保留 false，不得省略。

## 发布门禁

构建机执行：

```text
python3 scripts/promote_release.py --candidate artifacts/candidate --evidence <实际证据目录>/evidence.json --manual <实际证据目录>/manual-checklist.json
```

脚本要求与实际被测安装器完全一致的 SHA256、精确系统基线、自动测试和人工签名全部通过。它不生成或上传 GitHub Release。测试证据不是由源码单元测试伪造，单元测试中的模拟数据仅用于验证“发布阻断逻辑”。
