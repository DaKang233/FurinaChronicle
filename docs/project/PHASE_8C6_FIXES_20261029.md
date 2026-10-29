# Phase 8C.6：设备验收缺陷修复记录

日期：2026-10-29（Asia/Shanghai）

状态：修复和自动化通过；Partial device evidence；修复后设备回归待执行。

软件里程碑仍为 `0.8.15 / 8C.6`；当前主库 Schema 11，Archive 容器仍为 v1.0。

## 输入与复现

维护者提供的 `run-20261028-0115` 报告包含双向实际文件、重开、NoOp、取消、双端
Scoped Replace 和 Windows Tombstone 证据。26 个导出文件的大小／SHA-256 均与清单
吻合；18 组比较为 15 Equivalent / 3 Different。原报告写成 14/4，应在后续报告勘误，
不改写原始证据。

两处 Different 为 `windows-origin → W0`、`android-origin → A0`：来源未解析原因
`phase8c6_synthetic_unresolved` 变成 `placeholder_account`。这是 Accepted 逻辑字段
未能落库的保真缺口，不能因当前事实 digest 相同就忽略。第三处 Different 是两份独立
起点间的预期差异。

复查工具时另有偶发 FileNotFoundException：采样线程枚举后，SQLite journal 或整个
staged session 已被删除。该异常可以中断比较工具返回，但不是 Archive 内容差异。

## 修复

代码提交：`8e36948`。

- Core `GameAccount.UnresolvedReason` 保存来源说明，与当前身份诊断分开；
- Schema 11 增加可空列，Repository、Archive 新账号写入、快照导出均保留其值；
- 显式 null 不被补值，自定义原因不被替换。身份补全时清除不再适用的说明；
- v10 及适用旧库通过显式事务迁移到 v11；只延续旧 exporter 可证明的本地诊断。
  已经丢失的来源字符串无法从旧数据库恢复，不推断或自动纠正；
- 快照和原子恢复写入器共用当前 Schema 常量，避免硬编码导致假冲突；
- 独立 `RuntimeMetrics` 容忍文件／目录在枚举和 stat 期间消失，并保护并发采样的峰值；
  权限失败仍报告错误，不把不可读数据伪装为零空间；
- 严格比较器没有放宽，原始种子也没有修改。

这落实 ADR 0021 已接受的保真语义，不新增公开字段、导入覆盖入口或新的产品规则。
既有目标父属性的 Merge 行为没有改变；修复回归必须用原始种子与全新目标。

## 自动化

新增的自定义原因及显式 null 双数据库测试在修复前均失败；修复后通过。
测试直接复用工程工具的 Windows／Android 两份种子定义，避免只测试本地标准原因。

| 项目 | 结果 |
|---|---|
| 完整 Release 套件 | 616 passed / 0 failed / 0 skipped |
| 两个种子：导入→重复 NoOp→重开导出→第二库导入／NoOp→再次导出 | Equivalent |
| 原因 null／自定义值、Repository 更新备注 | Pass |
| Schema 10→11、重复初始化、重开、旧归属、失败时列与版本回滚 | Pass |
| 文件消失、目录延迟遍历消失、stat 时目录消失、权限错误不吞掉 | Pass |
| 旧证据 18 组重新比较 | 15 Equivalent / 3 Different，均符合原文件状态 |
| 原竞态样本再连续比较 20 次 | 全部正常退出，无自动重试 |
| 版权头／差异检查 | Pass |

旧种子→旧设备起点仍返回 Different，证明工具没有通过放宽规则掩盖旧缺陷。
新工具复查报告在工作区 `phase8c6-artifacts/fix-20261029/tool-recheck`，不是新设备证据。

## 固定构建

两个应用产物均绑定代码修复提交 `8e36948`、完整版本 `0.8.15.214`；Dirty=true 如实
保留（维护者未跟踪图标等工作区内容）。后续文档提交不意味着已重新编译该固定产物。

| 目标 | 结果 |
|---|---|
| MAUI Windows x64 Release，`Phase8CValidationMode=true` | 0 warning / 0 error |
| Android ARM64 Release，`E:\AndroidSDK`，匹配 RID／模式的定向 clean 后 build | 0 warning / 0 error |
| Android 包名 | `com.companyname.furinachronicle.validation.phase8c6.retest` |
| versionName / versionCode / ABI | `0.8.15 / 214 / arm64-v8a` |
| 签名 APK 大小 | 21,907,421 bytes |
| 签名 APK SHA-256 | `145db5f14e2cc1b86b46ae6a1a11a8bee4299e37b485c4c2e6c3de6c478fc8e6` |

APK 位于应用 `bin/Release/net10.0-android/android-arm64/` 下对应包名的
`-Signed.apk`。Windows 初次 --no-restore 构建因缺少 win-x64 assets 报 NETSDK1047，
重新还原该目标后构建成功，未为此修改项目配置。

本轮没有连接 ADB 设备，未安装／启动新 APK；没有访问生产库或用户凭据。当前
Windows 验收模式依然只隔离业务库，系统凭据隔离需要无真实通行证的测试用户环境。

## 剩余设备工作

按[修复后 DSH / UIA 补测指南](../development/PHASE_8C6_RETEST_GUIDE.md)执行：

1. Windows／Android 原始种子首次导入保真及双向返回，不能从旧设备起点重新定义基准；
2. 受控 Schema 10 一致副本升级证据；
3. Android Tombstone 取消／接受／重开／NoOp；
4. Preview／Apply 分阶段耗时、实际进程内存与临时空间采样；
5. 重开后原 Receipt 的只读查询；
6. 可观测事务写入窗口中的进程终止。

第 4～6 项如缺少合法诊断能力或阶段标识，记录具体 Blocked / Not run。未增加完整
Receipt 产品页面、事务暂停开关或 Android 私有目录读取旁路，不用工程故障注入冒充
设备终止。旧报告里已成立的设备证据可保留，但不能把本轮修复称作设备验收完成。
