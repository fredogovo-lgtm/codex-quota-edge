# Codex 额度贴边面板 v2.2.1

面板支持沿屏幕左、上、右、下四边拖动定位，松手贴到最近边缘。右键选择“锁定位置”或“解除锁定位置”。重启恢复上次位置、锁定状态和显示样式。

左右边缘收起为26×84逻辑像素，悬停展开到184×84；上下边缘收起为84×26横向排列，悬停展开到184×110，顶部向下、底部向上展开详情。移开收起。单击循环切换圆环、横向短条、三根并排竖条；拖动不会切换样式。锁定位置后仍可单击切换和悬停查看。多屏位置带屏幕标识，屏幕移除后回到可用屏幕范围。

- 青绿：5小时剩余额度。
- 琥珀：距离5小时额度重置的倒计时，以5小时为满进度。
- 蓝色：本周剩余额度。

长条在左右边缘从底部填充，在上下边缘转为三根横条并从左侧填充；详情显示百分比及倒计时。额度每60秒查询，倒计时逐秒更新。缺失、失败或到期尚未刷新时显示灰色或“—”。

## 启用与停用

解压整个包，双击 **Enable-Follow.vbs** 启用；双击 **Disable-Follow.vbs** 停用；**Start-Follow.vbs** 恢复已启用的监听。启停由无控制台程序在后台完成。旧cmd入口兼容保留，Windows运行cmd时可能短暂显示命令提示符，日常请使用vbs入口。

安装在当前用户 `%LOCALAPPDATA%\Programs\CodexUsageWidget-Follow`。Codex已打开时显示面板，退出后关闭，重开后恢复；最小化不算退出。当前用户交互式计划任务在登录时启动监听，意外停止后约1分钟内恢复。无需密码或管理员权限。右键“退出”只关闭本次Codex会话的面板，重开Codex或停用再启用后恢复。

设置：`%USERPROFILE%\.codex-usage-widget\edge-settings.json`，保存样式及屏幕位置、锁定状态。升级保留原样式；旧格式自动兼容。状态及有限大小日志在同目录follow文件夹。演示/验收默认使用preview-settings.json，避免改变日常选择。

## 运行与来源

Windows10/11 x64，已安装并登录Codex，需要Windows PowerShell5.1、Windows Script Host、Windows任务计划服务及系统.NET Framework。面板包含.NET8运行环境，构建未签名。新宿主使用系统.NET Framework启动无控制台子进程。

独立桌面伴随组件，通过本机Codex CLI只读查询额度，不修改Codex或登录资料，不保存额度响应原文。额度读取源于qeretycs-tech/codex-usage-widget v1.8.0（MIT），详见LICENSE、THIRD_PARTY_NOTICES.md与UPSTREAM.json。

源码在compact/和source/follow/；旧安装备份及发布包保留。回退前先用本版Disable-Follow.vbs停用，再运行旧包入口。--demo显示明确示例数据，--preview用于额度验收。
