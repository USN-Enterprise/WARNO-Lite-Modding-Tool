# WARNO Lite Modding Tool

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

这是一款面向 WARNO Mod 新手和不熟悉代码的玩家、易于上手的 Windows 图形化数值编辑器。通过可视化界面调整单位、武器、弹药等数值与相关参数，无需手写代码。编辑范围以数值和规则配置为主，不涉及模型、贴图、动画等美术资源的制作或编辑。

作者也是一名正在摸索的 WARNO Mod 新手。如果使用中遇到问题，或发现说明、参数理解有不准确的地方，欢迎指出，也欢迎提出改进建议。

**QQ 交流群：1013181135**

欢迎进群交流使用心得、反馈问题，一起探讨 Mod 制作。

## 许可证

本项目原创代码与文档采用 [MIT 许可证](LICENSE)，允许修改、分发和商用，需保留版权与许可声明。

从 WARNO 提取的游戏资料不在 MIT 授权范围内，包括 `src/WarnoLiteModdingTool.Core/Localisation/vanilla-names.json` 中的原版名称数据；相关权利归各自权利人所有，本项目不授予这些资料的再分发许可。

## 直接使用

1. 解压完整发布 ZIP。
2. 运行 `WarnoLiteModdingTool.exe`。1.8.7 起同一 Windows 登录会话只允许一个实例；重复启动提示“已在运行”。升级时请先关闭旧版。
3. 点击“自动查找 Mod”，或从“Mod 工具”使用同名入口，选择检测到的 Mod；也可手动打开包含 `GameData` 的 Mod 根目录。
4. 打开顶栏“设置”，选择跟随系统 / 简体中文 / English、默认编辑模式和七套主题和自定义图片背景；项目栏、Unit 列表与检查器、Weapon 三栏、战术师列表与编辑器，以及对象索引与高级详情之间的分隔条都可拖动，程序会保留各栏最低可用宽度。
5. 在“单位”模块中使用常驻搜索框；点较大的“筛选”按钮呼出阵营、国家、原子单位类别、生产栏位、角色、所属师和草稿状态。可切换“全部条件 / 任一条件”，收起后只留下带 × 的已选标签。
6. Unit、Weapon 和 Ammo 数值按大类折叠、小类标题分块；字段卡会随宽度排列。Unit 类别/角色/标签、战术师类型/标签采用独立标签选择，常用项来自当前 Mod。ECM 用正百分比显示；护甲类型、伤害族及索引由当前 Mod 的 `DamageResistance.ndf` 校验，步兵护甲索引锁为 1。前置部署可直接填写非负数值，也可选当前 Mod 动态档位；精确值 `2473.49823322` / `3533.56890459` 会分别标注“侦察”/“空降”。缺失模块仅在严格锚点成立时安全补建。
7. 单位列表常驻“全选当前筛选”“清空选择”和已选数量；展开“批量编辑”后选择已勾选 Unit 或当前完整筛选结果，再选择字段与操作。乘系数默认向上取整；普通模式提供固定值和增减百分比；高级模式另有乘法、固定数值加减、取整和可选上下限可预览命中数、样本与关联影响，也可直接加入草稿，加入时仍会校验。勾选至少两个 Unit 时，右侧第三栏还能直接检查和修改共有字段；不一致的数值显示 0 并明确提示。
8. 在“武器”模块中可按显示名或内部名任意片段搜索 Unit、Weapon、MountedWeapon、挂载使用的 Ammo 与替换候选；Unit 作用域列表同样常驻全选当前筛选、清空和计数。可编辑现有 Salves、Ammo 引用、隐藏开关、炮塔射界及 Ammo 字段。默认“仅当前 Unit”，也可选择多个 Unit；高级模式另有“全部引用”；局部修改按需克隆最小 `Ammo → Weapon → Unit` 链。
9. 在“弹药”模块中可以搜索并选择 Ammo，编辑现有射程、伤害族与索引、物理/压制伤害及两类溅射、精度、散布、射击、弹道、补给和行为字段；展开“查看引用”可搜索 Ammo → Weapon → Unit 链。这里编辑的是共享 Ammo 本体，草稿固定为“全部引用”；需要局部隔离时请从“武器”模块按 Unit 修改。
10. 界面显示 `AmmoBoxIndex`、Salves、每齐射射弹数及推导总弹量；EffectTag、WeaponAlternative、动画键和可识别表现引用只读显示，不开放自由新增/删除武器槽。
11. 在“战术师”模块编辑现有师：加入 Unit 与选择运输均支持显示名/内部名任意片段搜索，以及国家、阵营、栏位、角色小筛选；运输面板只把具有唯一运输模块的 Unit 作为新增候选，可一次勾选多项并以标签管理。既有但无法确认运输能力的兼容值会保留并标注。还可设置卡数、单卡数量和老练度倍率；师级标签和类型为标签选择，费用曲线在普通模式显示每类十个独立数值格，额外尾部值只在高级模式处理。
12. 默认卡组编辑页已移除；既有 `Decks`/`DeckPacks` 只读保留并参与基线冲突检查。战术师草稿检查同师 UnitRule 唯一、运输引用、数值范围与费用曲线，旧版本改变默认卡组的草稿会明确标为冲突。
13. 点击左侧“草稿总览”可查看当前项目全部未应用草稿；列表显示修改摘要、模块、状态和源文件，点选后显示原值→目标值、作用域、对象、字段和更新时间。批量修改收在可展开父项下，可复选子项后“应用所选”或“删除所选”。
14. 在通用对象索引中，名称采用主信息字号，内部类型、源文件和位置采用次要小字。默认排序为“源文件升序 → 行号升序”，同一行以字符偏移稳定兜底；表头箭头与排序说明会随点击更新。
15. 打开“高级模式”可查看 NDF 原值、字段路径、源文件位置、换算来源、引用影响和诊断；Unit 表右键也可快速查看内部名和引用关系。
16. 在任一编辑模块的事务中心预览并应用全部草稿；点击后会先显示忙碌状态，并在后台按草稿类型重索引和构建安全候选，完成后才弹出准确确认框。每次应用会在 `.warno-editor/backups/<备份编号>/` 保存原件和事务清单，并在目标 Mod 的 `logs/` 写入记录，可再次确认后恢复。
17. “生成 / 编译 Mod”常驻顶栏；“Mod 工具”菜单提供打开项目、启动开发模式和上传入口。官方流程按当前项目能力独立探测、确认后手动运行并保留输出。上传 BAT 还会调用官方备份，应以完整输出为准；编辑器自有事务备份始终是正式写入的安全基础。
18. 点“创建新 Mod”可调用 WARNO `Mods/CreateNewMod.bat`。选择包含官方脚本和 `ModData/base.zip` 的 `Mods` 目录，名称只能是 1–32 位英文字母/数字，且不得与现有 Mod 重名。
19. 顶栏“问题”进入诊断中心：“Mod 问题”显示项目、解析、草稿/事务和官方流程问题，“工具问题”显示程序自身异常。可复制带错误编号的完整诊断或打开日志目录；上次致命异常会在下次启动提示。

切换项目或正常关闭窗口前，程序会等待各模块的草稿保存；保存失败时保留当前项目和输入，修正问题后可重试。左侧模块列表可滚动，表格列宽随可用空间调整。

编辑阶段只写 `<目标 Mod>/.warno-editor/draft-v1.json`；正式文件只在应用确认后由事务写入。NDF 使用 UTF-8 无 BOM 精确字段补丁，CSV 保留原编码与换行；预览后发现外部修改、候选校验失败或提交异常时不会静默覆盖。程序另在当前 Windows 用户的本地应用数据目录保存最近项目列表。

程序不依赖当前开发用的 `Exp`，也不要求自身位于 WARNO 安装目录。编辑功能不需要 Python；只有用户主动调用 WARNO 官方 BAT 时，对应入口才依赖游戏自带的 `Utils`。战术师编辑需要 `Divisions.ndf`、`DivisionRules.ndf`、`DivisionCostMatrix.ndf`、`DeckPacks.ndf` 和 `Decks.ndf`；目标缺少某类文件时，只禁用对应模块。

## 将军模式与高级工作区

- 选择“将军模式”，搜索并选中已有战略营。中间编制树按营/团→连→排/组→单位展开，右侧修改选中项；可新增、删除、移动、排序、设置 HQ、改名，以及修改单位、数量、老练度和运输。
- “棋子属性”显示当前对象能够唯一识别的行动点、恢复、移动、角色、控制区、支援范围、战略影响、名称和已有图标引用。未识别字段不猜测写入。
- 编制修改实时进入草稿。应用时自动重算 PackIndex，共享定义按当前目标隔离；正式文件和必要名称 CSV 同事务备份、写入，失败回滚。
- 普通模式采用轻度精简，常用性能与编制功能保留。普通模式可编辑已显示字段并使用独立弹药模块；高级模式增加数字索引/复杂公式，打开“引用工作台”追踪引用并跳转，搜索当前对象字段及编辑受控原值。
- 高级应用预览按文件提供可翻页的修改前/后源码；普通模式同样可选择应用全部或部分草稿。切换模式不会丢弃任何已保存草稿。
- 英文选项翻译编辑器界面资源；用户名称、内部描述符、外部工具输出及未映射技术诊断保留原文。

## 1.7.1 使用边界

- 批量公式覆盖当前已支持的 Unit 字段；Weapon/Ammo 继续使用“当前 Unit / 所选 Unit / 全部引用”作用域编辑，第一版不保存跨 Mod 批量预设。
- 可以编辑现有 Unit、Weapon/Ammo 引用链和现有战术师规则；不编辑默认卡组，不克隆或删除 Unit，不创建/复制/删除整师；将军模式编辑现有战略营及其棋子属性，不创建整营或修改战役地图/事件/剧情。
- 不自由新增或删除武器槽，也不自动重建模型、贴图、动画等表现资源。
- 发布基线为 Windows x64 自包含 .NET 8 目录；最低 Windows 版本仍需更多干净环境验证。

## 开发与验证

需要 Microsoft .NET 8 x64 SDK。项目不使用第三方 NuGet 包。

```powershell
dotnet restore WarnoLiteModdingTool.sln --configfile NuGet.Config
dotnet build WarnoLiteModdingTool.sln -c Release --no-restore
dotnet run --project tests/WarnoLiteModdingTool.Tests/WarnoLiteModdingTool.Tests.csproj -c Release --no-build
```

自包含 Windows x64 发布：

```powershell
dotnet restore src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -r win-x64 --source https://api.nuget.org/v3/index.json
dotnet publish src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o publish/win-x64-portable
```

## 1.8 新操作

单位页“新增单位”启动五步向导；“编辑创建设置”可修改待创建单位。创建先进入草稿，应用时才一并写入身份、名称、牌组注册和所选师规则。母版外观沿用，可选择独立武器配置。暂不提供空白创建、跨 Mod 导入和正式单位删除。

武器、弹药、将军与师内列表提供折叠筛选；战术师列表支持搜索。问题中心可多选忽略与恢复，忽略不跳过正式应用校验。详见 [发布说明](RELEASE_NOTES.md)。

1.8.1：共用 NameToken 的单位可独立改名，自动新增名称 token；战术师默认按国家排序；切换单位仅展开最后手动展开的分区。

1.8.2：弹药名称与独立 token 草稿、原版名称预加载、将军标签筛选和国家列、可读战略差异。详见 [发布说明](RELEASE_NOTES.md)。

1.8.3：高级模式更名为专业模式。

1.8.4 的版本变更见 [发布说明](RELEASE_NOTES.md)。

---

<a id="english"></a>

# WARNO Lite Modding Tool — English

[中文](#chinese) | [English](#english)

A beginner-friendly Windows graphical editor for WARNO Mod creators and players who are unfamiliar with code. Use visual controls to adjust unit, weapon, and ammunition stats and related parameters without writing code. The editor focuses on numerical values and game rules; it does not create or edit models, textures, animations, or other art assets.

The author is also a WARNO Mod beginner learning along the way. If you encounter a problem or notice an inaccurate explanation or interpretation of a parameter, please point it out. Suggestions for improvement are welcome, too.

**QQ community group: 1013181135**

Join us to share tips, report issues, and discuss Mod creation.

## License

The project's original code and documentation are licensed under the [MIT License](LICENSE). Modification, redistribution, and commercial use are permitted, provided the copyright and license notices are retained.

Data extracted from WARNO is excluded from the MIT license, including the vanilla names in `src/WarnoLiteModdingTool.Core/Localisation/vanilla-names.json`. Rights to that material remain with its respective rights holders; this project grants no redistribution permission for it.

## Getting started

1. Extract the entire release ZIP.
2. Run `WarnoLiteModdingTool.exe`. Starting with 1.8.7, only one instance can run in the same Windows login session; duplicate launches display “Already running”. Close the old version before upgrading.
3. Use the automatic Mod search button, also available in the Mod Tools menu, and select a detected Mod. You can also manually open a Mod root folder containing `GameData`.
4. Open Settings in the top bar to choose the system language, Simplified Chinese, or English; set the default editing mode; and choose from seven themes or a custom background image. Drag the dividers between the project panel, unit list and inspector, weapon panels, division list and editor, and object index and details. Each panel retains a minimum usable width.
5. Use the search box in the Units module. Open Filters to select faction, country, unit category, production tab, role, division membership, and draft status. Switch between matching all or any conditions. When collapsed, the panel shows only the selected tags, each with a removal button.
6. Unit, Weapon, and Ammo numerical fields are grouped into collapsible categories and responsive field cards. Unit categories, roles, tags, and division types and tags use selectors populated from the current Mod. ECM is shown as a positive percentage. Armor and damage types and indices are validated against the Mod's `DamageResistance.ndf`; the infantry armor index is fixed at 1. Forward deployment accepts a nonnegative value or a preset found in the current Mod. The exact values `2473.49823322` and `3533.56890459` are labeled Recon and Airborne. Missing modules are added only when their insertion location can be identified reliably.
7. The unit list provides controls to select all filtered units, clear the selection, and view the selected count. In Batch Editing, choose selected units or all filtered results, then choose a field and operation. Multiplication rounds up by default. Basic mode offers fixed values and percentage increases or decreases; Advanced mode also offers multiplication, fixed additions and subtractions, rounding, and optional limits. Preview matches, samples, and related effects, or add changes directly to drafts with validation. Selecting at least two units opens a third panel for inspecting and editing shared fields; differing numerical values display 0 with an explicit mixed-values notice.
8. In Weapons, search any part of the display name or internal identifier of a Unit, Weapon, MountedWeapon, mounted Ammo, or replacement candidate. The unit scope list also provides select-all, clear, and count controls. Edit existing Salves, Ammo references, visibility toggles, turret firing arcs, and Ammo fields. The default scope is the current unit; multiple units can also be selected, and Advanced mode offers all references. Local changes clone only the required `Ammo → Weapon → Unit` references.
9. In Ammunition, search and select Ammo to edit existing range, damage type and index, physical and suppression damage, both splash types, accuracy, dispersion, firing, ballistics, supply, and behavior fields. Expand the references view to search the `Ammo → Weapon → Unit` chain. This page edits the shared Ammo object and always affects all references. For changes limited to particular units, use Weapons instead.
10. The interface shows `AmmoBoxIndex`, Salves, projectiles per salvo, and the calculated total ammunition. EffectTag, WeaponAlternative, animation keys, and identifiable visual references are read-only. Arbitrary weapon-slot creation or deletion is not supported.
11. In Tactical Divisions, edit existing divisions. Unit and transport selectors support partial display-name or internal-name searches and country, faction, tab, and role filters. New transport candidates must have a uniquely identified transport module. Select multiple transports and manage them as tags; existing values whose transport capability cannot be confirmed are retained and marked. Edit card limits, units per card, and veterancy multipliers. Division tags and types use selectors. Basic mode shows ten separate cost inputs per category; additional entries are handled in Advanced mode.
12. The default-deck editor has been removed. Existing `Decks` and `DeckPacks` remain read-only and participate in conflict checks. Division drafts validate unique UnitRules within each division, transport references, numerical ranges, and cost curves. Older drafts that change default decks are explicitly marked as conflicting.
13. Open Draft Overview on the left to see all unapplied changes in the current project. The list shows the change summary, module, status, and source file. Selecting an entry shows the original and target values, scope, object, field, and update time. Batch changes are grouped under expandable entries; select individual items to apply or delete them.
14. In the general object index, names are prominent and internal types, files, and locations appear as secondary details. The default order is source file, then line number, both ascending, with character position resolving ties. Header arrows and the sort description update when clicked.
15. Advanced mode shows raw NDF values, field paths, source locations, conversion references, affected references, and diagnostics. Right-click a unit to quickly inspect its internal name and references.
16. Preview and apply drafts from an editing module's transaction area. A busy indicator appears while the relevant data is reindexed and candidate changes are validated in the background; the confirmation dialog opens when preparation is complete. Each application saves originals and a transaction manifest in `.warno-editor/backups/<backup-id>/` and writes a record to the target Mod's `logs/` folder. Backups can be restored after confirmation.
17. Generate / Compile Mod is available in the top bar. Mod Tools provides project opening, development launch, and upload actions. Each official workflow is detected independently for the current project and runs manually after confirmation, with output retained. The upload BAT also invokes the official backup process; refer to its full output. The editor's own transaction backups remain the basis for safe file changes.
18. Create New Mod invokes WARNO's `Mods/CreateNewMod.bat`. Select a `Mods` folder containing the official scripts and `ModData/base.zip`. Names must contain 1–32 English letters or digits and must not duplicate an existing Mod name.
19. Open Problems in the top bar for diagnostics. Mod Problems covers project data, parsing, drafts, transactions, and official workflows; Tool Problems covers editor exceptions. Copy the full diagnostic with its error ID or open the log folder. A previous fatal exception is reported on the next launch.

Before switching projects or closing the window normally, the editor waits for draft saves in every module. Failed saves keep the current project and input available for retry. The module sidebar scrolls, and table columns adapt to available space.

During editing, only `<target Mod>/.warno-editor/draft-v1.json` is written. Actual Mod files are changed only after application is confirmed. NDF changes use precise UTF-8 field patches without a BOM; CSV files retain their original encoding and line endings. External changes after preview, failed validation, and commit errors do not cause silent overwrites. Recent projects are stored in the current Windows user's local application data folder.

The editor does not depend on the development sample named `Exp` and does not need to be installed inside WARNO's folder. Editing does not require Python. Only explicitly invoked official WARNO BAT workflows depend on the game's bundled `Utils`. Tactical division editing requires `Divisions.ndf`, `DivisionRules.ndf`, `DivisionCostMatrix.ndf`, `DeckPacks.ndf`, and `Decks.ndf`. Missing files disable only the affected module.

## Army General and the advanced workspace

- Open Army General and select an existing strategic battalion. The central tree expands from battalion/regiment to company, platoon/group, and unit. Edit the selected item on the right: add, delete, move, reorder, set HQ status, rename, or change units, quantities, veterancy, and transport.
- Pawn Properties exposes uniquely identified action points, recovery, movement, roles, zones of control, support range, strategic influence, names, and existing icon references. Unrecognized fields are not edited by guesswork.
- Formation changes are saved as drafts immediately. Applying them recalculates PackIndex values and isolates shared definitions for the selected target. Data files and required name CSV entries are backed up and written together, with rollback on failure.
- Basic mode keeps common stats and formation tools available, including editing visible fields and using the standalone Ammunition module. Advanced mode adds numerical indices and complex formulas. Use the Reference Workbench to trace and navigate references, search fields, and edit supported raw values.
- Advanced previews show paginated before-and-after source text for each file. Basic mode also supports applying all or selected drafts. Switching modes does not discard saved drafts.
- English translates the editor interface. User-created names, internal descriptors, external tool output, and unmapped technical diagnostics retain their original text.

## Version 1.7.1 limitations

- Batch formulas cover supported Unit fields. Weapon and Ammo editing uses the current-unit, selected-units, or all-references scope. Cross-Mod batch presets are not saved in this version.
- Existing units, Weapon/Ammo references, and tactical division rules can be edited. This version does not edit default decks, clone or delete units, or create, copy, or delete whole divisions. Army General edits existing strategic battalions and their pawn properties; it does not create whole battalions or edit campaign maps, events, or stories.
- Arbitrary weapon-slot addition or removal and automatic reconstruction of models, textures, animations, or other visual assets are not supported.
- Releases use a self-contained Windows x64 .NET 8 folder. The minimum supported Windows version still needs further verification on clean systems.

## Development and validation

Requires the Microsoft .NET 8 x64 SDK. The project uses no third-party NuGet packages.

```powershell
dotnet restore WarnoLiteModdingTool.sln --configfile NuGet.Config
dotnet build WarnoLiteModdingTool.sln -c Release --no-restore
dotnet run --project tests/WarnoLiteModdingTool.Tests/WarnoLiteModdingTool.Tests.csproj -c Release --no-build
```

Create a self-contained Windows x64 release:

```powershell
dotnet restore src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -r win-x64 --source https://api.nuget.org/v3/index.json
dotnet publish src/WarnoLiteModdingTool.App/WarnoLiteModdingTool.App.csproj -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=false -o publish/win-x64-portable
```

## New operations in 1.8

Add Unit on the Units page opens a five-step wizard. Edit Creation Settings updates a pending unit. Creation is saved as a draft first; identities, names, deck registration, and selected division rules are written together when applied. The new unit reuses its source unit's appearance and can have an independent weapon configuration. Creating units from scratch, importing across Mods, and deleting existing units are not supported.

Weapons, Ammunition, Army General, and division unit lists have collapsible filters. Tactical divisions support search. The Problems panel supports selecting multiple entries to ignore or restore; ignoring a problem does not bypass validation when applying changes. See the [release notes](RELEASE_NOTES.md#english).

1.8.1: Units sharing a NameToken can be renamed independently with a new name token. Tactical divisions sort by country. Switching units opens only the last manually expanded section.

1.8.2: Ammunition names and independent name-token drafts, preloaded vanilla names, Army General tag filters and country column, and readable formation changes. See the [release notes](RELEASE_NOTES.md#english).

1.8.3: Advanced mode was renamed Professional mode.

See the [release notes](RELEASE_NOTES.md#english) for changes in 1.8.4.
