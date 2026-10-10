# 构建与依赖收集

目标电脑只运行完整安装器和应用。以下要求仅适用于**构建电脑**，不转嫁给最终用户。

目前实际跑通的构建平台为 macOS ARM64，Python 3.14、Apple clang 21、CMake、make。脚本使用 Python 3.12+ 标准库；未调用 pip。C# 编译器、.NET 参考程序集、Windows 交叉工具链及 NSIS 都位于工作区私有 build/toolchain，不安装为目标机依赖。

## 从公开仓库构建

```text
git clone https://github.com/ec000/ThreadSafetyScanner.git
cd ThreadSafetyScanner
python3 scripts/fetch_dependencies.py
python3 scripts/bootstrap_build_tools.py
python3 scripts/build_all.py
```

第一步依赖获取仅在联网构建电脑执行；按 vendor/dependencies.lock.json 的固定官方 URL 和 SHA256 验证，不使用 latest 地址，不接受哈希变化。可在联网电脑预先填充 vendor/downloads，然后使用 `--verify-only` 在离线构建电脑验证。Rust 许可证收集使用 ripgrep 官方 Cargo.lock 中 56 个 crate 的精确 checksum；预填充 vendor/downloads/rust-crates 后也无需联网。

NSIS 私有编译器由 3.10 官方源代码构建，使用同版本官方 Windows 模板及插件；Windows 安装器模板针对支持 Win10 的 Unicode 版本，实际 Build 16299.15 安装运行仍须实测。CPPCheck 使用固定 llvm-mingw 20231128（UCRT）交叉编译，Win32 目标宏为 0x0601，静态链接 C++ 库，剥离调试符号。`build_cppcheck.py` 会在副本中应用明确的 UTF-8 路径和构建兼容修改；原始源码不修改。

## 分步构建

```text
python3 scripts/build_cppcheck.py --toolchain build/toolchain/llvm-mingw-20231128-ucrt-macos-universal
python3 scripts/build_application.py --dotnet build/toolchain/dotnet/dotnet --csc build/toolchain/roslyn/tasks/netcore/bincore/csc.dll --references build/toolchain/net471-reference/build/.NETFramework/v4.7.1
python3 scripts/build_validation.py --dotnet build/toolchain/dotnet/dotnet --csc build/toolchain/roslyn/tasks/netcore/bincore/csc.dll --references build/toolchain/net471-reference/build/.NETFramework/v4.7.1
python3 scripts/collect_rust_licenses.py
python3 scripts/collect_payload_metadata.py
python3 scripts/build_release.py --check-only
python3 -m unittest discover -s tests -p "test_*.py" -v
python3 scripts/build_release.py --makensis build/toolchain/nsis-dist/nsis-3.10/makensis
```

build_all.py 还会将中文说明和测试清单复制进 Application。分步执行时请自行完成该文档复制步骤。

应用 csproj 可在装有 .NET Framework 4.7.1 targeting pack 的 Windows 构建电脑上用 MSBuild 编译，但该替代构建路径本轮未实测。无需在目标电脑编译任何源码。

## 输入及输出约束

安装器唯一输入为 OfflineRelease/Application、Tools、Runtime、Rules、Licenses、Manifest。Application 文件安装到应用根目录，其他目录保持相对结构；Installer 只存构建脚本，不是运行依赖。

build_release.py 不下载文件、不调用 PATH 中的 rg/cppcheck、不寻找开发机 DLL，必须显式指定 makensis。缺必需文件/许可证/对应源码/规则时列出缺失项并终止；校验工具哈希、PE 架构、普通及延迟导入 DLL 闭包，发现额外运行库缺失时阻断。编译器警告视为错误。载荷 SHA256 和明确卸载文件列表由收集目录生成。

PE 检查仅确认依赖分类，不证明系统导出函数在 1709 上存在；只有目标电脑实际启动并完成真实扫描才可验收。

产物位于 `artifacts/candidate/`，安装器文件名为 `ThreadSafetyScanner_Offline_Setup_x64.exe`。BUILD-REPORT.json、SHA256SUMS.txt 记录实际文件大小及 SHA256。构建总是标记 candidate，不自动发布 GitHub Release。不同构建可能因源码压缩包/文件时间而有不同 SHA256；测试证据只适用于实际被测的那一个文件。

可用明确的 7-Zip 构建工具检查已编译安装器内部实际内容：

```text
python3 scripts/verify_embedded_payload.py --sevenzip <7zz的完整路径> --installer artifacts/candidate/ThreadSafetyScanner_Offline_Setup_x64.exe
```

这会解包实际 EXE、逐文件校验其内置清单，不是仅检查收集目录。其结果仍不替代 Windows 1709 运行验证。

## 源码、许可证及修改后重编译

Licenses 内附新应用源码、Cppcheck 原始源码、修改后对应源码、NSIS 源码及所有组件许可。静态链接的 simplecpp 可与 Cppcheck 完整对应源码一起重编译；LLVM/MinGW 所用许可文本同时提供。可依照脚本修改并重编译，再重建 SHA256 清单；完整性校验不限制 GPL/LGPL 允许的改动、替换与调试。

Cppcheck MSI 只用于审查原厂分发，**未作为运行依赖内置**；最终使用自主构建的静态 CLI，不打包 Qt GUI、Python addons 或微软可再分发 DLL。
