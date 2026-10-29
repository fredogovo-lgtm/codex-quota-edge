# Codex Usage Widget

[English](README.en.md) · Windows 10/11 · .NET 8 · MIT

一个用于查看 Codex 额度、重置时间和 Token 趋势的 Windows 桌面悬浮窗。

开发者：[九应](https://github.com/qeretycs-tech)

![Codex Usage Widget 默认界面](docs/screenshot.png)

## 功能

- 显示 Codex 额度剩余/已用百分比、重置时间及可用重置次数。
- 显示累计、最近一天、近 7 天和最近 14 天 Token 趋势。
- 每分钟自动刷新，也可手动刷新。
- 支持拖动、四边与四角缩放、始终置顶和贴边自动隐藏。
- 鼠标移入桌面边缘小标志后自动展开，也可单击展开。
- 独立齿轮设置窗口可拖到桌面任意位置。
- 统一调节窗口底板、数据卡片、操作按钮的透明度与毛玻璃质感。
- 支持原始字节导入 PNG、JPEG、BMP、GIF、TIFF 图片及 MP4、M4V、MOV、WMV、AVI、MPG、MPEG 视频。
- 图片不压缩、不转码，按原生像素解码并保持宽高比；视频静音循环，窗口隐藏或最小化时暂停。
- 底部背景菜单可随时恢复程序自带的“默认背景”。
- 一键隐私模式和全局快捷键 `Ctrl+Alt+H`，可立即隐藏私人背景但保留额度数据。
- 可选失焦隐藏；Windows 截图与屏幕共享保护默认开启。
- 自动查找 `codex.exe`，也可在齿轮设置中手动选择。

![默认背景选择菜单](docs/background-menu.png)

## 下载与安装

1. 打开 [GitHub Releases](https://github.com/qeretycs-tech/codex-usage-widget/releases)。
2. 普通 Windows 电脑下载 `CodexUsageWidget-win-x64.zip`；Windows on ARM 下载 `win-arm64`。
3. 解压后直接运行 `CodexUsageWidget.exe`，或双击 `Install.cmd` 安装到当前用户。

发行包是单文件、自包含版本，不需要另外安装 .NET。社区公开版未使用付费公众代码签名证书，Windows SmartScreen 可能显示提示；可先核对 Release 中的 SHA-256，或自行从源码构建。

## 背景媒体与隐私

- 背景文件只会复制到 `%LOCALAPPDATA%\CodexUsageWidget\backgrounds`，不会上传网络。
- 选择“默认背景”时，可以只停止显示，也可以同时删除应用保存的副本；原始文件永远不会被删除。
- 本地设置、背景媒体、Codex 登录文件和本机签名证书都不在仓库与 Release 中。
- 截图保护使用 Windows 的窗口捕获排除能力，能防止常见截图和屏幕共享工具捕获主窗口，但无法阻止外部相机或特殊采集硬件。
- 需要给项目提交界面截图时，可在齿轮设置中临时关闭截图保护。

应用通过本机 `codex app-server --stdio` 读取汇总数据，不直接读取或显示 `~/.codex/auth.json` 中的登录令牌，也不会开放公网监听端口。更多说明见 [SECURITY.md](SECURITY.md)。

## 从源码构建

安装 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)，然后运行：

```powershell
dotnet build .\CodexUsageWidget.sln -c Release
.\scripts\Build-Release.ps1 -Version 1.8.0 -Runtime win-x64
```

生成脚本不会下载或打包 `codex.exe`。维护者可用 `CodexUsageWidget.exe --demo` 启动虚构数据模式，生成不含真实账号数据的公开截图。

## 使用条件

- Windows 10 或 Windows 11（x64 / ARM64）。
- 已安装较新的 Codex，并已使用 ChatGPT 账号登录。
- 视频播放格式取决于 Windows 已安装的解码器，推荐 H.264 MP4。

## 免责声明

这是非官方社区项目，与 OpenAI 不存在隶属或背书关系。Codex App Server 协议可能随 Codex 版本变化。

项目采用 [MIT License](LICENSE)。Copyright © 2026 [九应](https://github.com/qeretycs-tech)。
