# 原生贴边小窗源码

WPF / .NET 8 / Windows x64。v2.2.1提供圆环、短条、长条三种样式，单击循环并保存。左右贴边竖向，上下贴边横向；支持拖动吸附、位置锁定和重启恢复。独立程序集名保持CodexUsageWidget，窗口标题保持“Codex 用量悬浮窗”，以兼容follow监听器。

从本目录使用.NET 8 SDK执行：

```powershell
dotnet run --project tests/EdgeTests.csproj
dotnet run --project tests/WpfSmoke.csproj -- ./test-render
dotnet publish CodexEdgeWidget.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o ./publish
```

开发者需安装.NET 8 SDK；使用已发布安装包的最终用户无需SDK。

EdgeTests包含纯模型、JSON合同与自有假app-server集成测试。WpfSmoke验证真实WPF布局、三款事件循环及三竖条颜色、比例、底线像素；加--end-style Ring/Line/Vertical控制关闭选择，再用相同输出目录运行--restore-only检查新进程恢复。加--dispose-failure --production-window验证释放失败退出和普通无任务栏窗口。测试数据仅由显式fixture提供。

程序参数--demo显示示例数据，--preview显示真实额度并在初始时展开、便于验收；--settings-path可隔离验收设置。正常启动不传参。
