# WinUI 提瓦特小助手导入

状态：Implemented / runtime-source verification pending。2026-11-19。

## 两种入口

抽卡页的数据操作菜单仍只有“从提瓦特小助手导入…”；弹窗选择自动或手动。手动不需要通行证、SToken 或角色选择，读取登录摘要失败也不应阻塞它。自动仅列出本机可用、具备刷新能力的国服官服／B 服角色，绑定 UID 与已读取角色列表去重。不会改变其他页面的选择。

自动流程：选择登录角色 → 冻结本页档案／导入模式及目标 → 本地只读准备 → 第三方发送确认 → 再验证本机角色及档案 → 生成新的临时链接 → 请求 UIGF → 兼容升级及统一预览 → 现有原子导入。

发送前的确认说明 UID 和带 authkey 的临时链接将发送给 lelaer.com。链接不进入计划、UI、日志或保存。取消发送确认不会生成链接或发出请求；请求成功后再取消 UIGF 预览只取消本地写入，不能撤回已经发送的数据。自动与手动均检查返回文件只包含所请求的一个 UID，不允许错误／额外账号借档案全账号模式写入。

## 复用与数据语义

- 显式角色的 TeyvatHelperUigfImportSource.DownloadForRoleAsync 复用原来的国服、SToken／MID、UID 和区服验证；不经过全局 PassportSelection。
- 只在执行下载时调用 ISTokenGachaUrlProvider，生成的链接不跨次使用。
- ServiceGachaTeyvatHelperImportGateway 接入相同请求客户端。没有增加另一套 HTTP 或业务写入体系。
- UIGF 2.2 等兼容版本进入同一个 UigfCompatibleGachaReader、GachaImportCoordinator、ImportUigfGachaRecords 和 CommitGachaRecords。
- 文件事实记为 StandardImport 与实际导入时间；不会因链接由官方生成就改成 OfficialApi 或补造 FetchedAt。
- 当前账号／档案范围、增量／全量、Revision、Undo、Tombstone、清理确认和原子性继续采用已有规则。新建父对象的整次导入不承诺 Undo；已有账号的纯事实变更按原规则撤销。
- 新增工作不改变 Schema、公开 Portable／Archive 格式或第三方上传用途。

## 自动化与 Windows 窗口证据

新增 12 项自动模式测试覆盖：本地准备不生成链接；删除／停用凭据、角色改变、Cookie-only、目标档案消失；取消；手动方式不读取凭据；失败秘密清理；真实旧版 UIGF 升级和原子导入；全量更新与撤销；同 UID 跨档案独立；错误／多个来源 UID。

当前混合工作区全量 Release 1157/1157；WinUI x64 Debug／Release 均 0 警告／0 错误。Release 产物检查不含 SyntheticTeyvatHelperSource 类型和 FURINA_UI1_TEYVAT_SCENARIO 标记。

隔离合成 SQLite 和虚构 DPAPI 凭据的实际 Windows UIA：
- 无登录时自动按钮禁用、手动表单可开；手动取消无请求。
- 有国服合成角色后，可选自动；发送确认取消时计数仍为 0。
- 下载后取消本地预览可返回；发送次数与本地提交次数不混同。
- UIGF 2.2 兼容预览、增量成功、全量预览与更新实际通过；关闭后数据库只有两条目标记录，最终发生时间秒数为第 3 次响应值，偏移 UTC+08:00，ImportedAt 非空、FetchedAt 为空。
- 网络失败／返回另一 UID 时没有预览或写入，原 Gacha 事实哈希不变，操作恢复可用；错误 UID 的安全通知已进入本机诊断。
- 所有 case 的原有记录内容不变，quick_check=ok，foreign_key_check 无结果；日志扫描不含合成 token 或生成链接。
- 最终截图已检查。测试根目录：E:\Workspace\FurinaWorkspace\.validation\winui-teyvat-20261119。

上面的 UIA 使用真实生产编排、文件读取、升级和原子存储；只将官方链接生成及第三方下载端口替换为显式 Debug 合成源。替身只在显式隔离目录下选择，只写请求计数，不保存链接／凭据。其计数不代表真实第三方网络证据。

真实米游社角色生成链接、真实 lelaer.com 响应与系统网络条件：Not run。本轮未上传用户凭据／数据，未安装 Android 或执行真机验收。
