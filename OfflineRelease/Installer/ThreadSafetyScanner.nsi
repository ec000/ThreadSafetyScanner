Unicode true
!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "FileFunc.nsh"
!include "x64.nsh"
!ifndef PAYLOAD_INCLUDE
  !error "Build through scripts/build_release.py; verified payload include is required."
!endif
!include "${PAYLOAD_INCLUDE}"

Name "ThreadSafetyScanner 离线安装"
OutFile "${OUTPUT_FILE}"
InstallDir "$PROGRAMFILES64\ThreadSafetyScanner"
RequestExecutionLevel admin
ManifestSupportedOS Win10
SetCompressor /SOLID lzma
CRCCheck force
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "0.1.0.1"
VIAddVersionKey /LANG=2052 "ProductName" "ThreadSafetyScanner Offline"
VIAddVersionKey /LANG=2052 "FileDescription" "完整离线安装候选包；Windows 1709 实机验证待完成"
VIAddVersionKey /LANG=2052 "FileVersion" "0.1.0.1"
VIAddVersionKey /LANG=2052 "LegalCopyright" "ThreadSafetyScanner contributors"
!define MUI_ABORTWARNING
!define MUI_WELCOMEPAGE_TITLE "ThreadSafetyScanner 完整离线安装"
!define MUI_WELCOMEPAGE_TEXT "安装程序内置 GUI、ripgrep、Cppcheck、规则及运行依赖，不访问互联网。$\r$\n$\r$\n最低系统：Windows 10 1709 x64，.NET Framework 4.7.1。$\r$\n$\r$\n此构建为验收候选，目标 Build 16299.15 实机验证状态见随包测试报告。$\r$\n$\r$\n正常扫描无需管理员权限。"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_LICENSE "..\Licenses\GPL-3.0.txt"
!define MUI_PAGE_CUSTOMFUNCTION_LEAVE ValidateDirectory
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "SimpChinese"

Var InstallLog
Var CopyStarted

Function .onInit
  SetRegView 64
  SetShellVarContext all
  StrCpy $CopyStarted "0"
  CreateDirectory "$APPDATA\ThreadSafetyScanner\Logs"
  FileOpen $InstallLog "$APPDATA\ThreadSafetyScanner\Logs\install.log" w
  ${If} ${Errors}
    MessageBox MB_ICONSTOP "无法创建安装日志，请检查 ProgramData 目录权限。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  FileWrite $InstallLog "ThreadSafetyScanner 0.1.0 offline install started$\r$\n"
  ${IfNot} ${IsNativeAMD64}
    MessageBox MB_ICONSTOP "本安装包仅支持原生 x64 Windows（不支持 x86/ARM64）。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  ReadRegStr $0 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "CurrentBuildNumber"
  ${If} $0 < 16299
    MessageBox MB_ICONSTOP "最低系统要求 Windows 10 1709 Build 16299.15。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  ReadRegDWORD $1 HKLM "SOFTWARE\Microsoft\Windows NT\CurrentVersion" "UBR"
  ${If} $0 = 16299
  ${AndIf} $1 < 15
    MessageBox MB_ICONSTOP "Windows 16299 的最低修订号为 15。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  ReadRegDWORD $0 HKLM "SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full" "Release"
  ${If} $0 < 461308
    MessageBox MB_ICONSTOP "未检测到 Windows 自带的 .NET Framework 4.7.1。请修复系统 .NET 组件后重试。本安装器不会联网下载运行库。"
    SetErrorLevel 1
    Abort
  ${EndIf}
FunctionEnd

Function ValidateDirectory
  ; Reject a nonempty destination rather than taking ownership of user files.
  FindFirst $0 $1 "$INSTDIR\*"
  ${DoWhile} $1 != ""
    ${If} $1 != "."
    ${AndIf} $1 != ".."
      FindClose $0
      MessageBox MB_ICONSTOP "安装目录必须为空。已有本程序时请先卸载；源码和用户报告不会被删除。"
      Abort
    ${EndIf}
    FindNext $0 $1
  ${Loop}
  FindClose $0
  ${GetRoot} "$INSTDIR" $0
  System::Call 'kernel32::GetDiskFreeSpaceExW(w "$0\", *l.r1, *l, *l)i.r2'
  ${If} $2 = 0
    MessageBox MB_ICONSTOP "无法检查目标磁盘可用空间。请选择可访问的本地磁盘。"
    Abort
  ${EndIf}
  FileWrite $InstallLog "Disk space: available=$1 bytes; required=${REQUIRED_BYTES} bytes$\r$\n"
  System::Int64Op $1 < ${REQUIRED_BYTES}
  Pop $2
  ${If} $2 != 0
    MessageBox MB_ICONSTOP "目标磁盘空间不足。安装需要预留 ${REQUIRED_MB} MiB。"
    Abort
  ${EndIf}
  System::Call 'kernel32::GetDiskFreeSpaceExW(w "$TEMP", *l.r1, *l, *l)i.r2'
  ${If} $2 = 0
    MessageBox MB_ICONSTOP "无法检查系统临时目录所在磁盘的可用空间。"
    Abort
  ${EndIf}
  FileWrite $InstallLog "Disk space: available=$1 bytes; required=${REQUIRED_BYTES} bytes$\r$\n"
  System::Int64Op $1 < ${REQUIRED_BYTES}
  Pop $2
  ${If} $2 != 0
    MessageBox MB_ICONSTOP "系统临时目录所在磁盘空间不足。"
    Abort
  ${EndIf}
FunctionEnd

Section "完整离线安装" SEC_MAIN
  SectionIn RO
  Call ValidateDirectory
  InitPluginsDir
  !insertmacro ExtractPayload
  FileWrite $InstallLog "Payload extracted; verifying SHA256 and OS runtime$\r$\n"
  StrCpy $0 -1
  ExecWait '"$PLUGINSDIR\payload\ThreadSafetyScanner.exe" --verify-payload' $0
  ${If} $0 != 0
    FileWrite $InstallLog "FAILED: payload verification exit=$0$\r$\n"
    MessageBox MB_ICONSTOP "离线文件完整性或运行环境检查失败（代码 $0），安装已终止。日志：$APPDATA\ThreadSafetyScanner\Logs\install.log"
    SetErrorLevel 1
    Abort
  ${EndIf}
  StrCpy $CopyStarted "1"
  CreateDirectory "$INSTDIR"
  !insertmacro CopyPayload
  IfErrors 0 +4
    FileWrite $InstallLog "FAILED: copy payload$\r$\n"
    SetErrorLevel 1
    Abort
  StrCpy $0 -1
  ExecWait '"$INSTDIR\ThreadSafetyScanner.exe" --verify-payload' $0
  ${If} $0 != 0
    MessageBox MB_ICONSTOP "安装后 SHA256 校验失败，正在撤回受管理文件。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "DisplayName" "ThreadSafetyScanner"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "DisplayVersion" "0.1.0"
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "InstallLocation" "$INSTDIR"
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "NoModify" 1
  WriteRegDWORD HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner" "NoRepair" 1
  CreateDirectory "$SMPROGRAMS\ThreadSafetyScanner"
  CreateShortcut "$SMPROGRAMS\ThreadSafetyScanner\ThreadSafetyScanner.lnk" "$INSTDIR\ThreadSafetyScanner.exe"
  CreateShortcut "$SMPROGRAMS\ThreadSafetyScanner\卸载.lnk" "$INSTDIR\Uninstall.exe"
  CreateShortcut "$DESKTOP\ThreadSafetyScanner.lnk" "$INSTDIR\ThreadSafetyScanner.exe"
  FileWrite $InstallLog "SUCCESS: installed $INSTDIR; user initialization happens at first normal-user launch$\r$\n"
  FileClose $InstallLog
SectionEnd

Function .onInstFailed
  FileWrite $InstallLog "Installation failed; rollback managed files only$\r$\n"
  FileClose $InstallLog
  ${If} $CopyStarted = "1"
    !insertmacro DeletePayload
    Delete "$INSTDIR\Uninstall.exe"
    RMDir "$INSTDIR"
  ${EndIf}
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext all
  Push "$INSTDIR\ThreadSafetyScanner.exe"
  Call un.CheckUnlocked
  Push "$INSTDIR\Tools\ripgrep\rg.exe"
  Call un.CheckUnlocked
  Push "$INSTDIR\Tools\cppcheck\cppcheck.exe"
  Call un.CheckUnlocked
FunctionEnd

Function un.CheckUnlocked
  Pop $0
  IfFileExists "$0" 0 done
  System::Call 'kernel32::CreateFileW(w r0, i 0x80000000, i 0, p 0, i 3, i 0, p 0)p.r1'
  ${If} $1 = -1
    MessageBox MB_ICONSTOP "程序或扫描引擎正在运行。请先关闭 ThreadSafetyScanner，再重新卸载。"
    SetErrorLevel 1
    Abort
  ${EndIf}
  System::Call 'kernel32::CloseHandle(p r1)'
  done:
FunctionEnd

Section "Uninstall"
  ; Only explicit installer-owned file names are removed. No recursive deletion.
  !insertmacro DeletePayload
  Delete "$INSTDIR\Uninstall.exe"
  Delete "$DESKTOP\ThreadSafetyScanner.lnk"
  Delete "$SMPROGRAMS\ThreadSafetyScanner\ThreadSafetyScanner.lnk"
  Delete "$SMPROGRAMS\ThreadSafetyScanner\卸载.lnk"
  RMDir "$SMPROGRAMS\ThreadSafetyScanner"
  RMDir "$INSTDIR"
  DeleteRegKey HKLM "Software\Microsoft\Windows\CurrentVersion\Uninstall\ThreadSafetyScanner"
  DetailPrint "已卸载程序。保留所有用户源码、LocalAppData 配置和报告。"
SectionEnd
