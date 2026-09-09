# WARNO Lite Modding Tool 发布说明

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

仅列出功能更新和影响使用的问题修复。

## 1.8.7 — 2026-09-09

- 禁止同时打开多个程序实例；重复启动时提示“已在运行”，确认后退出。

## 1.8.6 — 2026-09-09

- 批量编辑的原参数备注移到输入框右侧，输入控件保持对齐。
- 全界面滚动条滑块加粗，长列表中的滑块也更容易看见和拖动。
- 左侧功能模块列表支持上下滚动，窗口较小时仍可访问底部模块。
- 主窗口标题栏适当增高，改善最大化时的视觉比例。
- 表格列宽随可用空间自动调整，优先显示完整表头；窄区域保留最小可读宽度，必要时可横向滚动。
- 修复修改后立即切换项目或关闭窗口可能丢失最后输入的问题；保存失败时保留当前项目和输入，支持重试。

## 1.8.5 — 2026-09-08

- 修复合法游戏常量被误报为语法错误的问题。
- 各编辑模块的修改项旁显示原始参数名，方便对照。

## 1.8.4 — 2026-09-08

- 新增“游戏规则”编辑，可调整初始资金、参战名额等全局参数。
- 基础模式简化护甲与视野编辑，空中视野参数按原比例联动；侦察门槛支持预设与自定义。
- 单位特性支持搜索、添加和删除标签；专业模式支持自定义标识。
- 专业工作台支持编辑已识别的数值参数。

## 1.8.2 — 2026-09-08

- 弹药支持游戏内名称显示、搜索和改名；弹药页改名影响该弹药的全部使用单位。
- 将军模式增加可搜索的多选筛选和国家列，中文基础模式显示中文国名。
- 将军草稿与应用确认显示具体修改前后值。
- 修复弹药搜索框与筛选重叠、名称列过窄的问题。

## 1.8.1 — 2026-09-08

- 共用名称的单位可独立改名，不影响其他单位。
- 战术师列表按国家排序。
- 切换单位时只展开最后手动展开的分区。

## 1.8.0 — 2026-09-08

- 新增单位创建向导：从现有单位创建变体，可独立武器配置并加入所选师。
- 增加将军、战术师、武器和弹药筛选，以及师内卡片参数编辑。
- 特色单位与标签支持搜索、多选；批量修改可直接加入草稿。
- 问题中心支持批量忽略与恢复问题记录。

## 1.7.1 — 2026-09-08

- 修复自动查找 Mod 时重复列出同一目录的问题。

## 1.7.0 — 2026-09-08

- 普通模式开放已显示字段的编辑，并恢复独立弹药入口。
- 增加主题选择与自定义图片背景。
- 草稿支持多选、部分应用和部分删除；专业批改支持固定数值加减。
- 将军编制显示原版中英文名称，专业模式可查看和编辑编制索引。
- 新增自动查找 Steam 中的 WARNO Mod；单位和运输选择增加筛选、全选与清空。

## 1.6.0 — 2026-09-08

- 新增将军模式：编辑连排编制、单位、数量、老练度和运输，支持连排增删、移动、排序、总部设置及改名。
- 支持编辑战略棋子的行动点、移动、战斗角色、控制区、支援范围等属性。
- 新增语言与默认模式设置，支持简体中文和英文。
- 新增专业引用工作台、字段搜索和修改前后源码对照。

## 1.5.0 — 2026-09-07

- 普通模式支持自定义前置部署数值，并提供当前 Mod 的预设档位。
- 改善应用预览响应，减少等待时的界面卡顿。
- 战术师支持搜索并一次选择多种运输工具。
- 单位与武器列表增加全选当前筛选、清空选择和已选计数。
- 武器挂载弹药支持搜索选择；减少无关描述符造成的问题提示。

## 1.4.0

- 单位筛选支持“全部条件 / 任一条件”；类别、角色和师标签改为选择器。
- 新增多单位批量检查器，可直接修改共有参数。
- 支持为结构兼容的单位补充前置部署设置。
- 新增护甲、伤害类型及溅射参数编辑，ECM 以正百分比显示。
- 扩展武器、单位和运输搜索，改善大型候选列表的响应。
- 战术师费用曲线改为分格输入；移除默认卡组编辑，已有卡组数据保留。
- 弹药页增加使用者查询；修复合法 MAP 数据被误报为语法错误的问题。

## 1.3.0

- 单位、武器和弹药字段按类别分组折叠；单位筛选支持多选标签。
- 新增问题中心，区分 Mod 问题与工具问题，支持复制诊断。
- 接入官方 Mod 创建、生成、开发启动和上传入口。

## 1.2.0

- 新增草稿总览，可查看修改前后值与影响范围。
- 弹药页支持直接编辑共享弹药，并显示关联武器和单位数量。

## 1.1.0

- 新增黑蓝、白蓝主题，支持即时切换和保存。
- 工作区各栏可拖动调整宽度，对象列表支持按文件和行号排序。

## 1.0.1

- 修复打开含单位模块的 Mod 时可能闪退的问题。

## 1.0.0

- 支持打开结构兼容的 WARNO Mod，编辑单位数值与名称、武器和弹药参数、战术师单位池及费用。
- 支持批量赋值、公式调整和影响预览；武器修改可限定到所选单位。
- 编辑自动保存为草稿，正式应用提供校验、备份和恢复。
- 提供 Windows x64 便携版，解压即用。

---

<a id="english"></a>

# WARNO Lite Modding Tool — Release Notes

[中文](#chinese) | [English](#english)

Feature updates and fixes that affect everyday use.

## 1.8.7 — 2026-09-09

- Prevents multiple instances from running at once. A duplicate launch displays “Already running” and exits after dismissal.

## 1.8.6 — 2026-09-09

- Moved original parameter notes to the right of batch inputs and aligned the input controls.
- Made scrollbar thumbs thicker and easier to see and drag, including in long lists.
- Enabled vertical scrolling in the module sidebar so all modules remain accessible in smaller windows.
- Increased the main title bar height for more balanced proportions when maximized.
- Tables now adapt column widths to available space, keeping headers readable and allowing horizontal scrolling when needed.
- Fixed loss of the last edit when switching projects or closing immediately after typing. Failed saves keep the current project and input available for retry.

## 1.8.5 — 2026-09-08

- Fixed valid game constants being incorrectly reported as syntax errors.
- Added original parameter names beside editable fields for easier reference.

## 1.8.4 — 2026-09-08

- Added Game Rules editing for starting funds, player slots, and other global parameters.
- Simplified armor and vision editing in Basic mode. Air-vision values follow their original ratios; recon thresholds support presets and custom values.
- Unit specialties now support searchable tags that can be added or removed. Professional mode accepts custom identifiers.
- The Professional workbench supports editing recognized numerical parameters.

## 1.8.2 — 2026-09-08

- Added in-game ammunition names, name search, and renaming. Renaming on the Ammunition page affects every unit using that ammunition.
- Added searchable multi-select filters and a country column to Army General. Basic mode shows Chinese country names when using Chinese.
- Army General drafts and application confirmations show specific before-and-after values.
- Fixed overlapping ammunition search and filter controls and an overly narrow name column.

## 1.8.1 — 2026-09-08

- Units sharing a name can be renamed independently without affecting other units.
- Tactical divisions now sort by country.
- Switching units opens only the last manually expanded section.

## 1.8.0 — 2026-09-08

- Added a unit creation wizard: create a variant from an existing unit, optionally give it independent weapons, and add it to a selected division.
- Added Army General, tactical division, weapon, and ammunition filters, plus card parameter editing within divisions.
- Featured units and tags support search and multi-selection. Batch changes can be added directly to drafts.
- The Problems panel supports ignoring and restoring multiple entries at once.

## 1.7.1 — 2026-09-08

- Fixed automatic Mod search listing the same folder more than once.

## 1.7.0 — 2026-09-08

- Enabled editing of visible fields in Basic mode and restored the standalone Ammunition module.
- Added theme choices and custom background images.
- Drafts support multi-selection, partial application, and partial deletion. Professional batch editing supports fixed additions and subtractions.
- Army General formations display vanilla Chinese and English names. Professional mode can inspect and edit formation indices.
- Added automatic discovery of WARNO Mods in Steam. Unit and transport selection now includes filters, select-all, and clear controls.

## 1.6.0 — 2026-09-08

- Added Army General editing for company and platoon formations, units, quantities, veterancy, and transport. Add, delete, move, reorder, rename, and assign HQ status to companies and platoons.
- Added strategic pawn editing for action points, movement, combat roles, zones of control, support range, and other properties.
- Added language and default-mode settings, with Simplified Chinese and English support.
- Added the Professional Reference Workbench, field search, and before-and-after source previews.

## 1.5.0 — 2026-09-07

- Basic mode supports custom forward-deployment values and presets from the current Mod.
- Improved application preview responsiveness and reduced UI freezes while waiting.
- Tactical divisions support searching and selecting multiple transports at once.
- Unit and weapon lists include select-all-filtered, clear-selection, and selected-count controls.
- Mounted ammunition can be selected through search. Reduced problem reports caused by unrelated descriptors.

## 1.4.0

- Unit filters support matching all or any conditions. Categories, roles, and division tags use selectors.
- Added a multi-unit inspector for directly editing shared parameters.
- Added forward-deployment settings for structurally compatible units that lack them.
- Added armor, damage-type, and splash editing. ECM is displayed as a positive percentage.
- Expanded weapon, unit, and transport search and improved large candidate-list responsiveness.
- Division cost curves use separate input cells. Removed default-deck editing while retaining existing deck data.
- Added ammunition-user lookup. Fixed valid MAP data being incorrectly reported as syntax errors.

## 1.3.0

- Unit, weapon, and ammunition fields are grouped into collapsible categories. Unit filters support multi-select tags.
- Added a Problems panel separating Mod issues from editor issues, with copyable diagnostics.
- Added access to official Mod creation, generation, development launch, and upload workflows.

## 1.2.0

- Added Draft Overview to inspect before-and-after values and affected scope.
- The Ammunition page supports editing shared ammunition directly and shows linked weapon and unit counts.

## 1.1.0

- Added black-and-blue and white-and-blue themes with instant switching and saved preferences.
- Workspace panels can be resized by dragging dividers. The object list supports sorting by file and line number.

## 1.0.1

- Fixed a possible crash when opening a Mod containing a unit module.

## 1.0.0

- Open structurally compatible WARNO Mods and edit unit stats and names, weapon and ammunition parameters, and tactical division unit pools and costs.
- Apply batch values and formulas with impact previews. Weapon changes can be limited to selected units.
- Edits are automatically saved as drafts. Applying changes includes validation, backups, and restoration.
- Available as a portable Windows x64 release: extract and run.
