# Phase 8B 退出评审

状态：Closed

首次评审日期：2026-10-22（Asia/Shanghai）

关闭日期：2026-10-23（Asia/Shanghai）

## 结论

Phase 8B 的领域、持久化、Portable、修订／Undo 和 Chronicle 工程实现已经形成闭环。Android 物理设备隔离 Schema 7 新库、重开、导入／导出、幂等及拒绝路径和两个新领域的双向真实文件流程均由维护者确认通过；Windows 真实历史主库完成独立只读核验和一致备份。结合 populated Schema 6→7 及 v1～v6→7 自动化，当前达到 `Implementation complete`、`Device/file verification complete` 和 `Phase 8B closed`。

Windows 升级前没有留存精确 `user_version`，因此本评审不声称现场观察了 Schema 6→7。接受的是“精确自动迁移证据＋真实 pre-Schema-7 历史库自然升级结果”的组合证据；限制见下文。

## 完成定义核对

| 完成定义 | 当前证据 | 状态 |
|---|---|---|
| 共享身份、时间、来源、合并、修订与安全迁移 | 8B.0～8B.4 实现；Schema 1～7 与新增直接 populated 6→7／失败回滚测试 | Automated pass |
| Furina 载荷说明、Schema、样例、验证和迁移 | Gacha Portable 与两个领域 Portable；8B.7 正式 exporter 合成样本／负例／比较器 | Automated pass；格式仍 internal |
| 抽卡兼容与独立查询／导出 | 全量 525 项测试；领域出口不依赖 Chronicle | Automated pass |
| 角色／武器和挑战独立保存、查询、历史、Markdown、Portable 重导入 | 8B.5 与 8B.7 严格语义往返 | Automated pass |
| Chronicle 消费已成立领域 | Schema 7 generation／epoch、Contributor、Profile、游标、重建 | Automated pass |
| 至少三种时间语义且不伪造未知时间 | Gacha、手工获取、物品观察、深境螺旋 fixture 与 ADR 0018／0019 | Automated pass |
| 核心测试离线且跨平台语义保持 | Core／Services／Infrastructure 离线；Windows/Android Release build；真实双向文件流程 | Automated/build pass；maintainer-reported device pass |
| 8C 可基于领域出口建设完整 Archive | 输入清单及真实 picker／Preview／Apply／export 证据已形成 | Ready |

## 设备与历史库关闭证据

1. `P5 / Windows`：真实数据库创建于 2026-08-19，早于 Schema 7 引入；当前为 Schema 7、`application_id 0x46554348`、`quick_check ok`、外键错误 0，并保留 4 个档案、36 个账号、57,937 条 Gacha 记录、52,968 条修订和 17 个 ChangeSet。81,379,328 字节 SQLite Online Backup 的 SHA-256 为 `c5388ee3fab89697fdcff3b26835318916a35c8a1a598b1989defb9e0381eabd`，副本复核一致。
2. `P5 / Windows` 限制：升级前未捕获精确 Schema 或前后快照，不声称物理现场观察 Schema 6→7。自动化已经精确覆盖 populated Schema 6→7、v1～v6→7、失败回滚和取消；真实库证明 pre-Schema-7 历史数据自然到达健康 Schema 7。不得用人工修改 `user_version` 的降级库替代真实证据。
3. `P5 / Android`：物理设备上的 `0.8.8.169 / 8B.7 / 174fa95 / dirty false` 验收构建显示 Schema 7、正确 application ID 和 `quick_check ok`；维护者确认隔离新库、重开、导入和导出通过。
4. `P6`：维护者确认角色／武器和挑战均完成 Windows↔Android 双向文件流程、重复导入 NoOp，以及取消／拒绝、坏包和冲突路径。证据等级为 `Maintainer reported`，与自动化严格语义比较分开记录。

## 已接受的非阻塞延期

- Gacha 100k 宿主压力已有证据；设备级 100k 压力仍为后续质量项，没有新风险信号时不阻塞 8B 关闭。
- 完整 Chronicle 页面、完整 Archive／恢复、Sync／ChangeJournal、在线 Provider、全部挑战模式、真实成就目录属于后续阶段。

## 关闭决定

Phase 8B 于 2026-10-23 关闭，Phase 8C 可以开始。关闭不把内部 Portable 自动提升为公开稳定格式，也不改变上述 Windows 升级前版本未捕获的事实；后续代码或格式变化仍应按影响范围重新验证，而不能无限继承本次固定产物证据。
