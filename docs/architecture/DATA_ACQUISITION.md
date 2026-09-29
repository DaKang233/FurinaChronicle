# 数据采集

状态：Target

避免建立包含登录、网络、文件和本地采集全部职责的万能 Provider。目标能力拆分为：

```text
IAccountAuthenticator
IProviderRoleResolver
IDataCollector<T>
IDataImportCodec<T>
IDataExportCodec<T>
ILocalGameDataCollector<T>
IProviderCapabilityDescriptor
```

米游社、HoYoLAB、UIGF、UIAF、Yae、游戏缓存和手工输入可以组合不同能力。上层领域只依赖能力端口，不依赖具体来源。

一次采集应记录批次 ID、Provider、账号引用、开始和完成时间、结果范围、限流或部分失败、Collector 版本以及原始响应保留等级。网络成功不等于数据完整；Provider 必须报告 `Completeness`。

Yae 等本地采集器是可选组件：不进入 Core，不成为 Android 或基础档案功能的依赖；用户明确授权后运行；记录采集器和游戏版本；版本不兼容时安全禁用；崩溃不得破坏主数据库；结果标记为 `LocalCollector`，不冒充官方 API。
