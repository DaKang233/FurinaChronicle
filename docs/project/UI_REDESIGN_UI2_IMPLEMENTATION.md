# UI-2 抽卡分析实施记录

日期：2026-11-08（Asia/Shanghai）

UI-1 双端验收与 U10 隔离宿主补测已具备开展 UI-2 的条件。首个可运行增量沿用
UI-1 的合成库和隔离入口，在独立 WinUI 与 Android MAUI 接入五种只读视图。

## 当前范围

- 单账号和档案账号多选按稳定 ID 建立查询。同 UID 的另一档案副本不会进入所选档案
  的统计；复查时移除失效账号。取消、快速切换和离页沿用请求代数与取消令牌。
- 明细使用正式分页查询，每页 25 条，支持星级、卡池和排序。详情显示来源、原始
  时间、记录 ID 和物品信息。档案多选时列表显示账号。
- 总览使用正式统计服务的卡池总抽数、五星数和区间指标，详情显示最近五星。未合并
  多账号的当前保底；明细筛选独立，避免星级过滤改变总览口径。
- 历史复用内置活动目录，显示匹配状态、版本及物品汇总。日历支持按月筛选，采用
  UTC+08:00 或 UTC 归日，选择日期进入当天明细，记录详情保留原偏移。物品按已有
  统计语义显示本地抽取次数与最近记录时间。
- Android 将范围移到可滚动的独立面板，首屏保留视图选择和分页，将加载完成提示从
  常驻区域移除。Windows 使用原生选择器、可滚动详情对话框和现有响应式导航。

预览继续使用隔离 SQLite 和内置数据。旧业务抽卡、备份和管理入口仍由原页面提供。
本阶段只读，Schema、公开格式与生产数据路径沿用原约定。

## 验证与产物

本轮证据目录为 `artifacts/ui2/`。Windows 可通过 `windows/FurinaChronicle.Windows.exe`
启动，使用 `FURINA_UI1_DIRECTORY` 指定绝对隔离数据根。Android 使用 `android-build/Release/net10.0-android/android-arm64/`
下的签名 APK，包名沿用 `com.companyname.furinachronicle.preview.ui1`，与正式包分离。

| 检查 | 结果 |
|---|---|
| Release 回归 | 632/632 通过；`tests/ui2-regression.trx` |
| 独立 WinUI 发布与实际 UIA | 17 项通过；`windows-uia.json`，含五视图与 68 条多账号范围 |
| Android ARM64 独立预览构建 | 0 警告、0 错误 |
| Android API 36 模拟器 | 10 项通过；`android-emulator-uia.json`，含多选、五视图、日期下钻、五星筛选与 130% 横屏 |
| Android 实体设备 | 未运行；本轮仅连接临时模拟器 |
| 旧 MAUI Windows 8C.6 隔离宿主 | 构建通过，0 警告、0 错误 |

Android 横屏证据为 `android-landscape-130.png` 与同名控件树。2400×1080、字体 130%
时显示两条完整记录和分页摘要；该证据仅覆盖本次模拟器配置。原 UI-1 验收清单的
11 项产物指纹复核一致。

## 2026-11-09 界面收尾

- Windows 主导航提供概览与数据／抽卡，组件和主题诊断入口放在页脚。抽卡明细按可用
  宽度使用表格或紧凑列表，宽窗并排显示详情，窄窗使用可滚动对话框。
- Android 隔离预览使用概览和数据标签，横屏收起底栏，并提供“打开概览”菜单。概览可
  切换应用外观；正式 MAUI 业务入口继续承载现有流程。
- 双端月历按周排列整月日期，支持上／下月与时区切换，点击日期进入正式分页查询的
  当天明细。物品页使用自适应网格。总览提供星级分布、同账号区间与最近五星。
- 单条详情以账号副本 ID 和外部记录 ID 查询已有修订，只读显示本地变更前后内容，
  不将同 UID 的其他档案副本混入。取消或失败时清除旧月历内容。

最终预览和验收证据位于 `artifacts/ui2-complete/`。首个增量的产物与模拟器证据保留在
`artifacts/ui2/`。UI-2 仍为隔离只读交付，刷新、导入导出、纠正和删除属于 UI-3。

| 最终检查 | 结果与证据 |
|---|---|
| Release 全量回归 | `tests/ui2-final.trx`，632 项通过，包含同 UID、同外部记录 ID 的独立修订核验 |
| WinUI 发布和实际交互 | `windows/FurinaChronicle.Windows.exe`，`windows-uia.json` 共 22 项，含 1600×820 并排详情、800×600 窄窗、月历与日期下钻。`windows-theme.json` 另核验深色和浅色切换 |
| Android ARM64 发布 | `android-theme-build/Release/net10.0-android/android-arm64/com.companyname.furinachronicle.preview.ui1-Signed.apk`，0 警告、0 错误 |
| Android 14 真机 | V2353A，19 项通过，见 `device-acceptance.json` 与 `device-*.xml`。覆盖概览往返、五视图、范围、修订、筛选、月历下钻、130% 和 200% 字体横屏，以及 200% 下滚动。`device-dark.png` 确认视图选择、分页和底栏文字在深色下可读 |
| 旧 MAUI Windows 8C.6 | 隔离验证组合根构建通过，0 警告、0 错误 |
| 首轮 UI-1 产物 | 原清单 11 项 SHA-256 复核一致 |

真机通过 ADB 操作隔离预览包。测试后的字体和旋转设置恢复到原值。长账号名称在
窄标题栏可能省略，范围面板可查看完整标识。TalkBack 与 Narrator 的完整朗读矩阵、
其他 Android 机型与 Windows 缩放组合未在本轮覆盖。
