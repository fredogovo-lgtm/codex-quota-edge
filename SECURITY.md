# 数据与运行范围

仅显示5小时额度、重置倒计时、本周额度。客户端只发送本机Codex app-server的initialize、initialized和account/rateLimits/read；由CLI使用既有登录状态，不直接打开或修改登录资料。

组件不保存账户、额度响应原文、聊天或CLI标准错误。用户目录下.codex-usage-widget/edge-settings.json保存ring/line/vertical、屏幕标识、边缘位置比例及锁定状态；preview-settings.json隔离验收选择。follow目录仅有有限大小的进程状态和监听日志。首次只从本工具原AppData和Codex包缓存的edge-settings.json迁移已知样式，不读取其他应用资料。

当前用户交互式计划任务只运行本组件拥有的监听脚本；不要求密码，不提升权限，不代替用户启动Codex。启停操作核对任务标识和动作路径，避免覆盖其他任务；先持久化停用意图再停止及移除任务。

监听器只关闭自身拥有且PID与启动时间匹配的面板，客户端只回收自己启动的app-server。更新或停用不结束真实Codex。发布包包含.NET许可，Codex CLI另行安装。本构建未签名，无自动下载安装更新。

反馈时可提供版本、复现步骤和脱敏错误类别，不应提供登录文件、令牌或聊天记录。

