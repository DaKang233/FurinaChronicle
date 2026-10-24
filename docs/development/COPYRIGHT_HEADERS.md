# 版权头规范

状态：Accepted

## 标准版权头

C# 文件使用：

```csharp
// Copyright (c) 2026 DaKang233.
// SPDX-License-Identifier: MIT
```

XAML、XML、MSBuild 项目文件、Props、Targets、Resx、Slnx、Plist、应用清单和 Apple Privacy Manifest 使用：

```xml
<!--
  Copyright (c) 2026 DaKang233.
  SPDX-License-Identifier: MIT
-->
```

存在 XML 声明时，版权头放在 XML 声明之后；否则放在文件第一行。`2026` 是项目首次版权年份，不进行年度批量替换。许可证全文以仓库根目录的 `LICENSE.txt` 为准。

自动生成且不纳入版本控制的 `bin`、`obj`、`Generated`、`*.g.cs`、`*.g.i.cs` 和 `*.Designer.cs` 文件不添加版权头。SVG、PNG、字体等资源文件不属于代码版权头范围，不由此脚本修改。

## 编辑器与自动补齐

`.editorconfig` 为 C# 启用 `IDE0073`，Visual Studio 和兼容编辑器会提示缺少文件头，并可应用标准模板。

XAML 和 XML 由仓库脚本统一处理：

```powershell
./eng/copyright/Manage-CopyrightHeaders.ps1 -Mode Apply
./eng/copyright/Manage-CopyrightHeaders.ps1 -Mode Check
```

首次克隆后运行以下命令启用仓库提交钩子：

```powershell
./eng/Install-RepositoryHooks.ps1
```

钩子会在提交前为已暂存的新建或修改文件补齐版权头，并重新暂存这些文件。CI 会检查所有受版本控制的目标文件；未启用本地钩子也不能合并缺少版权头的更改。

## 修改规则

- 不在文件头写入个人邮箱、年份范围或动态年份。
- 不复制完整 MIT 正文到每个源文件。
- 不为不同项目层或平台使用不同措辞。
- 修改版权人、许可证或标准头内容时，必须同时修改脚本、`.editorconfig`、本文档和现有文件。
