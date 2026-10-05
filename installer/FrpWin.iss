; ============================================================================
;  FrpWin 内网穿透套装 —— Inno Setup 安装脚本
;  编译：ISCC.exe /DStageDir=... /DOutputDir=... FrpWin.iss
; ============================================================================

#ifndef StageDir
  #define StageDir "..\staging"
#endif
#ifndef OutputDir
  #define OutputDir "..\output"
#endif

#define AppName        "FrpWin 内网穿透套装"
#define AppShortName   "FrpWin"
#define AppVersion     "1.0.0"
#define AppPublisher   "FrpWin"
#define AppURL         "https://github.com/Ryz-z/FRP-Windows-GUI2"
#define AppExeName     "FrpWin.exe"

[Setup]
AppId={{635A3243-448D-4BAF-86BB-658E70EFA2D3}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppURL}
AppSupportURL={#AppURL}
AppUpdatesURL={#AppURL}
VersionInfoVersion=1.0.0.0
VersionInfoCompany={#AppPublisher}
VersionInfoDescription={#AppName} 安装程序
VersionInfoProductName={#AppShortName}
VersionInfoProductVersion={#AppVersion}
DefaultDirName={autopf}\{#AppShortName}
DefaultGroupName={#AppShortName}
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
LicenseFile=
OutputDir={#OutputDir}
OutputBaseFilename=FrpWin-Setup-{#AppVersion}
SetupIconFile={#StageDir}\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=commandline
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
CloseApplications=no
RestartIfNeededByRun=no

[Languages]
Name: "chinese"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
chinese.CreateDesktopIcon=创建桌面快捷方式
chinese.OpenFirewall=在 Windows 防火墙中放行服务端端口 7000（TCP + UDP）
chinese.AdditionalTasks=附加任务：
chinese.LaunchApp=立即运行 FrpWin 管理器
chinese.FirewallStatus=正在配置 Windows 防火墙...
chinese.StoppingServices=正在停止已运行的 frp 服务...
chinese.ProgramFiles=程序文件
chinese.DataFiles=配置与日志（保留）
chinese.Readme=使用说明
chinese.Uninstall=卸载 FrpWin

english.CreateDesktopIcon=Create a desktop shortcut
english.OpenFirewall=Open server port 7000 (TCP + UDP) in Windows Firewall
english.AdditionalTasks=Additional tasks:
english.LaunchApp=Launch FrpWin Manager now
english.FirewallStatus=Configuring Windows Firewall...
english.StoppingServices=Stopping running frp services...
english.ProgramFiles=Program files
english.DataFiles=Configuration and logs (kept)
english.Readme=Read me
english.Uninstall=Uninstall FrpWin

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: checkedonce
Name: "firewall"; Description: "{cm:OpenFirewall}"; GroupDescription: "{cm:AdditionalTasks}"; Flags: unchecked

[Dirs]
; 数据目录对所有用户可写，这样普通用户也能在图形界面里保存配置
Name: "{commonappdata}\{#AppShortName}"; Permissions: users-modify
Name: "{commonappdata}\{#AppShortName}\logs"; Permissions: users-modify

[Files]
Source: "{#StageDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\frps.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\frpc.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#StageDir}\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme; Languages: chinese
Source: "{#StageDir}\README.en.txt"; DestDir: "{app}"; Flags: ignoreversion isreadme; Languages: english
; 示例配置只在不存在时放入，卸载时不删除，避免覆盖用户自己的配置
Source: "{#StageDir}\conf\frps.toml"; DestDir: "{commonappdata}\{#AppShortName}"; Flags: onlyifdoesntexist uninsneveruninstall
Source: "{#StageDir}\conf\frpc.toml"; DestDir: "{commonappdata}\{#AppShortName}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"; Comment: "frp 内网穿透图形管理器"; Tasks: desktopicon
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; IconFilename: "{app}\{#AppExeName}"
Name: "{group}\{cm:DataFiles}"; Filename: "{sys}\explorer.exe"; Parameters: """{commonappdata}\{#AppShortName}"""
Name: "{group}\{cm:Readme}"; Filename: "{app}\使用说明.txt"; Languages: chinese
Name: "{group}\{cm:Readme}"; Filename: "{app}\README.en.txt"; Languages: english
Name: "{group}\{cm:Uninstall}"; Filename: "{uninstallexe}"

[Run]
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""FrpWin frps 7000 TCP"" dir=in action=allow protocol=TCP localport=7000"; Flags: runhidden; Tasks: firewall; StatusMsg: "{cm:FirewallStatus}"
Filename: "{sys}\netsh.exe"; Parameters: "advfirewall firewall add rule name=""FrpWin frps 7000 UDP"" dir=in action=allow protocol=UDP localport=7000"; Flags: runhidden; Tasks: firewall; StatusMsg: "{cm:FirewallStatus}"
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchApp}"; Flags: nowait postinstall skipifsilent

[Code]
{ ---------------------------------------------------------------------------
  安装前后清理：如果 frp 正在以服务或普通进程运行，文件会被占用，
  必须先停止服务并结束进程，否则覆盖安装会失败。
  --------------------------------------------------------------------------- }
procedure StopFrpServices();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop FrpWinServer', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\sc.exe'), 'stop FrpWinClient', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(700);
end;

procedure KillFrpProcesses();
var
  ResultCode: Integer;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM FrpWin.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM frps.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM frpc.exe /T', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Sleep(300);
end;

procedure RemoveFirewallRules();
var
  ResultCode: Integer;
  Q: String;
begin
  Q := '"';
  Exec(ExpandConstant('{sys}\netsh.exe'),
       'advfirewall firewall delete rule name=' + Q + 'FrpWin frps 7000 TCP' + Q,
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Exec(ExpandConstant('{sys}\netsh.exe'),
       'advfirewall firewall delete rule name=' + Q + 'FrpWin frps 7000 UDP' + Q,
       '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  WizardForm.StatusLabel.Caption := ExpandConstant('{cm:StoppingServices}');
  StopFrpServices();
  KillFrpProcesses();
  Result := '';
end;

procedure WriteLanguageChoice();
var
  LangCode: String;
  LangFile: String;
begin
  { 把安装时选的语言写下来，FrpWin.exe 启动时会读它决定界面语言。
    第四版之前没有这一步，所以“安装时选英文、程序里还是中文”。 }
  if ActiveLanguage = 'english' then
    LangCode := 'en'
  else
    LangCode := 'zh';

  LangFile := ExpandConstant('{commonappdata}\{#AppShortName}\language.txt');
  if SaveStringToFile(LangFile, LangCode + #13#10, False) then
    Log('界面语言写入成功: ' + LangFile + ' = ' + LangCode)
  else
    Log('界面语言写入失败: ' + LangFile);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    { 安装结束后确保数据目录的可写权限（某些系统上 ProgramData 继承权限不一致） }
    WriteLanguageChoice();
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  ResultCode: Integer;
begin
  if CurUninstallStep = usUninstall then
  begin
    StopFrpServices();
    KillFrpProcesses();
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete FrpWinServer', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    Exec(ExpandConstant('{sys}\sc.exe'), 'delete FrpWinClient', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
    RemoveFirewallRules();
  end;
end;
