# WARNO Lite Modding Tool

[中文](#chinese) | [English](#english)

<a id="chinese"></a>

面向新手和 Mod 作者的 **Windows WARNO 图形化编辑器**。通过界面调整单位、武器、弹药、战术师、将军模式和游戏规则，无需手写代码；支持单位卡片图片和师徽编辑。

当前版本：**1.9.15** · Windows x64 · 便携运行

**[下载发布包](https://github.com/USN-Enterprise/WARNO-Lite-Modding-Tool/releases/latest)** · **[中文使用教程](使用教程.md)** · [版本更新](RELEASE_NOTES.md#chinese)

## 主要功能

| 模块 | 可以做什么 |
| --- | --- |
| 单位 | 编辑数值、名称、经验类型与能力，批量修改，从模板创建单位，修改卡片图片 |
| 武器与弹药 | 编辑挂载、炮塔和弹药参数，按单位隔离修改，或编辑共享弹药并查看全部使用者 |
| 战术师 | 调整单位池、运输和费用，从模板创建战术师，修改名称、师徽与简介 |
| 将军模式与战略 Pack | 编辑现有营编制、棋子属性，以及共享 Pack 的单位、运输和老练度 |
| 游戏规则 | 调整对局、经济、AI、战斗、后勤、将军模式、空军、经验路线及地形规则 |

提供搜索筛选、基础/专业模式、中英文界面、主题与面板调整；修改通过草稿、预览和备份流程应用。

## 快速开始

1. 下载完整的 Windows x64 发布 ZIP，解压后运行 `WarnoLiteModdingTool.exe`。无需安装 .NET SDK 或 Python。
2. 使用“自动查找 Mod”，或打开包含 `GameData` 的 **Mod 根目录**；没有 Mod 时可从“Mod 工具”创建。
3. 选择对象并编辑。普通字段自动保存草稿；带“加入草稿”或“保存草稿”的操作需点击相应按钮。
4. 打开“草稿总览”，检查修改对象、前后值和影响范围，再预览并应用。
5. 点击“生成 / 编译 Mod”，检查生成结果，然后在游戏中启用并验证。

第一次使用建议跟着[完整修改示例](使用教程.md#first-edit)操作；各模块步骤、批量修改、备份恢复和排错见[使用教程](使用教程.md)。

## 使用前了解

- 功能取决于所选 Mod 的文件和结构；缺少某类资料只影响相关功能。编辑器可放在游戏目录之外。
- 原版名称、图片与师简介从本机 WARNO 提取，不随工具分发；找不到游戏时可在设置中指定目录。
- **武器页**可限定当前或所选单位；**弹药页**修改共享本体，会影响全部使用者。
- **保存草稿、应用文件和生成 Mod 是三个步骤**。应用时创建备份，游戏内效果仍需生成后检查。
- 单位与战术师支持模板创建；单位删除仅支持来源可核对的本工具创建单位。不提供模型/动画制作、任意武器槽增删或战役地图/剧情编辑。详见[当前边界](使用教程.md#limits)。

## 文档与交流

- [中文使用教程](使用教程.md) · [English user guide](USER_GUIDE.md)
- [发布说明](RELEASE_NOTES.md) · [开发与构建](DEVELOPMENT.md)
- **QQ 交流群：1013181135**。反馈问题时请附工具版本、操作步骤和“问题”中的完整诊断。

## 许可证

项目原创代码与文档采用 [MIT 许可证](LICENSE)。游戏资料不属于本项目授权范围。图片解码使用 [ZstdSharp.Port（MIT）](licenses/ZstdSharp-MIT.txt)。

---

<a id="english"></a>

**A Windows graphical editor for WARNO Mods**, for beginners and Mod authors. Edit units, weapons, ammunition, divisions, Army General and game rules without writing code. Unit portraits and division emblems can also be edited.

Current version: **1.9.15** · Windows x64 · Portable

**[Download a release](https://github.com/USN-Enterprise/WARNO-Lite-Modding-Tool/releases/latest)** · **[English user guide](USER_GUIDE.md)** · [Release notes](RELEASE_NOTES.md#english)

## Features

| Module | What you can do |
| --- | --- |
| Units | Edit stats, names, experience types and abilities; batch edit; create units from templates; change portraits |
| Weapons and ammunition | Edit mounts, turrets and ammunition; isolate changes to selected units or edit shared ammunition and inspect all users |
| Divisions | Adjust unit pools, transport and costs; create divisions from templates; edit names, emblems and descriptive text |
| Army General and Strategic Packs | Edit existing battalion formations, pawn properties and the units, transport and veterancy of shared Packs |
| Game rules | Adjust match, economy, AI, combat, logistics, Army General, air, experience-route and terrain settings |

Includes search and filters, basic/professional modes, Chinese/English UI, themes and adjustable panels. Changes use a draft, preview and backup workflow.

## Quick start

1. Download and extract the complete Windows x64 release ZIP. Run `WarnoLiteModdingTool.exe`; no .NET SDK or Python installation is needed.
2. Use automatic Mod discovery or open the **Mod root folder** containing `GameData`. To create a Mod, use Mod Tools.
3. Select an object and edit it. Ordinary fields save drafts automatically; actions with **Add to drafts** or **Save draft** require that button.
4. Open **Draft overview**, check the objects, before/after values and affected scope, then preview and apply.
5. Use **Generate / compile Mod**, check the result, then enable and test the Mod in the game.

Start with the [first-edit walkthrough](USER_GUIDE.md#first-edit). Module instructions, batch editing, restoration and troubleshooting are in the [user guide](USER_GUIDE.md).

## Before you start

- Available features depend on the selected Mod's files and structure. Missing data affects the relevant features only. The editor can run outside the game folder.
- Original names, images and division text are extracted from your local WARNO installation and are not bundled. Set the game folder in Settings if discovery fails.
- **Weapons** can limit changes to the current or selected units. **Ammunition** edits shared objects and affects every user.
- **Saving drafts, applying files and generating the Mod are separate steps.** Applying creates backups; game effects still need checking after generation.
- Units and divisions support template creation. Unit deletion is limited to verifiable units created by this tool. Model/animation authoring, arbitrary weapon-slot changes and campaign map/story editing are not provided. See [current limits](USER_GUIDE.md#limits).

## Documentation and community

- [English user guide](USER_GUIDE.md) · [中文使用教程](使用教程.md)
- [Release notes](RELEASE_NOTES.md#english) · [Development and building](DEVELOPMENT.md#english)
- **QQ group: 1013181135**. For issue reports, include the tool version, steps to reproduce and the full diagnostic from Problems.

## License

Original project code and documentation use the [MIT license](LICENSE). Game content is outside this license. Image decoding uses [ZstdSharp.Port (MIT)](licenses/ZstdSharp-MIT.txt).
