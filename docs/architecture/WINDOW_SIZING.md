# WinUI 窗口尺寸

状态：Implemented。2026-11-19。

原 MAUI 宿主在 App.xaml.cs 中设置 MinimumWidth=1000、MinimumHeight=600。独立 WinUI 保留初始 1100×720 物理像素，并补齐相同的最小逻辑尺寸 1000×600。

使用当前实际 Windows App SDK 2.5.1 中的 OverlappedPresenter.PreferredMinimumWidth／PreferredMinimumHeight。按 XamlRoot.RasterizationScale 将逻辑尺寸向上取整为物理像素；Root 加载、缩放或关联变化时更新。窗口已经小于新下限时，仅在 restored 状态补大，不干涉最大化／最小化。关闭时解除 XamlRoot 事件订阅。

这不引入原生窗口过程替换或实验性的 Window.MinWidth 属性。侧栏继续使用现有 NavigationView 的原生打开／收起按钮；在当前 compact-overlay 布局下不另加一套导航状态。

接口与物理／逻辑尺寸依据见微软 [窗口管理](https://learn.microsoft.com/en-us/windows/apps/develop/ui/manage-app-windows#modify-an-overlappedpresenter) 和 [PreferredMinimumWidth](https://learn.microsoft.com/en-us/windows/windows-app-sdk/api/winrt/microsoft.ui.windowing.overlappedpresenter.preferredminimumwidth?view=windows-app-sdk-1.8)。实现首先对照本机投影程序集确认属性存在，没有升级包或依赖实验版 API。

## 验证

- MainWindow.Sizing.cs、MainWindow.xaml.cs 为实现位置；没有更改数据库或业务选择。
- 实际 Windows UIA／本机窗口消息检查，当前缩放 100%：WM_GETMINMAXINFO 返回 1000×600。
- 对本次测试窗口执行系统键盘缩放，向左／向上各 65 次后实际窗口仍为 1000×600；不是只检查一个代码常量。
- 原生侧栏按钮名称在“打开导航”与“关闭导航”间切换；打开／收起和最小窗口截图已检查。
- 最终 Debug 产物重跑通过。证据目录：E:\Workspace\FurinaWorkspace\.validation\window-minimum-20261119。
- 跨显示器拖动及其他 DPI 档位的真实交互未运行；不把缩放公式或编译当成该项设备证据。
