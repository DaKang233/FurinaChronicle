# WinUI 抽卡文件导出

状态：Implemented；真实文件保存已验证。2026-11-19。

## 范围与选项

抽卡页“导出”冻结当前档案和账号列表。默认勾选当前账号／档案多选范围，用户可逐个勾选、恢复当前选择、全选或清空；没有选择时不能继续。输出所选账号的完整记录，当前视图、筛选和分页不截断导出。

格式沿用 UIGF 4.2、CSV、XLSX。三种格式均提供简体中文、繁体中文、英文。UIGF 提供既有兼容预设、最小预设、恢复默认，以及 info.lang、账号 lang、count、name、item_type、rank_type、空 hk4e_ugc 七个高级选项；必要标准字段始终输出。恢复默认将语言还原为简体中文、字段还原为兼容预设。

没有新增列编辑器或第三套 UIGF 预设。当前表格写出器有 30 列，包含原先九个抽卡列和身份／来源／导出上下文；MAUI 原页面只列出九列的提示已修正。公开表格结构、Codec 和格式语义保持不变。

## 离线与安全

生产 WinUI 注入 CachedGachaItemMetadataProvider，只查询现有 GachaMetadataDatabase 的多语言缓存，不触发过期刷新或首次联网。它与在线 SqliteGachaItemMetadataProvider 复用相同的行到领域元数据映射。缓存缺少目标语言时沿用现有导出器的原名称回退；缺少 UIGF 必需 item_id 时仍明确失败，不补造事实。

快照中的账号 GUID 在显示选项后冻结，再交给 LoadGachaExportData 验证归属。复用现有 UIGF／表格服务、暂存文件、系统文件保存器、目标目录同级临时文件和最终替换；没有新的事实写入、导入／覆盖或业务事务路径。取消选项不生成文件，取消系统保存不保存目标文件。

## 代码入口

- Windows/Ui1GachaView.ExportOptions.cs：账号、语言、预设、高级字段和不可变选项捕获。
- Windows/Ui1GachaView.Export.cs：既有后台生成、保存和通知流程。
- Windows/Composition/GachaPageServices.cs、WindowsComposition.cs：生产注入只读缓存，隔离合成模式仍允许空元数据。
- Infrastructure/Gacha/Metadata/CachedGachaItemMetadataProvider.cs：零网络查找。
- Tests/Infrastructure/Gacha/Metadata/SqliteGachaItemMetadataProviderTests.Export.cs：空／过期缓存、取消、三语言实际 UIGF／CSV 输出和缺失翻译回退。

上述路径均相对 src。数据库 Schema、UIGF／Portable／Archive 版本未改变。

## 验证

- 全量 Release 1204/1204，0 失败／跳过。新增五例验证缓存与实际语言输出；原导出编码、预设及公式转义回归通过。
- WinUI x64 Debug、Release 均 0 警告／错误。
- 最终 Debug 产物、隔离合成 SQLite、真实 Windows 系统“另存为”：账号多选、空选择禁止、两种预设和七个开关、恢复默认、语言／格式切换、取消均通过。
- 最小 UIGF 实际保存两个指定账号；可选字段全部省略，必要字段仍在。CSV／XLSX 实际各保存 629 条记录、30 列。
- 系统保存器由代理进程提供，UIA 显示为应用内子窗口且编辑／保存控件不提供操作 Pattern；选项使用 UIA，原生文件编辑使用限时 WM_SETTEXT 和异步 BM_CLICK。没有以虚假文件写入替代系统保存器。
- 原有档案、账号、共享身份、抽卡和修订表行内容哈希与输入合成副本一致；quick_check=ok、foreign_key_check 无结果。
- 证据：E:/Workspace/FurinaWorkspace/.validation/winui-export-20261119；最终脚本 Check-Export-Verified.ps1，结果 uia-verified-results.txt、database-results.txt，产物 selected-minimal-verified.json、all-accounts-verified.csv／xlsx。
- 测试按职责拆分后元数据定向复验 14/14。Android Debug 含程序集、显式 E:\AndroidSDK 编译通过，0 警告／错误，3 分 10.26 秒；Signed APK 92,334,464 字节。没有安装／真机交互。此证据不代表真实账号或外部服务验收。
