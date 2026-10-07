# 开发与构建 / Development and building

[中文](#chinese) | [English](#english) · [返回 README](README.md)

<a id="chinese"></a>

## 开发环境

本文对应根目录的 **2.11.2 / .NET 8 / WPF** 工程。普通用户请下载便携包并阅读[使用教程](使用教程.md)，不需要安装开发工具。

- Windows 与 Microsoft .NET 8 x64 SDK。
- 源码编辑依赖 AvalonEdit 6.3.1.120（[MIT](licenses/AvalonEdit-MIT.txt)），组件随自包含包分发，不要求用户安装 IDE 或 WebView2。源码专项：测试程序 `--source`；实际 WPF：`--source-ui`；合成大文件：`--source-perf`；旧版拒读：`--source-old-reader <旧Core.dll>`。
- NuGet 依赖：ZstdSharp.Port 0.8.8，用于解码本机游戏图片，采用 [MIT 许可证](licenses/ZstdSharp-MIT.txt)。首次还原需能访问 NuGet.org，或具备所需包的本地缓存。
- 自包含发布还需要对应 Windows x64 运行时包；这些是构建输入，最终用户无需自行安装。
- 不要求 `Exp`、研究样本或真实 Mod 作为运行依赖。测试使用合成样例，不应对用户真实 Mod 做写入验证。

在仓库根目录运行：

```powershell
dotnet restore WarnoLiteModdingTool.sln --source https://api.nuget.org/v3/index.json
dotnet build WarnoLiteModdingTool.sln -c Release --no-restore
dotnet run --project tests/WarnoLiteModdingTool.Tests/WarnoLiteModdingTool.Tests.csproj -c Release --no-build
```

测试项目是可执行检查程序，使用上面的 `dotnet run` 入口。源码检查通过不代表已完成官方生成或游戏内验收。

## 便携发布与文档

使用新的输出目录生成完整自包含发布物，避免混入旧版本文件：

```powershell
dotnet restore src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -r win-x64 --source https://api.nuget.org/v3/index.json
dotnet publish src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o publish/win-x64-portable
```

`publish/win-x64-portable` 为示例路径；已有交付物使用独立目录保留。分发整个输出目录，不能只取 EXE。正式打包应排除调试 PDB，并保留项目、运行时及第三方许可。

应用项目会复制下列文档；仓库和发布目录采用同名文件，使相对链接均可使用：

| 文件 | 内容 |
| --- | --- |
| `README.md` | 中英文简介与入口 |
| `使用教程.md` | 完整中文教程 |
| `USER_GUIDE.md` | 完整英文教程 |
| `RELEASE_NOTES.md` | 版本历史 |
| `DEVELOPMENT.md` | 本文 |
| `LICENSE`、`licenses/ZstdSharp-MIT.txt`、`licenses/AvalonEdit-MIT.txt` | 项目及应用依赖许可 |

另保留同源 `发布说明.md` 副本供现有“设置 → 关于 → 发布说明”入口使用。旧包中的 `使用说明.md` 是旧 README 的别名；新包使用上表的首页与两份教程。更新时建议解压到新目录，避免旧文档残留。

用户文档放根目录并纳入版本控制；内部 `docs/`、`logs/` 仍按现有规则忽略。历史版本发布准备脚本可能依赖旧文档别名和固定清单，不能直接作为新版本打包流程复用。对新的 ZIP 检查文档是否存在、与当前源码一致、相对链接及锚点是否有效；不覆盖已发布的历史附件。

## 距离与公路倍率验证（2.11.2）

`--distance-scale` 检查比例尺读取、输入、独立规则、草稿、CommonData提交及恢复；`--distance-scale-ui` 检查实际窗口、中英文和主题。`--road-speed`、`--road-speed-ui` 覆盖公路倍率。均使用合成Mod。无新增用户依赖或草稿格式，不代替游戏验证。

## 删除功能验证

2.11.1新增删除载荷v2、草稿schema7和单位历史v2，无新增运行依赖。专项：`--deletion`；实际窗口：`--deletion-ui`；旧版保护：`--deletion-old-reader <旧Core.dll>`。测试使用合成Mod，强行残留按告警提交，不能将其报告为引用闭合。

## 维护说明

- 按当前实现更新两份教程，同步步骤、作用域和限制；教程描述当前能力，版本变化集中写入发布说明。
- README 保留短流程和文档入口，不再逐版本累加操作说明。
- 当前教程面向根目录活动工程（现为 2.11.2），不将封存 v2 等实验工程的依赖或功能写入其使用说明。

---

<a id="english"></a>

## Development environment

This document covers the root **2.11.2 / .NET 8 / WPF** project. End users should download the portable release and read the [user guide](USER_GUIDE.md); developer tools are not required.

- Windows and the Microsoft .NET 8 x64 SDK.
- Source editing uses AvalonEdit 6.3.1.120 ([MIT](licenses/AvalonEdit-MIT.txt)), bundled with the portable app; no external IDE or WebView2 is required. Test switches: `--source`, `--source-ui`, `--source-perf`, and `--source-old-reader <old Core.dll>`.
- NuGet dependency: ZstdSharp.Port 0.8.8, used to decode local game images under the [MIT license](licenses/ZstdSharp-MIT.txt). Initial restore requires NuGet.org access or a local cache containing the required packages.
- Self-contained publishing also needs the Windows x64 runtime packages. These are build inputs, not separate installations for end users.
- `Exp`, research samples and real Mods are not runtime prerequisites. Use synthetic fixtures for write tests.

Run from the repository root:

```powershell
dotnet restore WarnoLiteModdingTool.sln --source https://api.nuget.org/v3/index.json
dotnet build WarnoLiteModdingTool.sln -c Release --no-restore
dotnet run --project tests/WarnoLiteModdingTool.Tests/WarnoLiteModdingTool.Tests.csproj -c Release --no-build
```

The tests are an executable check runner, invoked with `dotnet run` above. Passing source checks does not establish successful official generation or in-game behavior.

## Portable publishing and documentation

Publish to a fresh output directory to avoid retaining files from older versions:

```powershell
dotnet restore src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -r win-x64 --source https://api.nuget.org/v3/index.json
dotnet publish src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o publish/win-x64-portable
```

The output path is an example; preserve previous deliveries in separate directories. Distribute the entire output, not just the EXE. Exclude debug PDB files from release ZIPs and retain project, runtime and third-party licenses.

The application project copies these files with matching names in the repository and published directory:

| File | Purpose |
| --- | --- |
| `README.md` | Chinese/English overview and navigation |
| `使用教程.md` | Complete Chinese guide |
| `USER_GUIDE.md` | Complete English guide |
| `RELEASE_NOTES.md` | Version history |
| `DEVELOPMENT.md` | This document |
| `LICENSE`, `licenses/ZstdSharp-MIT.txt`, `licenses/AvalonEdit-MIT.txt` | Project and application dependency licenses |

An identical `发布说明.md` copy is retained for the existing Settings → About → Release notes action. Older packages used `使用说明.md` as an alias of the old README. New packages use the overview and two guides above. Extract updates into a fresh directory to avoid stale documents.

Public user documents live at the root and belong in version control; internal `docs/` and `logs/` remain ignored. Historical release scripts may assume old aliases and fixed file lists; do not reuse them unchanged. Check new ZIPs for document inclusion, agreement with the source files, and working relative links and anchors. Preserve existing published attachments.

## Maintenance

- Update both guides together, including steps, scope and limits. Guides describe current behavior; release notes record changes by version.
- Keep the README focused on the short workflow and navigation instead of appending version-specific instructions.
- These guides cover the active root project (now 2.11.2); keep the archived v2 and other experimental projects' dependencies and features separate.


### 工作台优化回归 / Workspace optimization checks

- `--source-opt`：对齐差异随机重构、全部分页命中、替换、schema6流式/BOM读取、可选会话损坏、已知新旧错误和航空组合。
- `--source-environment`：合成项目的374字符NTFS路径、只读和独占占用文件，以及搜索/分词CPU取消采样；不代表低权限账户或真实磁盘满验证。
- `--source-opt-perf search|save <Core.dll>`：每次独立进程比较preview.1和当前程序集；50万命中搜索与50MB草稿保存。搜索优化测完整计数和首500项，其他命中仍可翻页；不能解释为全量对象分配的同一实现。
- `--source-ui`：实际WPF的替换确认/撤销、输入与保存交错、应用范围和懒加载重开，输出程序化输入采样与布局截图；不模拟真实IME组合态。

Results are written under `publish/qa-source-optimization`; existing `qa-source-workspace/performance.json` remains the first preview's historical run. Storage stays at schema6. Content fingerprints cover full bytes; modification times alone never authorize commit.

- `--source-ui-large`：实际WPF的10/50MB多行样例打开、输入、草稿保存、候选与差异；取消应用后核对正式文件未改。大文件结果在 `large-ui.json`。
