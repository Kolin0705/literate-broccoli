# 屏译 Lite

Windows 11 游戏选区翻译工具。框选字幕或对话区域，使用快捷键将英语翻译为简体中文。Windows 自带 OCR 在本机识别，百度翻译负责文本翻译，无需访问 Google、OpenAI 或境外模型服务。

## 开始使用

1. 解压 `ScreenTranslator-win-x64-portable.zip`，运行 `ScreenTranslator.exe`。便携包包含 .NET 运行库，无需安装 Python 或 .NET；适用于 Intel / AMD 的 Windows 11 电脑。
2. 在[百度翻译开放平台](https://fanyi-api.baidu.com/)注册并开通 **通用文本翻译 API**。打开程序「设置」，填写平台提供的 APP ID 和密钥。额度、实名认证要求和费用以百度平台为准；本程序不提供共享密钥。
3. 如果提示缺少英语 OCR，打开程序「设置 → 打开 Windows 语言设置」，添加 **英语（美国）** 并安装基本输入 / OCR 语言功能，然后重启程序。无需把 Windows 显示语言改成英语。
4. 将游戏设为 **无边框窗口** 或窗口模式，按 **Ctrl+Alt+R**，拖动鼠标框选字幕区域。松开鼠标确认；Esc / 右键取消。
5. 按 **Ctrl+Alt+T** 翻译。把译文窗口移到字幕区域以外，勾选「窗口置顶」即可边玩边看。

| 操作 | 快捷键 |
| --- | --- |
| 重新框选 | Ctrl+Alt+R |
| 翻译一次 | Ctrl+Alt+T |
| 开启 / 暂停自动翻译 | Ctrl+Alt+P |

自动模式默认每 3 秒识别一次，可调整为 2–60 秒；同一段原文在本次运行中会使用缓存，最多保留 32 条。画面文字持续变化会消耗 API 额度。发生网络、鉴权或额度错误时自动暂停，不会不停重试。每次启动默认关闭自动模式。关闭主窗口会退出程序；最小化时仍可使用热键和托盘菜单。

## 轻量与隐私

- 不捆绑大型 OCR 模型，使用系统英语 OCR；空闲时不截图，手动模式按需识别。
- 截图只存在内存中，不保存、不上传；只将识别文字通过 HTTPS POST 发到 `fanyi-api.baidu.com`。API 签名按百度协议生成，密钥本身不发送。
- APP ID、选区等配置保存在 `%LOCALAPPDATA%\ScreenTranslator\settings.json`。密钥通过 Windows DPAPI 按当前用户加密；更换电脑或用户后需重新填写。删除此文件即可重置设置。
- 不记录识别历史或网络日志，不注入游戏进程。剪贴板只在点击「复制译文」时写入。
- 另提供 `ScreenTranslator-win-x64-lite.zip`，适合已安装 **.NET 8 Desktop Runtime x64** 的电脑。它体积更小，但不能替代运行库。运行库可从[微软 .NET 8 页面](https://dotnet.microsoft.com/zh-cn/download/dotnet/8.0)获取。

## 常见问题

**框选后是黑屏、译文窗口无法覆盖游戏：** 独占全屏、受保护画面或部分反作弊环境可能不允许普通屏幕捕获。先切换为无边框窗口；本工具未使用注入或绕过机制。暂不保证所有游戏可用。

**识别不准确：** 尽量只框选字幕，调大游戏字幕字号。强烈描边、透明背景、艺术字体及快速滚动会降低识别准确率。移动游戏窗口、调整分辨率或改变显示器布局后需要重新框选。程序按物理像素保存位置，不跟踪某个游戏窗口。

**屏译窗口短暂闪烁：** 当窗口遮住所选区域时，程序会短暂隐藏自身再截图。把译文窗口移出选区即可避免；截图后恢复窗口不会主动抢焦点。

**没有英语 OCR：** 部分中文精简系统没有安装相应可选功能。可在管理员 PowerShell 中先检查：

```powershell
Get-WindowsCapability -Online -Name 'Language.OCR*'
```

若 `Language.OCR~~~en-US~0.0.1.0` 显示 `NotPresent`，通过 Windows 设置安装，或运行：

```powershell
Add-WindowsCapability -Online -Name 'Language.OCR~~~en-US~0.0.1.0'
```

安装需要能访问 Windows Update；如果电脑由组织管理，请使用组织提供的可选功能源。组件已安装但仍初始化失败时，需要排查当前 Windows 版本的桌面 OCR 兼容性。

**快捷键不起作用：** 程序会提示冲突，仍可点击窗口按钮。先检查是否被游戏或其他软件占用。本程序无需管理员权限。

**翻译服务报错：** 检查是否开通「通用文本翻译」、APP ID 与密钥是否匹配、余额 / 额度及 IP 白名单。状态栏显示中文原因，悬停可查看完整文字。没有密钥时不会发起翻译请求。

发行文件暂未进行商业代码签名。请保留来源与 SHA-256 校验信息，不要关闭系统防护来运行不可信副本。

## 构建与测试

需要 .NET 8 SDK。仓库由 WinForms 桌面程序、独立翻译核心和核心测试组成，无第三方运行时 NuGet 库；Windows SDK 引用由目标框架提供。

```powershell
dotnet build ScreenTranslator.sln -c Release
dotnet test tests/ScreenTranslator.Core.Tests/ScreenTranslator.Core.Tests.csproj -c Release
powershell -NoProfile -File scripts/publish.ps1
```

发布脚本在 `artifacts/` 下生成便携包、精简包和 `SHA256SUMS.txt`，不会包含本机配置或 API 密钥。GitHub Actions 提供 Windows 构建与测试，可在 Actions 中运行 `Build Windows packages` 获取构建产物。

Linux 也可以交叉编译，不能运行 WinForms、系统 OCR 和屏幕捕获。等效发布命令：

```bash
dotnet publish src/ScreenTranslator/ScreenTranslator.csproj -c Release -r win-x64 \
  --self-contained true -p:PublishSingleFile=true \
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true \
  -p:DebugType=None -p:DebugSymbols=false -o artifacts/ScreenTranslator-win-x64-portable
```

核心测试使用模拟 HTTP 响应，覆盖签名、Unicode、响应解析、API 错误、正文读取超时、用户取消、文本整理和缓存淘汰，不发送收费请求。当前已通过交叉编译及这些测试；**尚未验证真实 Windows 11 的 OCR、混合 DPI 多屏、游戏截图、热键与真实百度账户请求**，请按 [Windows 验收清单](docs/windows-validation.md) 检查。

### OCR 支持范围

Windows.Media.Ocr 被已有开源工具用于免安装桌面程序；但[微软文档](https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr)仍将正式桌面支持范围限定为具有包身份的应用。本项目目前提供普通 EXE，包含 OCR 不可用检测，不能保证每个 Windows 系统均能运行。若目标机器始终无法初始化，需要另行采用 MSIX 打包或替代 OCR 后端；当前版本没有自动降级 OCR。
