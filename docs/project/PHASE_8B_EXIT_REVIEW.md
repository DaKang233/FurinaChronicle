# Phase 8B 退出评审

状态：Awaiting device/file verification；不得标记 Closed

评审日期：2026-10-22（Asia/Shanghai）

## 结论

Phase 8B 的领域、持久化、Portable、修订／Undo 和 Chronicle 工程实现已经形成闭环，8B.7 的非设备工程门槛已完成。当前仍缺 Windows 实际应用旧库升级、Android 实际应用新库重开，以及两个新领域的双向真实文件往返。因此当前可称 `Implementation complete`，不可称 `Device/file verification complete` 或 `Phase 8B closed`。

## 完成定义核对

| 完成定义 | 当前证据 | 状态 |
|---|---|---|
| 共享身份、时间、来源、合并、修订与安全迁移 | 8B.0～8B.4 实现；Schema 1～7 与新增直接 populated 6→7／失败回滚测试 | Automated pass |
| Furina 载荷说明、Schema、样例、验证和迁移 | Gacha Portable 与两个领域 Portable；8B.7 正式 exporter 合成样本／负例／比较器 | Automated pass；格式仍 internal |
| 抽卡兼容与独立查询／导出 | 全量 525 项测试；领域出口不依赖 Chronicle | Automated pass |
| 角色／武器和挑战独立保存、查询、历史、Markdown、Portable 重导入 | 8B.5 与 8B.7 严格语义往返 | Automated pass |
| Chronicle 消费已成立领域 | Schema 7 generation／epoch、Contributor、Profile、游标、重建 | Automated pass |
| 至少三种时间语义且不伪造未知时间 | Gacha、手工获取、物品观察、深境螺旋 fixture 与 ADR 0018／0019 | Automated pass |
| 核心测试离线且跨平台语义保持 | Core／Services／Infrastructure 离线；Windows/Android Release build | Build verified；device files pending |
| 8C 可基于领域出口建设完整 Archive | 输入清单已形成 | Ready after remaining evidence |

## 尚未关闭的硬门槛

1. `P5 / Windows`：用真实旧版生成或历史一致备份的 Schema 6 数据库，通过 Windows 验收 App 升级，退出重开并核对业务、外键、修订、Undo、Receipt 和 Chronicle 失效／重建。
2. `P5 / Android`：指定 ARM64 产物已在物理设备完成覆盖安装和冷启动，但仍须以隔离验收数据库新建 Schema 7，导入、退出重开、查询并导出。启动通过不能替代该数据流程证据。
3. `P6`：角色／武器和挑战各自完成 Windows→Android→Windows、Android 发起→Windows、重复导入、已有自然身份目标、取消和坏包拒绝；实际返回文件须由 `eng/phase8b` 比较器核对。
4. 设备证据必须绑定完成源码后的具体版本、commit、dirty 状态、文件 SHA-256 和构建产物；工程代码继续变化后按影响范围重验。

## 已接受的非阻塞延期

- Gacha 100k 宿主压力已有证据；设备级 100k 压力仍为后续质量项，没有新风险信号时不阻塞 8B 关闭。
- 完整 Chronicle 页面、完整 Archive／恢复、Sync／ChangeJournal、在线 Provider、全部挑战模式、真实成就目录属于后续阶段。

## 关闭规则

只有上述 P5/P6 实际证据全部通过，且支持矩阵和本文件更新到对应固定 commit，才能把 8B.7 与 Phase 8B 改为 Closed。构建、宿主测试、模拟器或口头推断均不能替代缺失的设备／文件证据。
