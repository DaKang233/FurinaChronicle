# 8B.5 领域历史支持矩阵

状态：Implemented；device round-trip verified；Portable internal / unpublished

| 能力 | 角色／武器 | 挑战 |
|---|---|---|
| 生产领域 | 角色状态、武器状态、手工获取 | 深境螺旋观察 |
| 输入 | 有界内部 collector fixture；手工事实用例 | 有界内部 collector fixture |
| 在线 Provider | Not implemented | Not implemented |
| SQLite | 当前主库 Schema 7；领域事实仍为 `ArchiveDomainFacts` / `ArchiveDomainRevisions` | 同左 |
| 查询 | 账号＋种类＋UTC 半开时间范围，最多 500/页 | 账号＋模式／期次＋UTC 半开时间范围，最多 500/页 |
| 历史 | 全部不可变观察；手工事实稳定 ID＋修订链 | 全部观察＋当前最佳证据派生查询 |
| 人类可读 | Markdown，含范围、来源、缺值、字段及派生变化 | Markdown，含周期、来源、层／间、星数和实际／未知时间 |
| Portable | `dataset_kind = 1`，v1.0 | `dataset_kind = 2`，v1.0 |
| Plan / Apply | 只读预览；目标重验；原子父实体／事实／修订／Receipt | 同左，并与 ADR 0017 判重一致 |
| 资源上限 | 10 MiB；100,000 个事实／修订 | 10 MiB；100,000 个观察 |
| 旧格式 | 不适用；没有 v0 | 不适用；没有 v0 |
| future major | 拒绝 | 拒绝 |
| Windows / Android 编译 | 8B.7 隔离验收构建通过；Android 物理设备与 Windows 文件流程已验收 | 同左 |
| Windows↔Android 实际文件往返 | Pass（2026-10-23，maintainer reported）；真实 picker／Preview／Apply／export、重开、NoOp 及拒绝路径已走查 | 同左 |
| Chronicle | Character／Weapon Profile；观察和状态变化默认显式开启才显示 | Challenge Profile；深境螺旋每期最多一个 Main 完成节点 |

未实施：角色／武器和挑战的完整产品 UI、米游社／HoYoLAB 在线采集、幻想真境剧诗、幽境危战、真实成就目录、完整 Archive、Sync / ChangeJournal。Chronicle Contributor 与服务级查询已实现，但没有完整产品页面，也不是领域保存／导出前置条件。内部 collector fixture 用于证明 Source→Domain 边界，不是对外交换格式；用户交换应使用 Portable。8B.7 的入口是显式构建开关控制的隔离验收页，不是正式产品 UI；实际互通通过也不自动改变 Portable 的 `internal / unpublished` 状态。
