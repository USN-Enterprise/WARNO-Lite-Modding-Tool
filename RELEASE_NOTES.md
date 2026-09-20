# WARNO Lite Modding Tool 发布说明

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

仅列出功能更新和影响使用的问题修复。

## 1.9.11 — 2026-09-20

- 武器页新增独立批量窗口，跨单位/Weapon勾选真实挂载，支持完整筛选结果、搜索、当前Mod条件筛选与隐藏已选数量。
- 覆盖现有可解析库存、数量、显示、Ammo替换、炮塔角度、弹药性能和行为字段；固定值/百分比及专业公式，明确取整、完整逐行预览和共享影响。
- 同箱/同炮塔去重；所选Ammo挂载精确隔离。按单位最终组合生成副本，相同结果复用，不同单位的不同值不会相互覆盖；局部副本继承共享的其他字段修改。
- 独立批次草稿、明确替换重叠范围、关联依赖应用、引用/基线复查与多文件备份恢复。替换Ammo后新增参数以最终Ammo为准；旧Ammo参数与后续替换冲突需移除旧批次重新预览。
- 待创建单位先应用创建；待删除单位禁止批改。保留1.9.10图片功能。未修改真实Mod，未运行生成器或游戏。

## 1.9.10 — 2026-09-19

- 单位检查器新增“修改单位图片”：选择当前Mod已有图片，或导入PNG/JPEG并在独立窗口裁剪、缩放查看及处理圆形透明区域；保留原比例和透明度，不套用师徽模板。
- 已有单位和待创建单位均支持图片草稿及重开预览。自定义图片使用独立PNG和按钮纹理键，只重定向所选单位；原图与共享者保留。
- 图片、纹理声明和单位引用纳入同一可恢复应用流程；支持与新建/改名同批提交，检查资源冲突、外部变更与失败恢复。
- 不改变单位3D模型或涂装。应用后仍需生成Mod；本版尚未运行生成器或验证游戏内显示。

## 1.9.9 — 2026-09-18

- 新建单位注册继承当前Mod可确认的母版路径，默认变量名使用母版名加递增序号；专业模式支持待创建及正式单位改名，联动引用，保持GUID、名称token和注册编号。已有错误注册可单独修复。
- 新增“特性与实际能力”：15类特性配套，区分教官、IFV角色及SIGINT合法变体；专业模式可自由增删、替换和清空当前Mod可解析的已有能力，显示标签独立编辑。
- 支持取消未应用创建及删除本工具已创建单位；核对来源，预览引用清理或替代，保留共享资源，删除编号不自动复用。待删除状态可撤销，正式删除支持文件级备份恢复。
- 三项接入既有草稿与事务，组合普通字段、经验路线和其他编辑；提交前重新检查输入、依赖和草稿变化。未知或多义结构说明原因并阻止相关写入。
- 本版验证为合成事务、失败恢复及WPF界面检查；未运行官方生成器或游戏，未修改用户真实Mod。

## 1.9.8 — 2026-09-17

- 游戏规则新增“经验与老练度”，按大类→路线→等级组织，基础/专业模式均可用，支持搜索及中英文。
- 编辑当前Mod已有路线的各级门槛及已识别效果数值，保留原修饰类型、未知效果、等级标签和未改文本；显示共享使用者及来源。
- 等级改动进入草稿，门槛/效果跨文件联合预览、备份与失败恢复；应用前重新检查共享影响，支持与单位路线切换同批提交。
- SF缺省0级、固定翼空效果包及自定义实际等级结构如实显示；多处共享或语义未支持的效果只读，不自动补建。
- 本版不创建单位独立路线、不扩展游戏等级上限、不自动改游戏提示文本；未运行游戏验证经验计算公式。

## 1.9.7 patch1 — 2026-09-17

- 修复已有师徽从后台加载后，打开图片编辑或固定模板时出现“调用线程无法访问此对象”的错误。导出PNG不再读取后台解码器元数据，像素和透明度保持。

## 1.9.7 — 2026-09-16

- 基础信息增加经验类型选择，候选来自当前Mod有效四档配置；展开显示实际等级效果，切换走草稿、预览和备份流程。
- “名称与徽章”保留选择已有师徽，新增可搜索的缩略图库、PNG/JPEG导入及独立图片编辑窗。支持缩放、矩形裁剪、圆形内/外透明、撤销与重置。
- 十种固定师徽模板支持0–3位番号：近卫、东德、空降、黑盾、波兰四种几何、捷克两种中央符号；捷克可选四种既定配色（深蓝/红/金黄/橄榄灰）。波兰伞锚不在模板内。
- 自定义PNG、纹理声明、目标师引用联合预览、应用和备份恢复；草稿保存图片及编辑参数，原图保持。
- 设置新增面板独立弹出开关，默认大面板，可选折叠分组；双击标题打开，关闭归位。师徽编辑器始终独立弹出。
- 模板包含重构底图，非原版像素还原。自定义师徽尚未运行AssetCooker或游戏内验收。

## 1.9.6 — 2026-09-16

- 同版本界面修补：游戏规则按大类/小类两层折叠，保留展开状态与搜索；SP改为左右浏览编辑布局，支持窄窗口切换、明确引用跳转和未保存编辑保护。

- 对齐创建 Mod 与开发模式按钮；欢迎页增加创建新 Mod 入口。
- 战术师列表显示师徽，支持国家、阵营、师类型、草稿状态筛选；单位池数量单元格编辑时水平、垂直居中。
- 单位主页面以外的筛选统一通过居中窗口打开，保留选择并支持清空。
- 武器页“挂载”增加已有 NbWeapons 字段，沿用当前/所选单位与全部引用作用域及必要共享隔离。
- 独立战略 Pack（SP）模块可编辑现有 Pack 的单位、运输、老练度与名称，显示编制引用并可双击跳转。共享本体修改影响所有引用者；重命名同步引用并检查名称冲突。编制的数量与索引继续在将军模式编辑。
- 自动产生的 Pack 使用带 `_mod_` 标记的单位、运输和老练度名称，重名自动追加短序号；可手动生成可读名称。
- 游戏规则按对局、经济、AI、战斗行为、后勤、将军模式、空军七类组织，搜索可跨分类。
- 空军布局提供网格方案与卡片倍率，数量由行列相乘确定，联动三个界面文件。倍率相对当前正式文件中的卡片尺寸，重复预览不累乘；应用后重载以新尺寸为1×。连续起飞间隔、撤离开火及撤离高度仅在专业模式显示。
- 本轮通过合成数据事务/恢复与WPF界面检查；空军布局和NbWeapons的游戏内效果未在本轮实测。

## 1.9.5 — 2026-09-11

- 单位检查器显示肖像：宽栏小图、窄栏查看按钮，点击放大；优先当前 Mod 图片，其余从本机 WARNO 读取并缓存，缺图不影响编辑。
- 新增战术师模板创建及名称/已有师徽修改；新师独立注册规则、费用和默认牌组，名称与资源引用统一进入草稿事务。新师草稿可从创建窗口继续编辑，应用后可调整单位池等参数。
- 新建单位支持固定槽位逐槽选择当前 Mod 已有 Ammo、恢复母版，并显示共享弹药箱及可推导弹量；自动隔离改动的 Weapon，保留模型、动画与挂架。
- 修复新建单位独立 Weapon 与其他武器草稿一起应用时被误报为悬空引用的问题。
## 1.9.4 — 2026-09-10

- 移除武器页“替换为已有 Weapon”选择框与保存按钮；保留当前 Weapon、挂载选择及右侧已有 Ammo 替换字段。

## 1.9.3 — 2026-09-10

- 新增上次成功打开 Mod 的本机解析缓存，加快再次打开；缓存只保留最近一个 Mod，文件变化、程序更新或缓存损坏时自动重新加载。
- 设置→常规增加缓存开关和清除入口，默认开启，下次打开生效。游戏名称缓存独立保留。
- 最新草稿仍从当前 Mod 恢复；正式预览与应用继续重新读取、校验原文件。

## 1.9.2 — 2026-09-10

- 原版名称改为从本机 WARNO 安装提取并缓存，支持 EDat v2/v3；新版不再内嵌游戏名称快照，也不需要外部解包工具。
- 自动查找游戏，设置中可选择游戏目录及刷新名称；原版词典不可用时保留普通参数编辑，名称创建/修改需先加载词典。
- 将军模式列表统一名称样式、留白和对齐，列序为名称、国家、类型；类型来自当前 Mod 的战略地图图标，支持中英文，未知值保留。

## 1.9.1 — 2026-09-09

- 列表增加行高和留白，文字垂直居中，改善单位页拥挤。
- 自动列宽依据当前筛选结果的实际内容分配，空间允许时以至少90%的条目完整显示为目标；窄窗口保留可读列宽与横向滚动。
- 左侧功能模块恢复备注说明，模块项和侧栏适当加大。
- 工作区内容沿面板圆角收边；软件窗口普通状态采用圆角，最大化贴屏，还原后恢复。

## 1.9.0 — 2026-09-09

- 重做正式编辑器主框架：项目顶栏、紧凑导航和全局草稿入口；设置与模式移至左下角。
- 首次启动及无效主题配置默认白蓝，保留七套主题和已有个人设置。
- 单位、武器、弹药使用统一字段行，名称、原参数和输入对齐。
- 单位多选在同一个右侧区域切换为批量编辑；混合值输入留空，避免误写为零。
- 草稿总览集中提供批次、部分应用和事务备份/恢复；单位创建、引用隔离、战术师、将军模式、游戏规则和专业工作台继续可用。

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

## 1.9.11 — 2026-09-20

- Added a dedicated weapon batch window for selecting actual mounts across units and Weapon descriptors, with complete filtered selection, search, Mod-derived facets and hidden-selection counts.
- Supports existing inventory, mount count, visibility, Ammo replacement, turret angles, ammunition performance and behavior fields. Includes fixed/percentage edits, advanced arithmetic, explicit rounding, complete result previews and shared impact details.
- Shared ammo boxes and turrets are calculated once. Local Ammo edits isolate selected mounts; identical final configurations share copies and distinct unit values remain independent. Local copies inherit shared edits to other fields.
- Versioned batch drafts, explicit overlap replacement, dependency-aware apply, baseline/reference revalidation and recoverable multi-file transactions. New parameter edits use the final replaced Ammo; incompatible older Ammo parameter batches must be removed and previewed again.
- Apply pending unit creation before batch edits; units pending deletion cannot be edited. Preserves the 1.9.10 picture feature. No real Mods, official generator or game were used for validation.

## 1.9.10 — 2026-09-19

- Added “Change unit picture” to the unit inspector: choose an existing picture from the current Mod, or import PNG/JPEG and crop, zoom or edit circular transparency in a separate window. Aspect ratio and transparency are preserved; emblem templates are hidden.
- Existing and pending units support picture drafts and reopened previews. Custom pictures receive independent PNG files and button texture keys, affecting only the selected unit.
- PNG, texture declaration and unit reference changes use the same recoverable apply workflow, including combined creation/rename operations, conflict checks and failure recovery.
- Unit models and skins are unchanged. Generate the Mod after applying. Generator and in-game display checks have not been performed for this release.

## 1.9.9 — 2026-09-18

- New units inherit their template's verified registration path and receive sequential readable names. Professional mode can rename pending and applied units with linked reference updates, preserving GUIDs, name tokens and registration IDs. Existing registration errors can be repaired separately.
- Traits and actual abilities provides 15 trait families, including instructor/IFV roles and valid SIGINT variants. Professional mode can add, replace, remove or clear resolvable abilities from this Mod, independently of display labels.
- Cancel pending creation or delete an applied unit created by this tool after provenance checks. Reference cleanup or replacement is previewed; shared resources remain and deleted IDs are not automatically reused. Deletion drafts can be undone, and committed deletions use file-level backup restoration.
- All three features share drafts, combined previews and transactions with existing unit and experience edits. Inputs, dependencies and drafts are rechecked before commit; ambiguous or unsupported structures block affected writes.
- Validation covers synthetic transactions, rollback and WPF UI checks. No official generator or in-game validation was performed, and no real Mod was modified.

## 1.9.8 — 2026-09-17

- Game Rules adds Experience and veterancy, organized by category, route and level, with search and Chinese/English support in both editor modes.
- Edit existing level thresholds and supported numeric effects from the selected Mod. Modifier types, unknown effects, tags and untouched text are preserved; shared users and sources are listed.
- Level edits use drafts and combined multi-file previews, backups and rollback. Shared impact is rechecked before committing; route selection and numeric edits can be applied together.
- Missing SF level-zero effects, empty aircraft effects and actual custom level structures remain visible. Multiply referenced or unsupported effects are read-only; missing effects are not inserted.
- Private unit routes, expanded game level limits and automatic in-game hint updates are outside this release. Engine experience formulas have not been tested in-game.

## 1.9.7 patch1 — 2026-09-17

- Fix cross-thread access when opening the image editor or templates with an existing emblem loaded in the background. PNG encoding preserves pixels and alpha without reading decoder metadata across threads.

## 1.9.7 — 2026-09-16

- Choose valid experience packs from the current Mod in basic mode and inspect level effects.
- Retain existing-emblem selection with a searchable thumbnail gallery. Import PNG/JPEG images into a separate zoomable editor with crop, circular transparency, undo and reset.
- Ten numbered templates cover Soviet, East German, Polish geometric and Czech designs; Czech shields offer four color presets. Polish parachute/anchor designs are excluded.
- Image data and editing parameters persist in drafts. New PNG files, texture declarations and division references share transaction preview, backup and recovery.
- Enable detachable panels in Settings. Double-click workspace or expandable-group titles; close windows to dock them again.
- Some template bases are reconstructed. AssetCooker and in-game validation have not been performed for the new emblem workflow.

## 1.9.6 — 2026-09-16

- UI patch, version unchanged: nested rule categories with retained expansion/search; side-by-side Pack browsing and editing, compact list/detail navigation, explicit reference navigation and unsaved-edit protection.

- Align Mod creation buttons and add Create Mod to the welcome panel.
- Show division emblems; add country, coalition, type and draft filters; center quantity editors.
- Open filters outside the main Unit page in centered windows with retained selections and a clear action.
- Edit existing NbWeapons under weapon mounting, with the existing scope and isolation workflow.
- Add a separate strategic Pack workspace for unit, transport, experience and identifier editing, reference inspection and formation navigation. Shared edits affect all users; renaming updates references and checks collisions.
- Generate readable Pack identifiers with a mod marker and short collision suffixes.
- Organize rules into seven categories, including Air. Air capacity uses grid presets and linked card/panel sizing across three files. Card multipliers use current saved dimensions; repeated previews do not compound.
- Takeoff interval, firing during evacuation and evacuation altitude are professional-only settings.
- Synthetic transaction/recovery and WPF checks passed. In-game air layout and NbWeapons effects remain unverified in this release cycle.

## 1.9.5 — 2026-09-11

- Unit portraits appear beside the inspector heading, with a compact button in narrow panes and click-to-enlarge. Mod images take priority; official images are read and cached locally from WARNO. Missing images do not block editing.
- Create tactical divisions from templates and edit names or existing emblems. New divisions receive independent rules, costs and default decks; identity changes are saved as transactional drafts. Reopen pending creations in the creation window, then edit roster parameters after applying.
- New units can select existing Ammo for each fixed template slot, reset individual slots and see shared ammo boxes and calculated ammunition. Changed Weapons are isolated automatically; models, animations and mounts are preserved.
- Fix false dangling-Weapon errors when applying newly created units together with other weapon drafts.
## 1.9.4 — 2026-09-10

- Removed the whole-Weapon replacement picker and save button. Current Weapon and mount selection, and the existing Ammo replacement field, remain available.

## 1.9.3 — 2026-09-10

- Cache parsed data for the last successfully opened Mod. File changes, application updates or a damaged cache trigger a normal reload.
- Enable, disable or clear the cache under Settings → General. Enabled by default; changes apply on next open. The original-name cache remains separate.
- Restore current drafts from the selected Mod. Formal preview and apply continue to reread and validate source files.

## 1.9.2 — 2026-09-10

- Read and cache original names from the local WARNO installation, supporting EDat v2/v3. New packages no longer embed game-name snapshots or require extraction tools.
- Locate the game automatically, or choose its folder and refresh names in settings. Ordinary parameter editing remains available without dictionaries; load names before creating or changing names.
- Align Army General list styling and add a Type column after Country. Types follow the Mod’s strategic map-symbol categories, with Chinese/English labels and unknown values preserved.

## 1.9.1 — 2026-09-09

- More spacious list rows with vertically centered text, especially in the unit list.
- Columns size to the filtered content, targeting at least 90% fully readable entries when space permits. Narrow layouts retain minimum widths and horizontal scrolling.
- Module notes return to a larger navigation sidebar.
- Workspace content follows rounded panel edges. The application window is rounded when restored and fills the screen when maximized.

## 1.9.0 — 2026-09-09

- Reworked the production editor shell with compact navigation and global draft access; settings and mode controls now sit at the bottom left.
- Fresh or invalid theme settings default to white-blue. All seven themes and valid saved preferences are preserved.
- Units, Weapons, and Ammunition share aligned field rows with original parameter notes.
- Selecting multiple units switches the existing right inspector to batch editing. Mixed-value inputs remain empty until a target value is entered.
- Draft Overview centralizes grouped changes, partial application, and transaction backups. Existing creation, reference isolation, division, Army General, rules, and professional tools remain available.

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
