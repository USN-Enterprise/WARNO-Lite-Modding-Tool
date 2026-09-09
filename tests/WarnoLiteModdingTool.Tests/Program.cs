using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Localisation;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Transactions;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Batch;
using WarnoLiteModdingTool.App;
using WarnoLiteModdingTool.App.Diagnostics;
using WarnoLiteModdingTool.App.Theming;
using WarnoLiteModdingTool.App.ViewModels;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WpfApplication = WarnoLiteModdingTool.App.App;
using System.Diagnostics;
using System.Text;

namespace WarnoLiteModdingTool.Tests;

internal static partial class Program
{
    private static readonly NdfTopLevelScanner Scanner = new();

    private static async Task<int> Main(string[] args)
    {
        if (args is ["--single-instance-probe", var mutexName, var mode]) return SingleInstanceProbe(mutexName, mode);
        if (args is ["--duplicate-startup-probe"]) return DuplicateStartupProbe();
        if (args is ["--scan-rules", var rulesPath]) { var rules=Core.Rules.RuleWorkspace.Load(rulesPath); foreach(var g in rules.Groups) Console.WriteLine($"{g.Definition.Number}. {g.Definition.Label}: {(g.CanEdit ? string.Join(", ",g.Cells.Select(c=>c.Raw)) : g.Error)}"); return rules.Groups.All(g=>g.CanEdit)?0:1; }
        if (args is ["--scan-strategic", var strategicPath])
        {
            var data = await ReadStrategic(strategicPath);
            Console.WriteLine($"Strategic records: {data.Records.Count}; packs: {data.Packs.Count}; groups: {data.CombatGroups.Count}; decks: {data.Decks.Count}; editable rosters: {data.Records.Count(r => r.Error is null)}");
            foreach (var error in data.Diagnostics.Take(12)) Console.WriteLine(error);
            return 0;
        }
        if (args is ["--scan", var projectPath])
        {
            return await ScanOptionalSample(projectPath);
        }

        if (args is ["--scan-p2", var p2ProjectPath])
        {
            return await ScanOptionalP2Sample(p2ProjectPath);
        }

        if (args is ["--scan-p5", var p5ProjectPath])
        {
            return await ScanOptionalP5Sample(p5ProjectPath);
        }

        if (args is ["--preview-p6", var p6ProjectPath])
        {
            return await PreviewOptionalP6Sample(p6ProjectPath);
        }

        if (args is ["--preview-p3", var p3ProjectPath])
        {
            return await PreviewOptionalP3Sample(p3ProjectPath);
        }

        if (args is ["--preview-p4", var p4ProjectPath])
        {
            return await PreviewOptionalP4Sample(p4ProjectPath);
        }

        var tests = new (string Name, Func<Task> Run)[]
        {
            ("185常量扫描与真正语法错误", ScannerConstants185),
            ("184全局规则事务与关联边界", RulesTransaction184),
            ("184视野比例与特性补建", UnitVisionAndSpecialties184),
            ("部分应用与部分删除保留未选草稿", PartialDraftApplyAndDelete),
            ("战略显式索引映射与重叠拒绝", StrategicExplicitIndexMapping),
            ("固定减法与原版词典及显示规则", FixedSubtractAndNames),
            ("新增单位身份、独立武器及师规则事务",UnitCreationTransaction),
            ("共用名称token独立改名及部分应用", SharedNameIsolation),
            ("182弹药名称独立事务",AmmoNameTransaction182),
            ("182战略可读差异",StrategicDiff182),
            ("战略编制共享隔离、名称、增减排序与无损事务", StrategicCompositionTransaction),
            ("战略草稿拒绝无效值与基线冲突", StrategicRejectsInvalidAndConflictingEdits),
            ("战略事务失败回滚与语言设置持久化", StrategicRollbackAndSettings),
            ("扫描器忽略注释、字符串和嵌套括号", ScannerIgnoresTriviaAndNestedDelimiters),
            ("扫描器报告未闭合对象", ScannerReportsTruncatedObject),
            ("扫描器保留混合换行行号与 UTF-8 字节偏移", ScannerTracksLinesAndUtf8Offsets),
            ("扫描器接受命名 MAP 并报告未闭合方括号", ScannerHandlesNamedMaps),
            ("完整夹具探测四个模块", DetectorFindsCompleteFixture),
            ("缺文件夹具只降级相关模块", DetectorDegradesMissingModules),
            ("本地化目录名动态发现", DetectorFindsCustomLocalisationName),
            ("字段元数据提供大类小类并更名将军价格", FieldMetadataProvidesMajorAndMinorGroups),
            ("官方三个 Mod 命令按依赖独立探测", DetectorProbesOfficialCommandsIndependently),
            ("新建 Mod 限制英数名称并验证临时假 BAT", CreateModValidatesNameAndRunsFakeOfficialScript),
            ("问题日志区分 Mod 与工具并恢复闪退编号", ProblemLogSeparatesSourcesAndRecoversFatal),
            ("语法边界夹具可完整索引", IndexerHandlesSyntaxFixture),
            ("索引器读取夹具但不写入", IndexerIsReadOnly),
            ("异常文件不阻断其他能力报告", IndexerContainsMalformedFile),
            ("最近项目可添加和移除", RecentProjectsRoundTrip),
            ("P2 字段读取定位 MAP 与嵌套参数", P2FieldReaderLocatesMapAndNestedArgument),
            ("P2 单位投影读取字段、名称与引用", P2WorkspaceReadsFieldsNamesAndReferences),
            ("P2 缺失与常量字段保持只读", P2WorkspaceReportsUnavailableFields),
            ("P2 CSV 支持分号与转义引号", P2CsvReaderHandlesQuotedValues),
            ("P2 草稿持久化与恢复不修改正式文件", P2DraftRoundTripPreservesFormalFiles),
            ("P2 草稿冲突不会静默恢复", P2DraftResolverDetectsConflict),
            ("P2 清空草稿保留未知文件", P2DraftClearPreservesUnknownFiles),
            ("P2 重复名称 token 禁用名称编辑", P2DuplicateNameTokenDisablesEditing),
            ("P2 多个 UNITS 目标单独降级名称编辑", P2MultipleUnitsCsvTargetsDisableEditing),
            ("P2 未知草稿版本保留原文件", P2UnknownDraftVersionIsPreserved),
            ("P3 应用 Unit 与名称并生成备份日志", P3AppliesUnitAndNameWithBackupAndLog),
            ("P3 名称新 token 同事务写入 NDF 与 CSV", P3AppliesGeneratedNameTokenAtomically),
            ("P3 预览后外部修改阻止提交", P3RejectsExternalChangeAfterPreview),
            ("P3 中途提交失败回滚已写文件", P3RollsBackCommittedFilesOnFailure),
            ("P3 恢复回到应用前状态并建立恢复备份", P3RestoresApplicationBackup),
            ("P3 保留 CSV UTF-8 BOM 与转义文本", P3PreservesCsvEncodingAndEscaping),
            ("P3 拒绝隐式归一化带 BOM 的 NDF", P3RejectsNdfWithBom),
            ("P3 拒绝被篡改的草稿目标原值", P3RejectsTamperedDraftTarget),
            ("P3 可在严格模块锚点间补建前置部署", P3InsertsOptionalDeploymentModule),
            ("P3 护甲族与伤害族索引按当前 Mod 边界校验", P3ValidatesResistanceAndDamageFamilies),
            ("P4 解析 AmmoBox、挂载和共享反向引用", P4ReadsWeaponAmmoRelationships),
            ("P4 仅当前 Unit 修改共享 Ammo 生成最小隔离链", P4IsolatesAmmoForCurrentUnit),
            ("P4 所选 Unit 覆盖 Weapon 全部引用者时不克隆 Weapon", P4SelectedUnitsReuseExclusiveWeapon),
            ("P4 全部引用直接修改共享 Ammo", P4EditsSharedAmmoGlobally),
            ("P4 局部 Salves 修改只克隆共享 Weapon", P4IsolatesWeaponField),
            ("P4 现有 Ammo 替换只隔离共享 Weapon", P4ReplacesExistingAmmo),
            ("P4 现有 Weapon 可替换全部 Unit 引用", P4ReplacesExistingWeaponReferences),
            ("P4 隔离修改可事务提交并产生备份日志", P4CommitsIsolationTransaction),
            ("P5 读取师、单位池、默认 Deck 与费用曲线", P5ReadsDivisionWorkspace),
            ("P5 规划师设置、规则与费用但不编辑默认卡组", P5PlansCompleteDivisionEdit),
            ("P5 旧默认卡组草稿转为明确冲突", P5RejectsLegacyDefaultDeckEdit),
            ("P5 拒绝同师重复 UnitRule", P5RejectsDuplicateUnitRule),
            ("P5 运输候选按结构识别并拒绝非运输新增", P5ValidatesTransportCapability),
            ("P5 跳过其他用途描述符只作为兼容提示", P5ReportsSkippedDescriptorsAsInformation),
            ("P5 拒绝默认 Deck 超出激活点上限", P5RejectsActivationOverflow),
            ("P5 战术师草稿可事务提交并重载", P5CommitsDivisionTransaction),
            ("P6 多选固定值生成普通 Unit 草稿与影响预览", P6SetsSelectedUnitsAndReportsImpact),
            ("P6 乘加与百分比公式按显示值计算", P6CalculatesNumericFormulas),
            ("P6 三种取整与上下限顺序稳定", P6AppliesRoundingAndBounds),
            ("P6 已有草稿作为当前值并可批量恢复基线", P6UsesDraftValueAndCanRestoreBaseline),
            ("P6 无效目标原子拒绝且批量草稿一次持久化", P6RejectsInvalidTargetsAndPersistsAtomically),
            ("1.8.7 单实例进程竞争与启动弹窗", SingleInstance187),
            ("1.8.6 F3 全模块离开保存与失败重试", DraftLifecycle186),
            ("1.8.6 表格列宽分配", ColumnAllocation186),
            ("WPF 窗口支持双主题、筛选标注、草稿总览、Ammo 直编与项目打开", WpfWindowSwitchesThemesAndOpensProjects)
        };

        if (args is ["--test", var filter]) tests = tests.Where(t => t.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();
        var failed = 0;
        foreach (var test in tests)
        {
            try
            {
                await test.Run();
                Console.WriteLine($"PASS  {test.Name}");
            }
            catch (Exception exception)
            {
                failed++;
                Console.Error.WriteLine($"FAIL  {test.Name}");
                Console.Error.WriteLine($"      {exception}");
            }
        }

        Console.WriteLine();
        Console.WriteLine($"结果：{tests.Length - failed}/{tests.Length} 通过");
        return failed == 0 ? 0 : 1;
    }

    private static async Task<int> ScanOptionalSample(string projectPath)
    {
        var context = new ModProjectDetector().Detect(projectPath);
        if (!context.IsRecognized)
        {
            Console.Error.WriteLine(context.Summary);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        var result = await new ProjectIndexer().IndexAsync(context);
        stopwatch.Stop();

        foreach (var module in result.Modules)
        {
            Console.WriteLine($"{module.DisplayName}: {module.Summary}");
        }

        Console.WriteLine($"总对象：{result.Objects.Count:N0}");
        Console.WriteLine($"诊断：{result.Diagnostics.Count:N0}");
        Console.WriteLine($"耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        return result.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static async Task<int> ScanOptionalP2Sample(string projectPath)
    {
        var context = new ModProjectDetector().Detect(projectPath);
        if (!context.IsRecognized)
        {
            Console.Error.WriteLine(context.Summary);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        var index = await new ProjectIndexer().IndexAsync(context);
        var workspace = await new UnitProjectLoader().LoadAsync(context, index);
        stopwatch.Stop();

        Console.WriteLine($"Unit：{workspace.Units.Count:N0}");
        Console.WriteLine($"字段定义：{UnitFieldDefinitions.All.Count:N0}");
        foreach (var availability in Enum.GetValues<UnitFieldAvailability>())
        {
            var count = workspace.Units.SelectMany(unit => unit.Fields).Count(field => field.Availability == availability);
            Console.WriteLine($"{availability}：{count:N0}");
        }

        Console.WriteLine($"名称可编辑：{workspace.Units.Count(unit => unit.CanEditName):N0}");
        Console.WriteLine($"P2 诊断：{workspace.Diagnostics.Count:N0}");
        Console.WriteLine($"总耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        return index.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static async Task<int> ScanOptionalP5Sample(string projectPath)
    {
        var context = new ModProjectDetector().Detect(projectPath);
        if (!context.IsRecognized)
        {
            Console.Error.WriteLine(context.Summary);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        var workspace = await new DivisionProjectLoader().LoadAsync(context, index, units);
        stopwatch.Stop();
        var invalid = workspace.Divisions
            .Select(division => (Division: division, Errors: DivisionStateValidator.Validate(workspace, division, division.Baseline)))
            .Where(item => item.Errors.Count > 0)
            .ToArray();
        Console.WriteLine($"Division：{workspace.Divisions.Count:N0}");
        Console.WriteLine($"可编辑：{workspace.Divisions.Count(item => item.CanEdit):N0}");
        Console.WriteLine($"DeckPack：{workspace.DeckPacks.Count:N0}");
        Console.WriteLine($"UnitRule：{workspace.Divisions.Sum(item => item.Baseline.UnitRules.Count):N0}");
        Console.WriteLine($"默认 Deck Pack 引用：{workspace.Divisions.Sum(item => item.Baseline.DefaultDeck.Count):N0}");
        Console.WriteLine($"基线校验问题师：{invalid.Length:N0}");
        foreach (var item in invalid.Take(8))
        {
            Console.WriteLine($"  {item.Division.Name}: {string.Join("；", item.Errors.Take(2))}");
        }

        Console.WriteLine($"P5 诊断：{workspace.Diagnostics.Count:N0}");
        foreach (var diagnostic in workspace.Diagnostics.Take(8))
        {
            Console.WriteLine($"  {diagnostic}");
        }
        Console.WriteLine($"总耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        return index.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error) ? 1 : 0;
    }

    private static async Task<int> PreviewOptionalP6Sample(string projectPath)
    {
        var context = new ModProjectDetector().Detect(projectPath);
        if (!context.IsRecognized)
        {
            Console.Error.WriteLine(context.Summary);
            return 2;
        }

        var stopwatch = Stopwatch.StartNew();
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        var preview = UnitBatchPlanner.Preview(new UnitBatchRequest(
            "optional-read-only",
            units.Units,
            "economy.commandPoints",
            UnitBatchOperation.Multiply,
            "1.05",
            UnitBatchRounding.Nearest,
            "0",
            null,
            []));
        stopwatch.Stop();

        Console.WriteLine($"目标 Unit：{preview.TargetCount:N0}");
        Console.WriteLine($"兼容字段：{preview.CompatibleCount:N0}");
        Console.WriteLine($"将变化：{preview.ChangedCount:N0}");
        Console.WriteLine($"不变化/不可编辑：{preview.UnchangedCount:N0}");
        Console.WriteLine($"共享影响：师 {preview.Impact.DivisionCount:N0} / Weapon {preview.Impact.WeaponCount:N0} / Ammo {preview.Impact.AmmoCount:N0}");
        Console.WriteLine($"警告：{preview.Warnings.Count:N0}；错误：{preview.Errors.Count:N0}");
        foreach (var sample in preview.Samples)
        {
            Console.WriteLine($"样本：{sample.DisplayName} {sample.CurrentValue} → {sample.TargetValue}");
        }

        Console.WriteLine($"总耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        return preview.Errors.Count == 0 ? 0 : 1;
    }

    private static async Task<int> PreviewOptionalP3Sample(string projectPath)
    {
        var root = Path.GetFullPath(projectPath);
        var editorDirectory = Path.Combine(root, ".warno-editor");
        var editorExisted = Directory.Exists(editorDirectory);
        var stopwatch = Stopwatch.StartNew();
        var workspace = await LoadWorkspaceAsync(root);
        var pair = workspace.Units
            .SelectMany(unit => unit.Fields.Select(field => (Unit: unit, Field: field)))
            .First(item => item.Field.CanEdit && item.Field.Definition.ValueKind == UnitValueKind.Integer &&
                           int.TryParse(item.Field.DisplayValue, out _));
        var target = (int.Parse(pair.Field.DisplayValue) + 1).ToString();
        var draft = CreateFieldDraft(pair.Unit, pair.Field, target, target);
        var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
        stopwatch.Stop();

        Console.WriteLine($"Unit：{workspace.Units.Count:N0}");
        Console.WriteLine($"预览操作：{preview.Operations.Count:N0}");
        Console.WriteLine($"正式候选文件：{preview.FormalFileCount:N0}");
        Console.WriteLine($"候选字节：{preview.Files.Sum(item => item.CandidateBytes.LongLength):N0}");
        Console.WriteLine($"验证项：{preview.ValidationMessages.Count:N0}");
        Console.WriteLine($"总耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        Console.WriteLine($"文件写入：{(editorExisted == Directory.Exists(editorDirectory) ? "无" : "检测到异常")}");
        return editorExisted == Directory.Exists(editorDirectory) ? 0 : 1;
    }

    private static async Task<int> PreviewOptionalP4Sample(string projectPath)
    {
        var root = Path.GetFullPath(projectPath);
        var editorDirectory = Path.Combine(root, ".warno-editor");
        var editorExisted = Directory.Exists(editorDirectory);
        var stopwatch = Stopwatch.StartNew();
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        var weapons = await new WeaponProjectLoader().LoadAsync(context, index, units);
        var pair = weapons.Ammunition
            .Where(ammo => (weapons.References.AmmoUnits.GetValueOrDefault(ammo.Name) ?? []).Count > 1)
            .Select(ammo => (Ammo: ammo, Field: ammo.Field("ammo.damage.physical")))
            .First(item => item.Field is not null);
        var target = (double.Parse(pair.Field!.DisplayValue, System.Globalization.CultureInfo.InvariantCulture) + 0.1)
            .ToString("G15", System.Globalization.CultureInfo.InvariantCulture);
        var unit = weapons.References.AmmoUnits[pair.Ammo.Name][0];
        var weapon = weapons.References.AmmoWeapons[pair.Ammo.Name]
            .First(name => (weapons.References.WeaponUnits.GetValueOrDefault(name) ?? []).Contains(unit, StringComparer.Ordinal));
        var draft = CreateWeaponDraft(pair.Field, target, DraftEditScope.CurrentUnit, [unit], weapon);
        var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
        stopwatch.Stop();

        Console.WriteLine($"Weapon：{weapons.Weapons.Count:N0}");
        Console.WriteLine($"MountedWeapon：{weapons.Weapons.Sum(item => item.Mounts.Count):N0}");
        Console.WriteLine($"Ammo：{weapons.Ammunition.Count:N0}");
        Console.WriteLine($"Ammo 可编辑字段：{weapons.Ammunition.Sum(item => item.Fields.Count):N0}");
        Console.WriteLine($"预览对象：{pair.Ammo.Name} / {unit}");
        Console.WriteLine($"影响：{weapons.References.AmmoWeapons[pair.Ammo.Name].Count:N0} Weapon / {weapons.References.AmmoUnits[pair.Ammo.Name].Count:N0} Unit");
        Console.WriteLine($"正式候选文件：{preview.FormalFileCount:N0}");
        Console.WriteLine($"验证项：{preview.ValidationMessages.Count:N0}");
        Console.WriteLine($"总耗时：{stopwatch.Elapsed.TotalSeconds:F2} 秒");
        Console.WriteLine($"文件写入：{(editorExisted == Directory.Exists(editorDirectory) ? "无" : "检测到异常")}");
        return editorExisted == Directory.Exists(editorDirectory) ? 0 : 1;
    }

    private static Task ScannerIgnoresTriviaAndNestedDelimiters()
    {
        const string source = """
            // export FakeInComment is TBad()
            export First_Object is TThing
            (
                Text = "export FakeInString is TBad())"
                Other = 'escaped \' quote ( still text'
                Values = [ TChild(Value = (1, 2)), ] // ) ignored
            )
            export Second_Object is TOther
            (
                Value = MAP [ ("x", [1, 2]) ]
            )
            """;

        var result = Scanner.Scan(source, "edge.ndf", "units");
        TestAssert.Equal(2, result.Objects.Count, "只能识别两个真实顶层对象");
        TestAssert.Equal("First_Object", result.Objects[0].Name, "第一个对象名称不符");
        TestAssert.Equal("Second_Object", result.Objects[1].Name, "第二个对象名称不符");
        TestAssert.Equal(0, result.Diagnostics.Count, "有效语法不应产生诊断");
        return Task.CompletedTask;
    }

    private static Task ScannerReportsTruncatedObject()
    {
        const string source = "export Broken is TThing\n(\n Value = [1, 2]\n";
        var result = Scanner.Scan(source, "broken.ndf", "units");
        TestAssert.Equal(0, result.Objects.Count, "未闭合对象不能进入索引");
        var diagnostic = TestAssert.Single(result.Diagnostics, "未闭合对象应产生一条诊断");
        TestAssert.Equal(NdfDiagnosticSeverity.Error, diagnostic.Severity, "未闭合对象应为错误");
        return Task.CompletedTask;
    }

    private static Task ScannerTracksLinesAndUtf8Offsets()
    {
        const string source = "// 中文\r\n\r\nexport One is TThing()\nexport Two is TThing\r\n(\r\n)";
        var result = Scanner.Scan(source, "mixed.ndf", "units");
        TestAssert.Equal(2, result.Objects.Count, "混合换行应扫描两个对象");
        TestAssert.Equal(3, result.Objects[0].LineNumber, "第一个对象行号不符");
        TestAssert.Equal(4, result.Objects[1].LineNumber, "第二个对象行号不符");
        TestAssert.True(
            result.Objects[0].ByteOffset > result.Objects[0].CharacterOffset,
            "中文前缀后 UTF-8 字节偏移应大于字符偏移");
        return Task.CompletedTask;
    }

    private static Task ScannerHandlesNamedMaps()
    {
        const string valid = "MatrixCostName_Test_multi is MAP [\n (Factory/Infantry, 1),\n]\nexport Real is TThing()";
        var result = Scanner.Scan(valid, "maps.ndf", "divisions");
        TestAssert.Equal(1, result.Objects.Count, "命名 MAP 不应被当成圆括号对象，也不应吞掉后续对象");
        TestAssert.Equal(0, result.Diagnostics.Count, "合法命名 MAP 不应产生警告");

        var broken = Scanner.Scan("Broken is MAP [\n (A, 1),\n", "maps.ndf", "divisions");
        TestAssert.True(broken.Diagnostics.Any(item => item.Message.Contains("方括号未闭合", StringComparison.Ordinal)), "未闭合命名 MAP 仍应产生诊断");
        return Task.CompletedTask;
    }

    private static Task DetectorFindsCompleteFixture()
    {
        var context = new ModProjectDetector().Detect(Fixture("minimal-complete"));
        TestAssert.True(context.IsRecognized, "完整夹具应识别为 Mod");
        TestAssert.Equal(4, context.Modules.Count(module => module.CanScan), "四个模块都应可扫描");
        TestAssert.Equal(1, context.LocalisationDictionaries.Count, "应发现一个本地化声明");
        TestAssert.Equal(3, context.OfficialCommands.Count, "应始终分别报告三个官方命令能力");
        TestAssert.True(context.OfficialCommands.All(command => !command.IsAvailable), "无 BAT 夹具不应启用官方命令");
        return Task.CompletedTask;
    }

    private static Task DetectorDegradesMissingModules()
    {
        var context = new ModProjectDetector().Detect(Fixture("missing-modules"));
        TestAssert.True(context.IsRecognized, "只含 GameData 的夹具仍应识别");
        TestAssert.True(context.Modules.Single(module => module.Key == "units").CanScan, "单位模块应可用");
        TestAssert.Equal(
            ModuleAvailability.Unavailable,
            context.Modules.Single(module => module.Key == "weapons").Availability,
            "武器模块应单独不可用");
        TestAssert.Equal(
            ModuleAvailability.Unavailable,
            context.Modules.Single(module => module.Key == "ammo").Availability,
            "弹药模块应单独不可用");
        TestAssert.Equal(
            ModuleAvailability.Unavailable,
            context.Modules.Single(module => module.Key == "divisions").Availability,
            "战术师模块应单独不可用");
        return Task.CompletedTask;
    }

    private static Task DetectorFindsCustomLocalisationName()
    {
        var root = Fixture("custom-layout-name");
        var context = new ModProjectDetector().Detect(root);
        var dictionary = TestAssert.Single(context.LocalisationDictionaries, "应动态发现本地化目录");
        TestAssert.True(
            dictionary.Contains("UnexpectedDictionary", StringComparison.Ordinal),
            "本地化目录不得由 Mod 根目录名拼接");
        return Task.CompletedTask;
    }

    private static Task FieldMetadataProvidesMajorAndMinorGroups()
    {
        TestAssert.True(UnitFieldDefinitions.All.All(field => !string.IsNullOrWhiteSpace(field.Section) && !string.IsNullOrWhiteSpace(field.Group)), "Unit 字段必须同时有大类和小类");
        TestAssert.True(UnitFieldDefinitions.All.Select(field => field.Section).Distinct().Count() >= 5, "Unit 字段应拆成多个大类");
        TestAssert.Equal("将军模式价格", UnitFieldDefinitions.All.Single(field => field.Key == "economy.tickets").Label, "Resource_Tickets 应使用明确的将军模式名称");
        TestAssert.True(WeaponFieldDefinitions.Ammo.All(field => !string.IsNullOrWhiteSpace(field.Section) && !string.IsNullOrWhiteSpace(field.Group)), "Ammo 字段必须同时有大类和小类");
        return Task.CompletedTask;
    }

    private static Task DetectorProbesOfficialCommandsIndependently()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "warno-editor-tests", Guid.NewGuid().ToString("N"));
        var gameRoot = Path.Combine(sandbox, "WARNO");
        var modsRoot = Path.Combine(gameRoot, "Mods");
        var modRoot = Path.Combine(modsRoot, "ProbeMod");
        try
        {
            CopyDirectory(Fixture("minimal-complete"), modRoot);
            Directory.CreateDirectory(Path.Combine(modsRoot, "Utils", "Python"));
            Directory.CreateDirectory(Path.Combine(modsRoot, "Utils", "Scripts"));
            File.WriteAllText(Path.Combine(modsRoot, "Utils", "Python", "python.exe"), string.Empty);
            File.WriteAllText(Path.Combine(modsRoot, "Utils", "Scripts", "GenerateMod.py"), string.Empty);
            File.WriteAllText(Path.Combine(gameRoot, "WARNO.exe"), string.Empty);
            File.WriteAllText(Path.Combine(modRoot, "GenerateMod.bat"), "@exit /b 0");
            File.WriteAllText(Path.Combine(modRoot, "LaunchGameDevMode.bat"), "@exit /b 0");
            File.WriteAllText(Path.Combine(modRoot, "UploadMod.bat"), "@exit /b 0");

            var withoutUploadBackup = new ModProjectDetector().Detect(modRoot);
            TestAssert.True(withoutUploadBackup.OfficialCommand("generate")?.IsAvailable == true, "生成入口依赖完整时应可用");
            TestAssert.True(withoutUploadBackup.OfficialCommand("dev")?.IsAvailable == true, "开发模式不应被上传依赖拖累");
            TestAssert.False(withoutUploadBackup.OfficialCommand("upload")?.IsAvailable == true, "缺 CreateModBackup.py 时只应禁用上传");

            File.WriteAllText(Path.Combine(modsRoot, "Utils", "Scripts", "CreateModBackup.py"), string.Empty);
            var complete = new ModProjectDetector().Detect(modRoot);
            TestAssert.True(complete.OfficialCommands.All(command => command.IsAvailable), "依赖齐全时三个官方命令都应可用");
        }
        finally
        {
            DeleteTemporaryFixture(sandbox);
        }

        return Task.CompletedTask;
    }

    private static async Task CreateModValidatesNameAndRunsFakeOfficialScript()
    {
        var modsRoot = Path.Combine(Path.GetTempPath(), "warno-editor-tests", Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.Combine(modsRoot, "Utils", "Python"));
            Directory.CreateDirectory(Path.Combine(modsRoot, "Utils", "Scripts"));
            Directory.CreateDirectory(Path.Combine(modsRoot, "ModData"));
            File.WriteAllText(Path.Combine(modsRoot, "Utils", "Python", "python.exe"), string.Empty);
            File.WriteAllText(Path.Combine(modsRoot, "Utils", "Scripts", "CreateNewMod.py"), string.Empty);
            File.WriteAllText(Path.Combine(modsRoot, "ModData", "base.zip"), string.Empty);
            File.WriteAllText(
                Path.Combine(modsRoot, "CreateNewMod.bat"),
                "@echo off\r\nmkdir \"%~1\\GameData\"\r\necho created %~1\r\nexit /b 0\r\n",
                Encoding.ASCII);

            var service = new ModCreationService();
            TestAssert.True(service.Probe(modsRoot).IsAvailable, "新建 Mod 的四项官方依赖齐全时应可用");
            TestAssert.True(service.ValidateName(modsRoot, "Bad-Name") is not null, "名称不得包含连字符");
            TestAssert.True(service.ValidateName(modsRoot, "CON") is not null, "应拒绝 Windows 保留名");
            TestAssert.True(service.ValidateName(modsRoot, "NewMod42") is null, "纯英数名称应可用");

            var result = await service.CreateAsync(modsRoot, "NewMod42");
            TestAssert.True(result.CommandResult.Succeeded, "临时假 BAT 应正常返回");
            TestAssert.True(Directory.Exists(Path.Combine(result.ModRoot, "GameData")), "创建后必须复核 GameData 结构");
            TestAssert.True(service.ValidateName(modsRoot, "newmod42") is not null, "已有名称应按不区分大小写防重名");
        }
        finally
        {
            DeleteTemporaryFixture(modsRoot);
        }
    }

    private static Task ProblemLogSeparatesSourcesAndRecoversFatal()
    {
        var sandbox = Path.Combine(Path.GetTempPath(), "warno-editor-tests", Guid.NewGuid().ToString("N"));
        var logRoot = Path.Combine(sandbox, "logs");
        try
        {
            var log = new ApplicationProblemLog(logRoot);
            var mod = log.Record(ProblemSource.Mod, ProblemSeverity.Warning, "Mod 问题", "夹具诊断", location: "fixture.ndf:3");
            var tool = log.Record(ProblemSource.Tool, ProblemSeverity.Fatal, "工具闪退", "测试异常", new InvalidOperationException("boom"));
            TestAssert.Equal(2, log.Reports.Count, "同一问题日志应保留两类来源");
            TestAssert.Equal(ProblemSource.Mod, mod.Source, "Mod 诊断不得归到工具问题");
            TestAssert.Equal(ProblemSource.Tool, tool.Source, "未处理异常应归到工具问题");
            var recovered = log.ConsumeLastFatal();
            TestAssert.Equal(tool.Id, recovered?.Id, "下次启动应可恢复上次闪退编号");
            TestAssert.True(log.ConsumeLastFatal() is null, "闪退标记消费后不应重复提示");
            TestAssert.True(Directory.EnumerateFiles(logRoot, "problems-*.jsonl").Any(path => File.ReadAllText(path).Contains(mod.Id, StringComparison.Ordinal)), "日志文件应包含可复制的错误编号");
        }
        finally
        {
            DeleteTemporaryFixture(sandbox);
        }

        return Task.CompletedTask;
    }

    private static async Task IndexerIsReadOnly()
    {
        var root = Fixture("minimal-complete");
        var before = SnapshotFiles(root);
        var context = new ModProjectDetector().Detect(root);
        var result = await new ProjectIndexer().IndexAsync(context);
        var after = SnapshotFiles(root);

        TestAssert.Equal(6, result.Objects.Count, "完整夹具应索引六个对象");
        TestAssert.Equal(before.Count, after.Count, "索引前后文件数量必须相同");
        foreach (var pair in before)
        {
            TestAssert.True(after.TryGetValue(pair.Key, out var current), $"文件不得消失：{pair.Key}");
            TestAssert.Equal(pair.Value, current, $"文件不得被索引器修改：{pair.Key}");
        }

        TestAssert.False(Directory.Exists(Path.Combine(root, ".warno-editor")), "P1 不得创建草稿目录");
    }

    private static async Task IndexerHandlesSyntaxFixture()
    {
        var context = new ModProjectDetector().Detect(Fixture("syntax-edge-cases"));
        var result = await new ProjectIndexer().IndexAsync(context);
        TestAssert.Equal(1, result.Objects.Count, "语法边界夹具应识别一个真实对象");
        TestAssert.Equal(0, result.Diagnostics.Count, "引号、注释和嵌套分隔符不应产生误报");
    }

    private static async Task IndexerContainsMalformedFile()
    {
        var context = new ModProjectDetector().Detect(Fixture("malformed-object"));
        var result = await new ProjectIndexer().IndexAsync(context);
        TestAssert.True(result.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error), "异常文件应产生错误诊断");
        TestAssert.Equal(
            ModuleAvailability.ParseError,
            result.Modules.Single(module => module.Key == "units").Availability,
            "异常只应标记对应模块");
        TestAssert.Equal(
            ModuleAvailability.Unavailable,
            result.Modules.Single(module => module.Key == "weapons").Availability,
            "缺失武器文件的状态应保持独立");
    }

    private static async Task RecentProjectsRoundTrip()
    {
        var temporaryRoot = Path.Combine(Path.GetTempPath(), "warno-editor-tests", Guid.NewGuid().ToString("N"));
        var settingsFile = Path.Combine(temporaryRoot, "recent.json");
        try
        {
            var store = new RecentProjectStore(settingsFile);
            var first = Fixture("minimal-complete");
            var second = Fixture("missing-modules");
            await store.AddAsync(first);
            await store.AddAsync(second);
            var loaded = await store.LoadAsync();
            TestAssert.Equal(2, loaded.Count, "应保存两个最近项目");
            TestAssert.Equal(Path.GetFullPath(second), loaded[0].Path, "最新项目应排在首位");

            await store.RemoveAsync(second);
            loaded = await store.LoadAsync();
            TestAssert.Equal(1, loaded.Count, "应移除选中的最近项目");
            TestAssert.Equal(Path.GetFullPath(first), loaded[0].Path, "未移除项目应保留");
        }
        finally
        {
            var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "warno-editor-tests"));
            var resolved = Path.GetFullPath(temporaryRoot);
            if (resolved.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                Directory.Exists(resolved))
            {
                Directory.Delete(resolved, true);
            }
        }
    }

    private static Task P2FieldReaderLocatesMapAndNestedArgument()
    {
        const string source = """
            export Test is TEntityDescriptor
            (
                ModulesDescriptors = [
                    TDamageModuleDescriptor
                    (
                        BlindageProperties = TBlindageProperties
                        (
                            ResistanceFront = TResistanceTypeRTTI(Family=ResistanceFamily_blindage Index=18)
                        )
                    ),
                    TProductionModuleDescriptor
                    (
                        ProductionRessourcesNeeded = MAP [
                            ($/GFX/Resources/Resource_CommandPoints, 275),
                        ]
                    ),
                ]
            )
            """;
        var document = new NdfSyntaxDocument(source);
        var locator = new NdfFieldLocator();
        var armor = locator.Locate(document, new NdfFieldSelector(
            "TDamageModuleDescriptor",
            "BlindageProperties",
            NestedType: "TBlindageProperties",
            NestedField: "ResistanceFront",
            ArgumentName: "Index"));
        var price = locator.Locate(document, new NdfFieldSelector(
            "TProductionModuleDescriptor",
            "ProductionRessourcesNeeded",
            "Resource_CommandPoints"));
        TestAssert.Equal(1, armor.Values.Count, "应唯一定位正面装甲索引");
        TestAssert.Equal("18", document.Raw(armor.Values[0]), "装甲索引读取错误");
        TestAssert.Equal(1, price.Values.Count, "应唯一定位价格 MAP 项");
        TestAssert.Equal("275", document.Raw(price.Values[0]), "价格读取错误");
        return Task.CompletedTask;
    }

    private static async Task P2WorkspaceReadsFieldsNamesAndReferences()
    {
        var context = new ModProjectDetector().Detect(Fixture("p2-unit-complete"));
        var index = await new ProjectIndexer().IndexAsync(context);
        var workspace = await new UnitProjectLoader().LoadAsync(context, index);
        TestAssert.Equal(2, workspace.Units.Count, "P2 夹具应读取两个 Unit");

        var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
        TestAssert.Equal("测试主战坦克", tank.DisplayName, "UNITS.csv 名称优先级错误");
        TestAssert.Equal("275", tank.Field("economy.commandPoints")?.DisplayValue, "指挥点读取错误");
        TestAssert.Equal("50", tank.Field("armor.ecm")?.DisplayValue, "ECM 换算错误");
        TestAssert.True(
            UnitValueConverter.TryFormatTarget(tank.Field("armor.ecm")!, "40%", out var ecmDisplay, out var ecmRaw, out _),
            "ECM 显示值应能转换回 NDF 原值");
        TestAssert.Equal("40", ecmDisplay, "ECM 显示值规范化错误");
        TestAssert.Equal("-0.4", ecmRaw, "ECM 反向换算错误");
        var zeroEcm = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Recon_SOV").Field("armor.ecm")!;
        TestAssert.Equal("0", zeroEcm.DisplayValue, "零 ECM 不应显示成负零");
        TestAssert.True(UnitValueConverter.TryFormatTarget(zeroEcm, "0", out _, out var zeroEcmRaw, out _), "零 ECM 应可安全写回");
        TestAssert.Equal("0.0", zeroEcmRaw, "零 ECM 的 NDF 原值不得写成 -0");
        TestAssert.Equal("18", tank.Field("armor.front")?.DisplayValue, "装甲索引读取错误");
        TestAssert.Equal("ResistanceFamily_blindage", tank.Field("armor.front.family")?.DisplayValue, "正面护甲族读取错误");
        TestAssert.Equal(1, workspace.DamageResistance.ResistanceFamilies.Single(item => item.Name == "ResistanceFamily_infanterie").MinimumIndex, "族索引应从 1 开始");
        TestAssert.Equal(40, workspace.DamageResistance.DamageFamilies.Single(item => item.Name == "DamageFamily_ap").MaximumIndex, "DamageFamilyCounts 应作为最大索引而不是减一");
        var categoryChoices = tank.Field("structure.category")!.Choices.Select(item => item.Display).ToArray();
        TestAssert.True(categoryChoices.Contains("TAcknowUnitType_Tank", StringComparer.Ordinal), "多类别字段应拆出 Tank 原子选项");
        TestAssert.True(categoryChoices.Contains("TAcknowUnitType_Transport", StringComparer.Ordinal), "多类别字段应拆出 Transport 原子选项");
        TestAssert.True(categoryChoices.All(item => !item.Contains(',', StringComparison.Ordinal)), "类别选项不得保留复合重复项");
        TestAssert.True(tank.Weapons.Contains("WeaponDescriptor_Test_Tank_US"), "Unit→Weapon 引用缺失");
        TestAssert.True(tank.Ammunition.Contains("Ammo_Test_AP"), "Unit→Ammo 引用缺失");
        TestAssert.True(tank.Divisions.Any(item => item.Contains("Test US", StringComparison.Ordinal)), "DivisionRule→Unit 引用缺失");
        TestAssert.True(tank.CanEditName, "唯一 UNITS.csv 应允许名称草稿");

        var recon = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Recon_SOV");
        TestAssert.Equal("侦察; \"尖兵\"", recon.DisplayName, "带分号和引号的名称解析错误");
    }

    private static async Task P2WorkspaceReportsUnavailableFields()
    {
        var context = new ModProjectDetector().Detect(Fixture("p2-unit-complete"));
        var index = await new ProjectIndexer().IndexAsync(context);
        var workspace = await new UnitProjectLoader().LoadAsync(context, index);
        var recon = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Recon_SOV");
        TestAssert.Equal(
            UnitFieldAvailability.UnsupportedValue,
            recon.Field("survival.suppression")?.Availability,
            "常量引用不能伪装成可编辑整数");
        TestAssert.Equal(
            UnitFieldAvailability.Missing,
            recon.Field("movement.acceleration")?.Availability,
            "缺失陆地运动字段应单项只读");
        TestAssert.Equal(
            UnitFieldAvailability.Missing,
            recon.Field("deployment.shift")?.Availability,
            "缺失前置部署模块应单项只读");
    }

    private static Task P2CsvReaderHandlesQuotedValues()
    {
        const string csv = "\"TOKEN\";\"REFTEXT\"\r\n\"ABCDEF1234\";\"侦察; \"\"尖兵\"\"\"\r\n";
        var rows = SemicolonCsvReader.Read(csv);
        TestAssert.Equal(2, rows.Count, "CSV 应读取表头和数据行");
        TestAssert.Equal("侦察; \"尖兵\"", rows[1][1], "CSV 引号或分号解析错误");

        var catalog = new UnitLocalisationCatalog(
            "C:\\Fixture",
            [],
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal),
            []);
        var token = catalog.GenerateToken();
        TestAssert.Equal(10, token.Length, "自动 token 必须恰好 10 个字符");
        TestAssert.True(token.StartsWith("WL", StringComparison.Ordinal), "自动 token 应使用工具前缀");
        return Task.CompletedTask;
    }

    private static async Task P2DraftRoundTripPreservesFormalFiles()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var before = SnapshotFormalFiles(root);
            var context = new ModProjectDetector().Detect(root);
            var index = await new ProjectIndexer().IndexAsync(context);
            var workspace = await new UnitProjectLoader().LoadAsync(context, index);
            var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
            var health = tank.Field("survival.health")!;
            using (var store = new DraftStore(root))
            {
                var initial = await store.LoadAsync();
                TestAssert.Equal(0, initial.Document.Operations.Count, "首次打开不应有草稿");
                TestAssert.False(Directory.Exists(store.EditorDirectory), "纯浏览不应创建 .warno-editor");
                await store.UpsertAsync(CreateFieldDraft(tank, health, "12", "12"));
                TestAssert.True(File.Exists(store.DraftPath), "有效编辑应写入草稿文件");
                var bytes = File.ReadAllBytes(store.DraftPath);
                TestAssert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "草稿 JSON 不应带 UTF-8 BOM");
            }

            using (var reopened = new DraftStore(root))
            {
                var loaded = await reopened.LoadAsync();
                TestAssert.Equal(1, loaded.Document.Operations.Count, "重启后应恢复一条草稿");
                var resolved = DraftResolver.Resolve(workspace, loaded.Document.Operations);
                TestAssert.Equal(DraftResolutionStatus.Active, resolved[0].Status, "基线未变时草稿应可恢复");
            }

            var after = SnapshotFormalFiles(root);
            TestAssert.Equal(before.Count, after.Count, "P2 不应增加正式文件");
            foreach (var pair in before)
            {
                TestAssert.True(after.TryGetValue(pair.Key, out var current), $"正式文件不得消失：{pair.Key}");
                TestAssert.Equal(pair.Value, current, $"P2 草稿不得修改正式文件：{pair.Key}");
            }
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P2DraftResolverDetectsConflict()
    {
        var context = new ModProjectDetector().Detect(Fixture("p2-unit-complete"));
        var index = await new ProjectIndexer().IndexAsync(context);
        var workspace = await new UnitProjectLoader().LoadAsync(context, index);
        var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
        var health = tank.Field("survival.health")!;
        var draft = CreateFieldDraft(tank, health, "12", "12") with { BaselineRaw = "999" };
        var resolved = DraftResolver.Resolve(workspace, [draft]);
        TestAssert.Equal(DraftResolutionStatus.Conflict, resolved[0].Status, "字段基线变化必须标记冲突");
    }

    private static async Task P2DraftClearPreservesUnknownFiles()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            using var store = new DraftStore(root);
            await store.LoadAsync();
            Directory.CreateDirectory(store.EditorDirectory);
            var unknown = Path.Combine(store.EditorDirectory, "keep.txt");
            await File.WriteAllTextAsync(unknown, "保留");

            var context = new ModProjectDetector().Detect(root);
            var index = await new ProjectIndexer().IndexAsync(context);
            var workspace = await new UnitProjectLoader().LoadAsync(context, index);
            var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
            var health = tank.Field("survival.health")!;
            await store.UpsertAsync(CreateFieldDraft(tank, health, "12", "12"));
            await store.ClearAsync();
            TestAssert.False(File.Exists(store.DraftPath), "清空应删除本工具草稿");
            TestAssert.True(File.Exists(unknown), "清空不得删除未知文件");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P2DuplicateNameTokenDisablesEditing()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var csv = Path.Combine(root, "GameData", "Localisation", "UnexpectedP2Dictionary", "UNITS.csv");
            await File.AppendAllTextAsync(csv, "\"TESTUNIT01\";\"重复名称\"\r\n");
            var context = new ModProjectDetector().Detect(root);
            var index = await new ProjectIndexer().IndexAsync(context);
            var workspace = await new UnitProjectLoader().LoadAsync(context, index);
            var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
            TestAssert.False(tank.CanEditName, "重复 CSV token 不得开放名称编辑");
            TestAssert.True(workspace.Diagnostics.Any(item => item.Contains("token 重复", StringComparison.Ordinal)), "重复 token 应产生诊断");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P2MultipleUnitsCsvTargetsDisableEditing()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var second = Path.Combine(root, "GameData", "Localisation", "SecondDictionary");
            Directory.CreateDirectory(second);
            await File.WriteAllTextAsync(
                Path.Combine(second, "LocalisationDicos.ndf"),
                "unnamed TLocalisationDicoResource(FileName = 'GameData:/Localisation/SecondDictionary/UNITS.csv')");
            await File.WriteAllTextAsync(Path.Combine(second, "UNITS.csv"), "\"TOKEN\";\"REFTEXT\"\r\n");
            var context = new ModProjectDetector().Detect(root);
            var index = await new ProjectIndexer().IndexAsync(context);
            var workspace = await new UnitProjectLoader().LoadAsync(context, index);
            TestAssert.True(workspace.Units.All(unit => !unit.CanEditName), "多个 UNITS.csv 目标时只应降级名称编辑");
            TestAssert.True(workspace.Units.All(unit => unit.Fields.Any(field => field.CanEdit)), "名称降级不应阻断单位字段");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P2UnknownDraftVersionIsPreserved()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            using var store = new DraftStore(root);
            Directory.CreateDirectory(store.EditorDirectory);
            const string original = "{\"schemaVersion\":99,\"updatedUtc\":\"2026-09-05T00:00:00Z\",\"operations\":[]}";
            await File.WriteAllTextAsync(store.DraftPath, original);
            var loaded = await store.LoadAsync();
            TestAssert.True(loaded.IsBlocked, "未知草稿版本必须阻止覆盖");
            TestAssert.Equal(original, await File.ReadAllTextAsync(store.DraftPath), "加载未知版本不得改写原草稿");
            await store.ClearAsync();
            TestAssert.False(File.Exists(store.DraftPath), "用户清空后应删除未知版本草稿");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3AppliesUnitAndNameWithBackupAndLog()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (workspace, tank) = await LoadTankAsync(root);
            var ndfPath = tank.Source.SourceFile;
            var csvPath = Path.Combine(root, "GameData", "Localisation", "UnexpectedP2Dictionary", "UNITS.csv");
            var originalNdf = await File.ReadAllTextAsync(ndfPath);
            var originalNdfBytes = await File.ReadAllBytesAsync(ndfPath);
            var originalCsvBytes = await File.ReadAllBytesAsync(csvPath);
            var health = tank.Field("survival.health")!;
            var healthDraft = CreateFieldDraft(tank, health, "12", "12");
            var nameDraft = CreateNameDraft(tank, "新名称; \"可靠\"", tank.NameToken!);

            using var store = new DraftStore(root);
            await store.LoadAsync();
            await store.UpsertAsync(healthDraft);
            await store.UpsertAsync(nameDraft);
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            TestAssert.Equal(2, preview.FormalFileCount, "应用应修改一个 NDF 和一个 CSV");
            var result = await service.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded, "P3 应用应成功");
            TestAssert.False(File.Exists(store.DraftPath), "应用成功后应清理草稿");

            var expectedNdf = originalNdf.Remove(health.Location!.CharacterOffset, health.Location.CharacterLength)
                .Insert(health.Location.CharacterOffset, "12");
            var actualNdf = await File.ReadAllTextAsync(ndfPath);
            TestAssert.Equal(expectedNdf, actualNdf, "NDF 应只替换目标字段 span");
            var ndfBytes = await File.ReadAllBytesAsync(ndfPath);
            TestAssert.False(ndfBytes.Length >= 3 && ndfBytes[0] == 0xEF && ndfBytes[1] == 0xBB && ndfBytes[2] == 0xBF, "NDF 写回不得带 BOM");

            var fresh = await LoadWorkspaceAsync(root);
            var freshTank = fresh.Units.Single(unit => unit.Name == tank.Name);
            TestAssert.Equal("12", freshTank.Field("survival.health")?.RawValue, "应用后的生命值错误");
            TestAssert.Equal("新名称; \"可靠\"", freshTank.DisplayName, "应用后的本地化名称错误");

            var backups = new TransactionBackupStore(root);
            var manifest = backups.Load(result.BackupId);
            TestAssert.Equal("Completed", manifest.State, "应用备份状态错误");
            var ndfManifest = manifest.Files.Single(item => item.RelativePath.EndsWith("UniteDescriptor.ndf", StringComparison.Ordinal));
            var csvManifest = manifest.Files.Single(item => item.RelativePath.EndsWith("UNITS.csv", StringComparison.Ordinal));
            TestAssert.True(backups.ReadOriginal(result.BackupId, ndfManifest).SequenceEqual(originalNdfBytes), "备份 NDF 原件不一致");
            TestAssert.True(backups.ReadOriginal(result.BackupId, csvManifest).SequenceEqual(originalCsvBytes), "备份 CSV 原件不一致");
            TestAssert.True(File.Exists(Path.Combine(root, result.LogRelativePath!.Replace('/', Path.DirectorySeparatorChar))), "应用日志应随事务创建");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3AppliesGeneratedNameTokenAtomically()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var ndfPath = Path.Combine(root, "GameData", "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf");
            var csvPath = Path.Combine(root, "GameData", "Localisation", "UnexpectedP2Dictionary", "UNITS.csv");
            var ndf = (await File.ReadAllTextAsync(ndfPath)).Replace("NameToken = 'TESTUNIT02'", "NameToken = 'BAD'", StringComparison.Ordinal);
            await File.WriteAllTextAsync(ndfPath, ndf, new UTF8Encoding(false));
            var csv = string.Join("\r\n", (await File.ReadAllLinesAsync(csvPath)).Where(line => !line.Contains("TESTUNIT02", StringComparison.Ordinal))) + "\r\n";
            await File.WriteAllTextAsync(csvPath, csv, new UTF8Encoding(false));

            var workspace = await LoadWorkspaceAsync(root);
            var recon = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Recon_SOV");
            TestAssert.True(recon.NameTokenRequiresReplacement, "无效 NameToken 应要求替换");
            const string targetToken = "WL12345678";
            using var store = new DraftStore(root);
            await store.LoadAsync();
            await store.UpsertAsync(CreateNameDraft(recon, "新侦察名称", targetToken));
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            await service.CommitApplyAsync(preview, store);

            var updatedNdf = await File.ReadAllTextAsync(ndfPath);
            TestAssert.True(updatedNdf.Contains("NameToken = 'WL12345678'", StringComparison.Ordinal), "NDF NameToken 未更新");
            var updatedRows = SemicolonCsvReader.Read(await File.ReadAllTextAsync(csvPath));
            var row = updatedRows.Single(item => item.Count >= 2 && item[0] == targetToken);
            TestAssert.Equal("新侦察名称", row[1], "新 token 的 CSV 名称错误");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3RejectsExternalChangeAfterPreview()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_, tank) = await LoadTankAsync(root);
            using var store = new DraftStore(root);
            await store.LoadAsync();
            await store.UpsertAsync(CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "12"));
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            var external = (await File.ReadAllTextAsync(tank.Source.SourceFile)).Replace("MaxPhysicalDamages = 10", "MaxPhysicalDamages = 11", StringComparison.Ordinal);
            await File.WriteAllTextAsync(tank.Source.SourceFile, external, new UTF8Encoding(false));

            await TestAssert.ThrowsAsync<IOException>(
                () => service.CommitApplyAsync(preview, store),
                "预览后的外部修改必须阻止提交");
            TestAssert.Equal(external, await File.ReadAllTextAsync(tank.Source.SourceFile), "外部修改不应被旧预览覆盖");
            TestAssert.True(File.Exists(store.DraftPath), "冲突失败后草稿必须保留");
            TestAssert.False(File.Exists(Path.Combine(root, preview.LogRelativePath.Replace('/', Path.DirectorySeparatorChar))), "冲突失败不应写正式日志");
            TestAssert.Equal("RolledBack", new TransactionBackupStore(root).Load(preview.BackupId).State, "无提交冲突应记录为已回滚");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3RollsBackCommittedFilesOnFailure()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var csvPath = Path.Combine(root, "GameData", "Localisation", "UnexpectedP2Dictionary", "UNITS.csv");
        try
        {
            var (_, tank) = await LoadTankAsync(root);
            var originalNdf = await File.ReadAllBytesAsync(tank.Source.SourceFile);
            var originalCsv = await File.ReadAllBytesAsync(csvPath);
            using var store = new DraftStore(root);
            await store.LoadAsync();
            await store.UpsertAsync(CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "12"));
            await store.UpsertAsync(CreateNameDraft(tank, "回滚测试名称", tank.NameToken!));
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            File.SetAttributes(csvPath, File.GetAttributes(csvPath) | FileAttributes.ReadOnly);

            await TestAssert.ThrowsAsync<IOException>(
                () => service.CommitApplyAsync(preview, store),
                "第二个文件提交失败时事务应失败");
            TestAssert.True((await File.ReadAllBytesAsync(tank.Source.SourceFile)).SequenceEqual(originalNdf), "已提交 NDF 应回滚到原件");
            TestAssert.True((await File.ReadAllBytesAsync(csvPath)).SequenceEqual(originalCsv), "未提交 CSV 应保持原件");
            TestAssert.True(File.Exists(store.DraftPath), "回滚后草稿应保留");
            TestAssert.Equal("RolledBack", new TransactionBackupStore(root).Load(preview.BackupId).State, "失败事务应记录回滚状态");
        }
        finally
        {
            if (File.Exists(csvPath))
            {
                File.SetAttributes(csvPath, FileAttributes.Normal);
            }

            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3RestoresApplicationBackup()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_, tank) = await LoadTankAsync(root);
            var originalNdf = await File.ReadAllBytesAsync(tank.Source.SourceFile);
            using var store = new DraftStore(root);
            await store.LoadAsync();
            await store.UpsertAsync(CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "12"));
            var service = new UnitTransactionService();
            var applyPreview = await service.PrepareApplyAsync(root, store.Operations);
            var applied = await service.CommitApplyAsync(applyPreview, store);
            var applyLog = Path.Combine(root, applied.LogRelativePath!.Replace('/', Path.DirectorySeparatorChar));

            var restorePreview = service.PrepareRestore(root, applied.BackupId);
            var restored = await service.CommitRestoreAsync(restorePreview);
            TestAssert.True(restored.Succeeded, "恢复事务应成功");
            TestAssert.True((await File.ReadAllBytesAsync(tank.Source.SourceFile)).SequenceEqual(originalNdf), "恢复后 NDF 应与应用前逐字节一致");
            TestAssert.True(File.Exists(applyLog), "恢复不应删除既有应用日志");
            TestAssert.True(File.Exists(Path.Combine(root, restored.LogRelativePath!.Replace('/', Path.DirectorySeparatorChar))), "恢复应产生独立日志");
            var restoreManifest = new TransactionBackupStore(root).Load(restored.BackupId);
            TestAssert.Equal("Restore", restoreManifest.Kind, "恢复动作应建立 Restore 备份");
            TestAssert.Equal("Completed", restoreManifest.State, "恢复备份状态错误");
            TestAssert.Equal(applied.BackupId, restoreManifest.SourceBackupId, "恢复来源备份编号缺失");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3PreservesCsvEncodingAndEscaping()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var csvPath = Path.Combine(root, "GameData", "Localisation", "UnexpectedP2Dictionary", "UNITS.csv");
            var original = await File.ReadAllBytesAsync(csvPath);
            await File.WriteAllBytesAsync(csvPath, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original).ToArray());
            var (_, tank) = await LoadTankAsync(root);
            using var store = new DraftStore(root);
            await store.LoadAsync();
            const string target = "名称; \"双引号\"";
            await store.UpsertAsync(CreateNameDraft(tank, target, tank.NameToken!));
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations);
            await service.CommitApplyAsync(preview, store);

            var bytes = await File.ReadAllBytesAsync(csvPath);
            TestAssert.True(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "CSV 原有 UTF-8 BOM 应保留");
            var rows = SemicolonCsvReader.Read(Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
            TestAssert.Equal(target, rows.Single(row => row.Count >= 2 && row[0] == tank.NameToken)[1], "CSV 特殊字符转义错误");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3RejectsNdfWithBom()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_, tank) = await LoadTankAsync(root);
            var original = await File.ReadAllBytesAsync(tank.Source.SourceFile);
            await File.WriteAllBytesAsync(tank.Source.SourceFile, new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original).ToArray());
            var workspace = await LoadWorkspaceAsync(root);
            tank = workspace.Units.Single(unit => unit.Name == tank.Name);
            var draft = CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "12");
            await TestAssert.ThrowsAsync<InvalidDataException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [draft]),
                "P3 不应隐式删除 NDF BOM 后写入");
            TestAssert.True((await File.ReadAllBytesAsync(tank.Source.SourceFile)).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(original)), "拒绝后 NDF 应保持不变");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3RejectsTamperedDraftTarget()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (_, tank) = await LoadTankAsync(root);
            var draft = CreateFieldDraft(tank, tank.Field("survival.health")!, "12", "13");
            await TestAssert.ThrowsAsync<TransactionValidationException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [draft]),
                "草稿 targetRaw 与转换结果不符时必须拒绝");
            TestAssert.False(Directory.Exists(Path.Combine(root, ".warno-editor", "backups")), "候选校验失败不应创建备份");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3InsertsOptionalDeploymentModule()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var workspace = await LoadWorkspaceAsync(root);
            var unit = workspace.Units.Single(item => item.Name == "Descriptor_Unit_Test_Recon_SOV");
            var field = unit.Field("deployment.shift")!;
            TestAssert.Equal(UnitFieldAvailability.Missing, field.Availability, "测试 Unit 基线应没有前置部署模块");
            TestAssert.True(UnitValueConverter.TryFormatTarget(field, "100", out var display, out var raw, out var error), error);
            var operation = new DraftOperation(
                DraftOperation.CreateId(DraftTargetKind.OptionalUnitModule, unit.Source.RelativeSourceFile, unit.Name, field.Definition.Key),
                null, DraftTargetKind.OptionalUnitModule, "units", unit.Source.RelativeSourceFile, unit.Name, unit.Source.TypeName,
                field.Definition.Key, field.Definition.Selector.DisplayPath, field.Definition.ValueKind.ToString(), string.Empty, string.Empty,
                display, raw, $"{unit.DisplayName} · 补建前置部署", null, false, DateTimeOffset.UtcNow);
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [operation]);
            var candidate = Candidate(preview, "UniteDescriptor.ndf");
            var unitStart = candidate.IndexOf(unit.Name, StringComparison.Ordinal);
            var upkeep = candidate.IndexOf("TUnitUpkeepModuleDescriptor", unitStart, StringComparison.Ordinal);
            var deployment = candidate.IndexOf("TDeploymentShiftModuleDescriptor( DeploymentShiftGRU = 100.0 )", upkeep, StringComparison.Ordinal);
            var visual = candidate.IndexOf("TVisualShootPositionsModuleDescriptor", upkeep, StringComparison.Ordinal);
            TestAssert.True(upkeep >= 0 && upkeep < deployment && deployment < visual, "前置部署必须插入在两个严格锚点之间");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P3ValidatesResistanceAndDamageFamilies()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var (context, units, weapons) = await LoadP4Async(root);
            var tank = units.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
            var familyField = tank.Field("armor.front.family")!;
            var invalidArmor = CreateFieldDraft(tank, familyField, "ResistanceFamily_infanterie", "ResistanceFamily_infanterie");
            await TestAssert.ThrowsAsync<TransactionValidationException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [invalidArmor]),
                "步兵护甲族与非 1 索引组合必须被拒绝");

            var ammo = weapons.Ammo("Ammo_Test_AP")!;
            var damageIndex = ammo.Field("ammo.damage.index")!;
            var invalidDamage = CreateWeaponDraft(damageIndex, "41", DraftEditScope.AllReferences, [], null);
            await TestAssert.ThrowsAsync<TransactionValidationException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [invalidDamage]),
                "伤害索引超过当前 DamageFamilyCounts 必须被拒绝");

            TestAssert.True(context.IsRecognized, "族校验测试项目应保持可识别");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4ReadsWeaponAmmoRelationships()
    {
        var (_, _, weapons) = await LoadP4Async(Fixture("p4-shared"));
        var shared = weapons.Weapon("WeaponDescriptor_P4_Shared")!;
        TestAssert.Equal(2, shared.Mounts.Count, "共享 Weapon 应有两个 MountedWeapon");
        TestAssert.Equal(0, shared.Mounts[0].AmmoBoxIndex, "第一个挂载应映射 AmmoBox 0");
        TestAssert.Equal(0, shared.Mounts[1].AmmoBoxIndex, "同一 AmmoBox 可以对应多个挂载");
        TestAssert.Equal("4", shared.Field("weapon.salves.0")!.DisplayValue, "AmmoBox 0 的 Salves 应为 4");
        TestAssert.Equal(2, weapons.Ammo("Ammo_P4_Shared")!.ShotsPerSalvo, "每齐射射弹数应读取为 2");
        TestAssert.Equal(2, weapons.References.WeaponUnits[shared.Name].Count, "共享 Weapon 应有两个 Unit 引用者");
        TestAssert.Equal(2, weapons.References.AmmoWeapons["Ammo_P4_Shared"].Count, "共享 Ammo 应有两个 Weapon 引用者");
        TestAssert.Equal(3, weapons.References.AmmoUnits["Ammo_P4_Shared"].Count, "共享 Ammo 应影响三个 Unit");
    }

    private static async Task P4IsolatesAmmoForCurrentUnit()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            var ammo = weapons.Ammo("Ammo_P4_Shared")!;
            var draft = CreateWeaponDraft(ammo.Field("ammo.damage.physical")!, "11", DraftEditScope.CurrentUnit, ["Descriptor_Unit_P4_One"], "WeaponDescriptor_P4_Shared");
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
            var ammoText = Candidate(preview, "Ammunition.ndf");
            var weaponText = Candidate(preview, "WeaponDescriptor.ndf");
            var unitText = Candidate(preview, "UniteDescriptor.ndf");
            TestAssert.True(ammoText.Contains("Ammo_P4_Shared_WLMT_", StringComparison.Ordinal), "局部 Ammo 修改应追加 Ammo 副本");
            TestAssert.True(ammoText.Contains("PhysicalDamages = 11", StringComparison.Ordinal), "Ammo 副本应包含目标值");
            TestAssert.True(weaponText.Contains("WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "共享 Weapon 应为当前 Unit 克隆");
            TestAssert.True(unitText.Contains("Descriptor_Unit_P4_One", StringComparison.Ordinal) && unitText.Contains("$/GFX/Weapon/WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "当前 Unit 应改引 Weapon 副本");
            var secondStart = unitText.IndexOf("Descriptor_Unit_P4_Two", StringComparison.Ordinal);
            TestAssert.True(unitText[secondStart..].Contains("$/GFX/Weapon/WeaponDescriptor_P4_Shared,", StringComparison.Ordinal), "未选 Unit 应保持原 Weapon 引用");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4SelectedUnitsReuseExclusiveWeapon()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            var draft = CreateWeaponDraft(
                weapons.Ammo("Ammo_P4_Shared")!.Field("ammo.supply")!,
                "15",
                DraftEditScope.SelectedUnits,
                ["Descriptor_Unit_P4_One", "Descriptor_Unit_P4_Two"],
                "WeaponDescriptor_P4_Shared");
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
            var weaponText = Candidate(preview, "WeaponDescriptor.ndf");
            TestAssert.True(!weaponText.Contains("WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "所选 Unit 已覆盖全部 Weapon 引用者时不应克隆 Weapon");
            TestAssert.True(weaponText.Contains("Ammunition = $/GFX/Weapon/Ammo_P4_Shared_WLMT_", StringComparison.Ordinal), "原 Weapon 应改引隔离 Ammo");
            TestAssert.True(weaponText.Contains("WeaponDescriptor_P4_Other", StringComparison.Ordinal) && weaponText.Contains("Ammunition = $/GFX/Weapon/Ammo_P4_Shared\n", StringComparison.Ordinal), "其他 Weapon 应保留原 Ammo");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4EditsSharedAmmoGlobally()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            var draft = CreateWeaponDraft(weapons.Ammo("Ammo_P4_Shared")!.Field("ammo.damage.physical")!, "12", DraftEditScope.AllReferences, [], null);
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
            var ammoText = Candidate(preview, "Ammunition.ndf");
            TestAssert.True(ammoText.Contains("PhysicalDamages = 12", StringComparison.Ordinal), "全局候选应直接包含目标值");
            TestAssert.True(!ammoText.Contains("Ammo_P4_Shared_WLMT_", StringComparison.Ordinal), "全局修改不应生成 Ammo 副本");
            TestAssert.True(preview.Files.All(file => !file.RelativePath.EndsWith("WeaponDescriptor.ndf", StringComparison.OrdinalIgnoreCase)), "全局 Ammo 数值修改不应写 Weapon 文件");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4IsolatesWeaponField()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            var weapon = weapons.Weapon("WeaponDescriptor_P4_Shared")!;
            var draft = CreateWeaponDraft(weapon.Field("weapon.salves.0")!, "6", DraftEditScope.CurrentUnit, ["Descriptor_Unit_P4_One"], weapon.Name);
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
            var weaponText = Candidate(preview, "WeaponDescriptor.ndf");
            TestAssert.True(weaponText.Contains("WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "局部 Weapon 字段修改应克隆共享 Weapon");
            TestAssert.True(weaponText.Contains("Salves = [ 6, 5, ]", StringComparison.Ordinal), "Weapon 副本 Salves 应修改目标 AmmoBox");
            TestAssert.True(!weaponText.Contains("Ammo_P4_Shared_WLMT_", StringComparison.Ordinal), "仅改 Weapon 字段不应克隆 Ammo");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4ReplacesExistingAmmo()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            var weapon = weapons.Weapon("WeaponDescriptor_P4_Shared")!;
            var ammoField = weapon.Mounts[0].Fields.Single(item => item.Definition.FieldName == "Ammunition");
            var draft = CreateWeaponDraft(ammoField, "Ammo_P4_Alt", DraftEditScope.CurrentUnit, ["Descriptor_Unit_P4_One"], weapon.Name);
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [draft]);
            var weaponText = Candidate(preview, "WeaponDescriptor.ndf");
            TestAssert.True(weaponText.Contains("WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "当前 Unit 替换共享 Weapon 的 Ammo 应克隆 Weapon");
            TestAssert.True(weaponText.Contains("Ammunition = $/GFX/Weapon/Ammo_P4_Alt", StringComparison.Ordinal), "Weapon 副本应引用项目中已有 Ammo");
            TestAssert.True(preview.Files.All(file => !file.RelativePath.EndsWith("Ammunition.ndf", StringComparison.OrdinalIgnoreCase)), "只替换 Ammo 引用不应写 Ammo 文件");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4CommitsIsolationTransaction()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, _, weapons) = await LoadP4Async(root);
            using var store = new DraftStore(root);
            await store.LoadAsync();
            var draft = CreateWeaponDraft(
                weapons.Ammo("Ammo_P4_Shared")!.Field("ammo.damage.physical")!,
                "14",
                DraftEditScope.CurrentUnit,
                ["Descriptor_Unit_P4_One"],
                "WeaponDescriptor_P4_Shared");
            await store.UpsertAsync(draft);
            var transactions = new UnitTransactionService();
            var preview = await transactions.PrepareApplyAsync(root, store.Operations.ToArray());
            var result = await transactions.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded, "P4 事务应提交成功");
            TestAssert.Equal(0, store.Operations.Count, "提交成功后应清理草稿");
            TestAssert.True(transactions.ListBackups(root).Any(item => item.BackupId == result.BackupId && item.CanRestore), "应生成可恢复备份");
            var unitText = File.ReadAllText(Path.Combine(root, "GameData", "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf"));
            TestAssert.True(unitText.Contains("Descriptor_Unit_P4_One", StringComparison.Ordinal) && unitText.Contains("WeaponDescriptor_P4_Shared_WLMT_", StringComparison.Ordinal), "提交后当前 Unit 应改引隔离 Weapon");
            var log = File.ReadAllText(Path.Combine(root, result.LogRelativePath!));
            TestAssert.True(log.Contains("自动克隆最小 Ammo→Weapon→Unit 链", StringComparison.Ordinal), "目标日志应记录隔离方式");
            foreach (var relative in preview.Files.Where(item => item.Kind == FormalTextFileKind.Ndf).Select(item => item.RelativePath))
            {
                var bytes = File.ReadAllBytes(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
                TestAssert.True(bytes.Length < 3 || bytes[0] != 0xEF || bytes[1] != 0xBB || bytes[2] != 0xBF, $"NDF 不得带 BOM：{relative}");
            }
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P4ReplacesExistingWeaponReferences()
    {
        var root = CreateTemporaryFixtureCopy("p4-shared");
        try
        {
            var (_, units, _) = await LoadP4Async(root);
            var targets = units.Units.Where(item => item.Weapons.Contains("WeaponDescriptor_P4_Shared", StringComparer.Ordinal)).ToArray();
            var names = targets.Select(item => item.Name).ToArray();
            var operations = targets.Select(unit => CreateUnitWeaponDraft(unit, "WeaponDescriptor_P4_Shared", "WeaponDescriptor_P4_Other", DraftEditScope.AllReferences, names)).ToArray();
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, operations);
            var unitText = Candidate(preview, "UniteDescriptor.ndf");
            TestAssert.Equal(0, unitText.Split("$/GFX/Weapon/WeaponDescriptor_P4_Shared", StringSplitOptions.None).Length - 1, "全部引用替换后不应保留源 Weapon 引用");
            TestAssert.Equal(3, unitText.Split("$/GFX/Weapon/WeaponDescriptor_P4_Other", StringSplitOptions.None).Length - 1, "三个 Unit 最终都应引用目标 Weapon");
            TestAssert.True(preview.Files.All(item => !item.RelativePath.EndsWith("WeaponDescriptor.ndf", StringComparison.OrdinalIgnoreCase)), "替换已有 Weapon 引用不应写 Weapon 文件");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5ReadsDivisionWorkspace()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, units, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            TestAssert.True(divisions.HasCompleteFileSet, "P5 夹具应具有完整五文件集");
            TestAssert.True(division.CanEdit, division.EditReason);
            TestAssert.Equal(2, division.Baseline.UnitRules.Count, "应读取两条 UnitRule");
            TestAssert.Equal(1, division.Baseline.DefaultDeck.Count, "应读取一个默认 Pack 引用");
            TestAssert.Equal(3, division.Baseline.CostCurves.Count, "应读取三条费用曲线");
            TestAssert.Equal(1, DivisionStateValidator.CalculateActivationPoints(divisions, division.Baseline), "基线默认 Deck 激活点应为 1");
            TestAssert.Equal("Descriptor_Unit_P5_Transport", division.Baseline.UnitRules[1].AvailableTransports.Single(), "运输引用应解析为 Unit 名称");
            TestAssert.True(units.Units.Single(item => item.Name == "Descriptor_Unit_P5_Transport").HasUniqueTransporterModule, "唯一运输模块应被识别为运输资格");
            TestAssert.False(units.Units.Single(item => item.Name == "Descriptor_Unit_P5_Tank").HasUniqueTransporterModule, "角色名称不能替代运输模块结构判断");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5PlansCompleteDivisionEdit()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, _, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var target = BuildP5Target(division);
            var operation = CreateDivisionDraft(division, target);
            var preview = await new UnitTransactionService().PrepareApplyAsync(root, [operation]);
            TestAssert.True(Candidate(preview, "Divisions.ndf").Contains("MaxActivationPoints = 12", StringComparison.Ordinal), "师激活点上限应进入候选");
            TestAssert.True(Candidate(preview, "DivisionRules.ndf").Contains("NumberOfUnitInPack = 5", StringComparison.Ordinal), "UnitRule 单卡数量应进入候选");
            TestAssert.True(Candidate(preview, "DivisionCostMatrix.ndf").Contains("Factory/Tanks, [1, 3]", StringComparison.Ordinal), "费用曲线应进入候选");
            TestAssert.True(preview.Files.All(item => !item.RelativePath.EndsWith("DeckPacks.ndf", StringComparison.OrdinalIgnoreCase) && !item.RelativePath.EndsWith("Decks.ndf", StringComparison.OrdinalIgnoreCase)), "默认卡组移除后不应生成 Deck/DeckPack 写入");
            TestAssert.True(preview.ValidationMessages.Any(item => item.Contains("UnitRule 唯一", StringComparison.Ordinal)), "预览应报告 P5 唯一性与容量校验");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5RejectsLegacyDefaultDeckEdit()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, units, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var legacy = division.Baseline with
            {
                DefaultDeck = division.Baseline.DefaultDeck.Concat([
                    new DivisionDeckPackState(null, "Descriptor_Unit_P5_Recon", "Descriptor_Unit_P5_Transport", 0, 1)
                ]).ToArray()
            };
            var resolved = DraftResolver.Resolve(units, null, divisions, [CreateDivisionDraft(division, legacy)]).Single();
            TestAssert.Equal(DraftResolutionStatus.Conflict, resolved.Status, "旧版默认卡组修改草稿必须冲突");
            TestAssert.True(resolved.Reason.Contains("默认卡组编辑已移除", StringComparison.Ordinal), "冲突应明确提示功能已移除");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5RejectsDuplicateUnitRule()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, units, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var duplicate = division.Baseline with
            {
                UnitRules = division.Baseline.UnitRules.Concat([division.Baseline.UnitRules[0]]).ToArray()
            };
            var operation = CreateDivisionDraft(division, duplicate);
            var resolved = DraftResolver.Resolve(units, null, divisions, [operation]).Single();
            TestAssert.Equal(DraftResolutionStatus.Conflict, resolved.Status, "重复 UnitRule 草稿应冲突");
            TestAssert.True(resolved.Reason.Contains("UnitRule 重复", StringComparison.Ordinal), "冲突应说明 UnitRule 重复");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5ValidatesTransportCapability()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, _, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var target = division.Baseline with
            {
                UnitRules = division.Baseline.UnitRules.Select(rule => rule.Unit == "Descriptor_Unit_P5_Recon"
                    ? rule with { AvailableTransports = rule.AvailableTransports.Concat(["Descriptor_Unit_P5_Tank"]).ToArray() }
                    : rule).ToArray()
            };
            var errors = DivisionStateValidator.Validate(divisions, division, target);
            TestAssert.True(errors.Any(item => item.Contains("不具备唯一可识别的运输模块", StringComparison.Ordinal)), "新增非运输 Unit 必须在核心应用校验层被拒绝");
            await TestAssert.ThrowsAsync<TransactionValidationException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [CreateDivisionDraft(division, target)]),
                "非运输 Unit 不得绕过界面进入应用候选");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5ReportsSkippedDescriptorsAsInformation()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var divisionsPath = Path.Combine(root, "GameData", "Generated", "Gameplay", "Decks", "Divisions.ndf");
            File.AppendAllText(divisionsPath, "\nexport Descriptor_Deck_Division_P5_Other is TDeckDivisionDescriptor\n(\n    CfgName = 'P5_Other'\n)\n", new UTF8Encoding(false));
            var (_, _, divisions) = await LoadP5Async(root);
            TestAssert.Equal(1, divisions.SkippedNonTacticalDescriptorCount, "缺少战术师字段的同类型描述符应计为兼容提示");
            TestAssert.False(divisions.Diagnostics.Any(item => item.Contains("已跳过", StringComparison.Ordinal)), "预期跳过项不得进入 Mod 问题诊断");
            TestAssert.Equal(1, divisions.Divisions.Count, "兼容提示不得影响真正战术师的加载");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5RejectsActivationOverflow()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, _, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var target = BuildP5Target(division) with { MaxActivationPoints = 0 };
            var operation = CreateDivisionDraft(division, target);
            await TestAssert.ThrowsAsync<TransactionValidationException>(
                () => new UnitTransactionService().PrepareApplyAsync(root, [operation]),
                "超出总激活点的默认 Deck 必须被拒绝");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P5CommitsDivisionTransaction()
    {
        var root = CreateTemporaryFixtureCopy("p5-division");
        try
        {
            var (_, _, divisions) = await LoadP5Async(root);
            var division = divisions.Divisions.Single();
            var operation = CreateDivisionDraft(division, BuildP5Target(division));
            using var store = new DraftStore(root);
            _ = await store.LoadAsync();
            await store.UpsertAsync(operation);
            var service = new UnitTransactionService();
            var preview = await service.PrepareApplyAsync(root, store.Operations.ToArray());
            var result = await service.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded, "P5 事务应成功提交");
            TestAssert.Equal(0, store.Operations.Count, "提交后应清空草稿");
            var bytes = File.ReadAllBytes(Path.Combine(root, "GameData", "Generated", "Gameplay", "Decks", "Divisions.ndf"));
            TestAssert.True(bytes.Length < 3 || !bytes.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "P5 NDF 写回不得带 BOM");
            var (_, _, reloaded) = await LoadP5Async(root);
            TestAssert.Equal(1, reloaded.Divisions.Single().Baseline.DefaultDeck.Count, "提交后默认 Deck 应保持不变");
            TestAssert.True(File.Exists(Path.Combine(root, result.LogRelativePath!.Replace('/', Path.DirectorySeparatorChar))), "P5 提交应写入事务日志");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task P6SetsSelectedUnitsAndReportsImpact()
    {
        var workspace = await LoadWorkspaceAsync(Fixture("p2-unit-complete"));
        var preview = Batch(
            workspace.Units,
            "economy.commandPoints",
            UnitBatchOperation.Set,
            "100");

        TestAssert.True(preview.CanAddToDrafts, "两个可编辑价格应允许加入草稿");
        TestAssert.Equal(2, preview.TargetCount, "批量目标数错误");
        TestAssert.Equal(2, preview.CompatibleCount, "兼容字段数错误");
        TestAssert.Equal(2, preview.ChangedCount, "变化数错误");
        TestAssert.Equal(2, preview.Upserts.Count, "应生成两条普通字段草稿");
        TestAssert.True(preview.Upserts.All(item => item.TargetKind == DraftTargetKind.NdfField && item.Module == "units"), "批量不得创建旁路草稿类型");
        TestAssert.True(preview.Samples.Any(item => item.CurrentValue == "275" && item.TargetValue == "100"), "预览应包含原值与结果样本");
        TestAssert.True(preview.Impact.DivisionCount > 0, "预览应汇总师影响");
        TestAssert.Equal(2, preview.Impact.WeaponCount, "预览应去重汇总 Weapon");
        TestAssert.True(preview.Impact.AmmoCount > 0, "预览应汇总 Ammo");
    }

    private static async Task P6CalculatesNumericFormulas()
    {
        var workspace = await LoadWorkspaceAsync(Fixture("p2-unit-complete"));
        var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");

        var multiply = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Multiply, "1.1");
        TestAssert.Equal("71.5", TestAssert.Single(multiply.Upserts, "乘法应生成一项").TargetValue, "乘法结果错误");

        var add = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Add, "5");
        TestAssert.Equal("70", TestAssert.Single(add.Upserts, "加法应生成一项").TargetValue, "加法结果错误");

        var increase = Batch([tank], "movement.maxSpeed", UnitBatchOperation.IncreasePercent, "10");
        TestAssert.Equal("71.5", TestAssert.Single(increase.Upserts, "增加百分比应生成一项").TargetValue, "增加百分比结果错误");

        var decrease = Batch([tank], "movement.maxSpeed", UnitBatchOperation.DecreasePercent, "20");
        TestAssert.Equal("52", TestAssert.Single(decrease.Upserts, "减少百分比应生成一项").TargetValue, "减少百分比结果错误");
    }

    private static async Task P6AppliesRoundingAndBounds()
    {
        var workspace = await LoadWorkspaceAsync(Fixture("p2-unit-complete"));
        var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");

        var nearest = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Multiply, "1.1", UnitBatchRounding.Nearest);
        TestAssert.Equal("72", TestAssert.Single(nearest.Upserts, "四舍五入应生成一项").TargetValue, "四舍五入错误");

        var floor = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Multiply, "1.1", UnitBatchRounding.Floor);
        TestAssert.Equal("71", TestAssert.Single(floor.Upserts, "向下取整应生成一项").TargetValue, "向下取整错误");

        var ceiling = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Multiply, "1.1", UnitBatchRounding.Ceiling);
        TestAssert.Equal("72", TestAssert.Single(ceiling.Upserts, "向上取整应生成一项").TargetValue, "向上取整错误");

        var maximum = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Multiply, "2", UnitBatchRounding.None, null, "100");
        TestAssert.Equal("100", TestAssert.Single(maximum.Upserts, "最大值限制应生成一项").TargetValue, "最大值限制错误");

        var minimum = Batch([tank], "movement.maxSpeed", UnitBatchOperation.Add, "-100", UnitBatchRounding.None, "10", null);
        TestAssert.Equal("10", TestAssert.Single(minimum.Upserts, "最小值限制应生成一项").TargetValue, "最小值限制错误");
    }

    private static async Task P6UsesDraftValueAndCanRestoreBaseline()
    {
        var workspace = await LoadWorkspaceAsync(Fixture("p2-unit-complete"));
        var tank = workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US");
        var health = tank.Field("survival.health")!;
        var existing = CreateFieldDraft(tank, health, "12", "12");

        var add = Batch([tank], "survival.health", UnitBatchOperation.Add, "3", existingDrafts: [existing]);
        var operation = TestAssert.Single(add.Upserts, "已有草稿上继续计算应生成一项覆盖");
        TestAssert.Equal("15", operation.TargetValue, "公式应从草稿当前值计算");
        TestAssert.Equal("10", operation.BaselineValue, "新草稿必须保留正式文件基线");

        var restore = Batch([tank], "survival.health", UnitBatchOperation.Set, "10", existingDrafts: [existing]);
        TestAssert.Equal(0, restore.Upserts.Count, "恢复基线不应保留新草稿");
        TestAssert.Equal(existing.Id, TestAssert.Single(restore.RemoveOperationIds, "恢复基线应移除旧草稿"), "移除草稿 ID 错误");
    }

    private static async Task P6RejectsInvalidTargetsAndPersistsAtomically()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        try
        {
            var workspace = await LoadWorkspaceAsync(root);
            var invalid = Batch(workspace.Units, "armor.ecm", UnitBatchOperation.Set, "150");
            TestAssert.False(invalid.CanAddToDrafts, "ECM 越界必须阻止整批");
            TestAssert.True(invalid.Errors.Single().StartsWith("2 个 Unit 无法处理", StringComparison.Ordinal), "两个越界目标都应进入汇总错误预览");
            TestAssert.Equal(0, invalid.Upserts.Count, "有错误时不得留下部分草稿候选");

            var valid = Batch(workspace.Units, "economy.commandPoints", UnitBatchOperation.Set, "90");
            using var store = new DraftStore(root);
            _ = await store.LoadAsync();
            await store.ApplyBatchAsync(valid.Upserts, valid.RemoveOperationIds);
            TestAssert.Equal(2, store.Operations.Count, "整批应一次进入同一草稿文档");
            var loaded = await store.LoadAsync();
            TestAssert.Equal(2, loaded.Document.Operations.Count, "重启恢复应保留完整批次");
            var resolved = DraftResolver.Resolve(workspace, loaded.Document.Operations);
            TestAssert.True(resolved.All(item => item.Status == DraftResolutionStatus.Active), "批量草稿应复用现有冲突恢复");
            var transaction = new UnitTransactionService();
            var preview = await transaction.PrepareApplyAsync(root, store.Operations.ToArray());
            var result = await transaction.CommitApplyAsync(preview, store);
            TestAssert.True(result.Succeeded, "批量草稿应通过统一事务提交");
            var reloaded = await LoadWorkspaceAsync(root);
            TestAssert.True(reloaded.Units.All(unit => unit.Field("economy.commandPoints")?.DisplayValue == "90"), "统一事务应写入全部批量目标");
            var ndf = File.ReadAllBytes(Path.Combine(root, "GameData", "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf"));
            TestAssert.True(ndf.Length < 3 || !ndf.AsSpan(0, 3).SequenceEqual(new byte[] { 0xEF, 0xBB, 0xBF }), "批量写回 NDF 不得带 BOM");
        }
        finally
        {
            DeleteTemporaryFixture(root);
        }
    }

    private static async Task WpfWindowSwitchesThemesAndOpensProjects()
    {
        var root = CreateTemporaryFixtureCopy("p2-unit-complete");
        var divisionRoot = CreateTemporaryFixtureCopy("p5-division");
        var ammoOnlyRoot = CreateTemporaryFixtureCopy("p2-unit-complete");
        WriteStrategicFixture(root, "\n");
        RulesFixture184(root, "\n");
        var ammoUiPath=Path.Combine(root,"GameData/Generated/Gameplay/Gfx/Ammunition.ndf");
        File.WriteAllText(ammoUiPath,File.ReadAllText(ammoUiPath).Replace("TAmmunitionDescriptor\n(","TAmmunitionDescriptor\n(\n    Name = 'TESTUNIT01'").Replace("TAmmunitionDescriptor\r\n(","TAmmunitionDescriptor\r\n(\r\n    Name = 'TESTUNIT01'"),new UTF8Encoding(false));
        File.WriteAllText(ammoUiPath, File.ReadAllText(ammoUiPath).Replace("Name = 'TESTUNIT01'", "Name = 'TESTUNIT01'\n    AffichageMunitionParSalve = 1\n    ShotsCountPerSalvo = 2"), new UTF8Encoding(false));
        File.Delete(Path.Combine(ammoOnlyRoot, "GameData", "Generated", "Gameplay", "Gfx", "UniteDescriptor.ndf"));
        File.Delete(Path.Combine(ammoOnlyRoot, "GameData", "Generated", "Gameplay", "Gfx", "WeaponDescriptor.ndf"));
        try
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                Exception? failure = null;
                WpfApplication? application = null;
                try
                {
                    application = new WpfApplication();
                    application.InitializeComponent();
                    var settings = Path.Combine(root, ".test-settings", "recent-projects.json");
                    var themeSettings = Path.Combine(root, ".test-settings", "theme.txt");
                    ThemeManager.Initialize(new UiThemeStore(themeSettings));
                    TestAssert.Equal(AppTheme.DarkBlue, ThemeManager.CurrentTheme, "无设置时应使用参考图对应的黑蓝主题");
                    var problemLog = new ApplicationProblemLog(Path.Combine(root, ".test-settings", "problem-logs"));
                    var viewModel = new MainViewModel(new RecentProjectStore(settings), problemLog: problemLog);
                    var window = new MainWindow(viewModel);
                    var picker = new WarnoLiteModdingTool.App.Controls.SearchPicker
                    {
                        ItemsSource = Enumerable.Range(0, 200).Select(index => index == 175 ? "Weapon_KRUG_DDR" : $"Weapon_{index:000}").ToArray()
                    };
                    var pickerSearch = picker.FindName("SearchBox") as TextBox
                        ?? throw new InvalidOperationException("大列表选择器缺少搜索框。");
                    var pickerResults = picker.FindName("ResultList") as ListBox
                        ?? throw new InvalidOperationException("大列表选择器缺少结果列表。");
                    TestAssert.Equal(160, pickerResults.Items.Count, "空搜索应限制首批结果数量");
                    pickerSearch.Text = "krug";
                    TestAssert.Equal(1, pickerResults.Items.Count, "搜索应支持不区分大小写的任意位置匹配");
                    var themeSelector = window.FindName("ThemeSelector") as ComboBox
                        ?? throw new InvalidOperationException("主窗口缺少主题选择器。");
                    var headerBar = window.FindName("HeaderBar") as Border
                        ?? throw new InvalidOperationException("主窗口缺少主题顶栏。");
                    var primaryText = new TextBlock { Style = (System.Windows.Style)window.FindResource("PrimaryGridText") };
                    var indexPrimaryText = new TextBlock { Style = (System.Windows.Style)window.FindResource("IndexPrimaryGridText") };
                    var secondaryText = new TextBlock { Style = (System.Windows.Style)window.FindResource("SecondaryGridText") };
                    TestAssert.Equal(13d, primaryText.FontSize, "对象名称应使用醒目的主信息字号");
                    TestAssert.Equal("SemiBold", primaryText.FontWeight.ToString(), "对象名称应使用半粗体");
                    TestAssert.Equal(10d, secondaryText.FontSize, "内部类型、源文件和位置应使用次要信息字号");
                    TestAssert.Equal(14d, indexPrimaryText.Margin.Left, "主索引名称应增加左侧留白");
                    TestAssert.Equal(1200d, window.MinWidth, "窗口最小宽度应容纳各内部栏位下限");
                    TestAssert.True(window.FindName("TopGenerateButton") is Button, "生成 / 编译 Mod 应位于顶栏");
                    TestAssert.True(window.FindName("ModToolsOpenProjectButton") is Button, "打开 Mod 文件夹应移入 Mod 工具菜单");
                    TestAssert.True(window.FindName("UnitSelectAllButton") is Button && window.FindName("UnitClearSelectionButton") is Button, "Unit 页应常驻全选当前筛选与清空选择按钮");
                    TestAssert.True(window.FindName("WeaponSelectAllButton") is Button && window.FindName("WeaponClearSelectionButton") is Button, "Weapon 页应常驻全选当前筛选与清空选择按钮");

                    var unitFilters = window.FindName("UnitFilterPanel") as Grid
                        ?? throw new InvalidOperationException("主窗口缺少 Unit 筛选栏。");
                    TestAssert.True(window.FindName("UnitActiveFilterTags") is ItemsControl, "收起时应保留已选标签列表");
                    TestAssert.True(window.FindName("UnitFilterPopup") is System.Windows.Controls.Primitives.Popup, "筛选选项应放入可呼出面板");
                    TestAssert.Equal("\uE721", ((TextBlock)window.FindName("UnitSearchIcon")).Text, "Unit 搜索框应显示放大镜");
                    TestAssert.Equal("\uE721", ((TextBlock)window.FindName("ObjectSearchIcon")).Text, "对象搜索框应显示放大镜");

                    var requiredSplitters = new[]
                    {
                        "MainPaneSplitter", "UnitPaneSplitter", "WeaponLeftSplitter",
                        "WeaponRightSplitter", "AmmoPaneSplitter", "DivisionPaneSplitter",
                        "DraftPaneSplitter", "ObjectDetailSplitter"
                    };
                    foreach (var splitterName in requiredSplitters)
                    {
                        TestAssert.True(window.FindName(splitterName) is GridSplitter, $"{splitterName} 应提供可拖动分隔条");
                    }

                    TestAssert.Equal(220d, ((ColumnDefinition)window.FindName("ProjectPaneColumn")).MinWidth, "项目栏应保留可用最小宽度");
                    TestAssert.Equal(680d, ((ColumnDefinition)window.FindName("WorkspaceColumn")).MinWidth, "主工作区应保留可用最小宽度");
                    TestAssert.Equal(360d, ((ColumnDefinition)window.FindName("UnitListColumn")).MinWidth, "Unit 列表应保留可用最小宽度");
                    TestAssert.Equal(300d, ((ColumnDefinition)window.FindName("UnitInspectorColumn")).MinWidth, "Unit 检查器应保留可用最小宽度");
                    TestAssert.Equal(220d, ((ColumnDefinition)window.FindName("WeaponUnitsColumn")).MinWidth, "Weapon Unit 栏应保留可用最小宽度");
                    TestAssert.Equal(300d, ((ColumnDefinition)window.FindName("WeaponScopeColumn")).MinWidth, "Weapon 作用域栏应保留可用最小宽度");
                    TestAssert.Equal(360d, ((ColumnDefinition)window.FindName("WeaponFieldsColumn")).MinWidth, "Weapon 字段栏应保留可用最小宽度");
                    TestAssert.Equal(300d, ((ColumnDefinition)window.FindName("AmmoListColumn")).MinWidth, "Ammo 列表应保留可用最小宽度");
                    TestAssert.Equal(500d, ((ColumnDefinition)window.FindName("AmmoFieldsColumn")).MinWidth, "Ammo 字段栏应保留可用最小宽度");
                    TestAssert.Equal(240d, ((ColumnDefinition)window.FindName("DivisionListColumn")).MinWidth, "战术师列表应保留可用最小宽度");
                    TestAssert.Equal(480d, ((ColumnDefinition)window.FindName("DivisionEditorColumn")).MinWidth, "战术师编辑器应保留可用最小宽度");
                    TestAssert.Equal(480d, ((ColumnDefinition)window.FindName("DraftListColumn")).MinWidth, "草稿列表应保留可用最小宽度");
                    TestAssert.Equal(320d, ((ColumnDefinition)window.FindName("DraftDetailColumn")).MinWidth, "草稿详情应保留可用最小宽度");
                    TestAssert.Equal(480d, ((ColumnDefinition)window.FindName("ObjectListColumn")).MinWidth, "对象索引应保留可用最小宽度");

                    var advancedMode = window.FindName("AdvancedModeCheckBox") as CheckBox
                        ?? throw new InvalidOperationException("主窗口缺少高级模式开关。");
                    var objectDetailColumn = (ColumnDefinition)window.FindName("ObjectDetailColumn");
                    advancedMode.IsChecked = true;
                    TestAssert.Equal(280d, objectDetailColumn.MinWidth, "高级详情打开后应保留可用最小宽度");
                    TestAssert.True(objectDetailColumn.Width.Value >= 280d, "高级详情打开后应恢复可见宽度");
                    advancedMode.IsChecked = false;
                    TestAssert.Equal(0d, objectDetailColumn.Width.Value, "高级详情关闭后应收起");
                    advancedMode.IsChecked = true;
                    TestAssert.True(objectDetailColumn.Width.Value >= 280d, "高级详情再次打开后应恢复宽度");
                    advancedMode.IsChecked = false;
                    TestAssert.Equal(0, themeSelector.SelectedIndex, "主题选择器应显示当前黑蓝主题");
                    TestAssert.Equal("#FF050608", ((SolidColorBrush)window.FindResource("BackgroundBrush")).Color.ToString(), "黑蓝主题应使用近黑背景");
                    themeSelector.SelectedIndex = 1;
                    TestAssert.Equal(AppTheme.LightBlue, ThemeManager.CurrentTheme, "选择白蓝后应立即切换主题");
                    TestAssert.Equal("#FFEEF4FA", ((SolidColorBrush)window.FindResource("BackgroundBrush")).Color.ToString(), "白蓝主题应使用浅色背景");
                    TestAssert.Equal("#FFFFFFFF", ((SolidColorBrush)headerBar.Background).Color.ToString(), "已创建的顶栏也应即时切换到白蓝主题");
                    TestAssert.Equal(AppTheme.LightBlue, new UiThemeStore(themeSettings).Load(), "白蓝选择应持久化");
                    themeSelector.SelectedIndex = 0;
                    TestAssert.Equal(AppTheme.DarkBlue, ThemeManager.CurrentTheme, "应可从白蓝切回黑蓝");
                    TestAssert.Equal("#FF0C1016", ((SolidColorBrush)headerBar.Background).Color.ToString(), "已创建的顶栏应能切回黑蓝主题");
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(window.Dispatcher));
                    RunWithDispatcher(viewModel.OpenProjectAsync(root), window.Dispatcher);
                    DrainDispatcher(window.Dispatcher);
                    TestAssert.True(viewModel.UnitWorkspace is not null, "打开完整 Unit Mod 后应建立工作区");
                    TestAssert.Equal(2, viewModel.UnitWorkspace!.Units.Count, "WPF 回归应加载两条 Unit");
                    TestAssert.True(viewModel.WeaponWorkspace is not null, "完整 Unit Mod 应建立 Weapon 工作区");
                    TestAssert.True(viewModel.AmmoWorkspace is not null, "完整 Unit Mod 应建立可直接编辑的 Ammo 工作区");
                    TestAssert.True(viewModel.UnitWorkspace.FieldSections.Count >= 5, "Unit 数值面板应按多个大类分块");
                    TestAssert.True(viewModel.AmmoWorkspace!.FieldSections.Count > 0, "Ammo 数值面板应生成分组");
                    var deploymentField = viewModel.UnitWorkspace.Fields.Single(item => item.Key == "deployment.shift");
                    TestAssert.True(deploymentField.IsDeploymentEditor, "前置部署在普通模式应提供可输入编辑器和参考预设");
                    TestAssert.True(deploymentField.DeploymentPresets.Any(item => item.Display.Contains("侦察", StringComparison.Ordinal)), "2473.49823322 应带侦察备注");
                    TestAssert.True(viewModel.WeaponWorkspace!.Fields.Any(item => item.IsReferenceEditor), "武器挂载 Ammo 引用应使用可搜索编辑器");
                    viewModel.UnitWorkspace.ClearBatchSelection();
                    viewModel.UnitWorkspace.SelectVisibleForBatch();
                    TestAssert.Equal(viewModel.UnitWorkspace.VisibleUnitCount, viewModel.UnitWorkspace.BatchSelectedCount, "Unit 全选应只选择当前筛选结果");
                    viewModel.UnitWorkspace.ClearBatchSelection();
                    TestAssert.Equal(0, viewModel.UnitWorkspace.BatchSelectedCount, "Unit 清空选择应一次清零");
                    viewModel.WeaponWorkspace.ClearScopeSelection();
                    viewModel.WeaponWorkspace.SelectVisibleScopeUnits();
                    TestAssert.True(viewModel.WeaponWorkspace.WeaponScopeSelectedCount > 0, "Weapon 全选当前筛选应选择当前 Weapon 的有效引用者");
                    viewModel.WeaponWorkspace.ClearScopeSelection();
                    TestAssert.Equal(0, viewModel.WeaponWorkspace.WeaponScopeSelectedCount, "Weapon 清空选择应一次清零");
                    var filterDimension = viewModel.UnitWorkspace.FilterDimensions.First(dimension => dimension.Options.Count >= 2);
                    filterDimension.Options[0].IsSelected = true;
                    var firstFilterCount = viewModel.UnitWorkspace.VisibleUnitCount;
                    TestAssert.Equal(1, viewModel.UnitWorkspace.ActiveFilterCount, "选中一个筛选值应生成一枚可移除标签");
                    filterDimension.Options[1].IsSelected = true;
                    TestAssert.True(viewModel.UnitWorkspace.VisibleUnitCount >= firstFilterCount, "同一类多选应采用 OR 语义");
                    TestAssert.Equal(2, viewModel.UnitWorkspace.ActiveFilterCount, "同一类多选应显示两枚标签");
                    viewModel.UnitWorkspace.ClearFilters();
                    TestAssert.Equal(2, viewModel.UnitWorkspace.VisibleUnitCount, "清空标签应恢复完整 Unit 列表");
                    TestAssert.Equal(0, viewModel.UnitWorkspace.ActiveFilterCount, "清空后不应留下已选标签");
                    TestAssert.Equal("\uE721", ((TextBlock)window.FindName("AmmoSearchIcon")).Text, "Ammo 搜索框应显示放大镜");
                    TestAssert.True(window.FindName("UnitFieldSections") is ItemsControl, "Unit 页应绑定分组字段容器");
                    TestAssert.True(window.FindName("WeaponFieldSections") is ItemsControl, "Weapon 页应绑定分组字段容器");
                    TestAssert.True(window.FindName("AmmoFieldSections") is ItemsControl, "Ammo 页应绑定分组字段容器");
                    TestAssert.True(window.FindName("ModProblemList") is ListBox && window.FindName("ToolProblemList") is ListBox, "问题中心应分开 Mod 与工具列表");
                    TestAssert.Equal(3, viewModel.ObjectsView.SortDescriptions.Count, "对象索引应具有明确且稳定的默认排序");
                    TestAssert.Equal(nameof(NdfObjectInfo.RelativeSourceFile), viewModel.ObjectsView.SortDescriptions[0].PropertyName, "对象索引应先按源文件排序");
                    TestAssert.Equal(ListSortDirection.Ascending, viewModel.ObjectsView.SortDescriptions[0].Direction, "源文件应默认升序");
                    TestAssert.Equal(nameof(NdfObjectInfo.LineNumber), viewModel.ObjectsView.SortDescriptions[1].PropertyName, "同一文件内应再按物理行号排序");
                    TestAssert.Equal(ListSortDirection.Ascending, viewModel.ObjectsView.SortDescriptions[1].Direction, "行号应默认升序");
                    TestAssert.Equal(nameof(NdfObjectInfo.CharacterOffset), viewModel.ObjectsView.SortDescriptions[2].PropertyName, "同一行对象应以字符偏移稳定排序");
                    var sortLabel = (TextBlock)window.FindName("ObjectSortLabel");
                    TestAssert.Equal("排序：源文件 ↑ · 行号 ↑", sortLabel.Text, "界面应直接说明默认排序及方向");
                    var objectGrid = (DataGrid)window.FindName("ObjectIndexGrid");
                    TestAssert.Equal(ListSortDirection.Ascending, objectGrid.Columns[2].SortDirection, "源文件表头应显示升序上标");
                    TestAssert.Equal(ListSortDirection.Ascending, objectGrid.Columns[3].SortDirection, "行号表头应显示升序上标");

                    var ammoModule = viewModel.Modules.Single(item => item.Key == "ammo");
                    viewModel.SelectedModule = ammoModule;
                    TestAssert.True(viewModel.IsAmmoModule, "点击弹药模块应进入 Ammo 直编页而不是通用对象索引");
                    var ammoWorkspace = viewModel.AmmoWorkspace!;
                    TestAssert.True(ammoWorkspace.SelectedAmmo is not null, "Ammo 直编页应默认选中一项 Ammo");
                    TestAssert.True(ammoWorkspace.Fields.Count > 0, "Ammo 直编页应列出当前 Ammo 可安全定位的现有字段");
                    TestAssert.True(ammoWorkspace.References.Count > 0, "Ammo 页应直接使用现有索引展示 Weapon→Unit 引用链");
                    ammoWorkspace.ReferenceSearchText = "Tank_US";
                    TestAssert.Equal(1, ammoWorkspace.References.Count, "Ammo 引用搜索应支持 Unit 内部名任意片段");
                    ammoWorkspace.ReferenceSearchText = string.Empty;
                    var ammoSource = Path.Combine(root, "GameData", "Generated", "Gameplay", "Gfx", "Ammunition.ndf");
                    var ammoBefore = File.ReadAllBytes(ammoSource);
                    var directField = ammoWorkspace.Fields.Single(item => item.Field.Key == "ammo.damage.physical");
                    directField.EditValue = "11";
                    RunWithDispatcher(directField.FlushAsync(), window.Dispatcher);
                    TestAssert.Equal(1, viewModel.UnitWorkspace.DraftCount, "Ammo 直编应生成一项语义草稿");
                    var ammoDraft = viewModel.UnitWorkspace.DraftItems.Single();
                    TestAssert.Equal("弹药", ammoDraft.Module, "草稿总览应标明 Ammo 模块");
                    TestAssert.Equal("全部引用", ammoDraft.Scope, "Ammo 直编应明确采用全部引用作用域");
                    TestAssert.Equal(DraftEditScope.AllReferences, ammoDraft.Resolved.Operation.EditScope, "Ammo 草稿应使用全局共享对象写入语义");
                    TestAssert.True(ammoBefore.SequenceEqual(File.ReadAllBytes(ammoSource)), "Ammo 直编阶段只能写草稿，不能修改正式 NDF");

                    var draftsModule = viewModel.Modules.Single(item => item.Key == "drafts");
                    viewModel.SelectedModule = draftsModule;
                    TestAssert.True(viewModel.IsDraftModule, "点击草稿总览应进入独立页面");
                    TestAssert.True(window.FindName("DraftOverviewGrid") is DataGrid, "草稿总览应提供可选择的完整列表");
                    TestAssert.True(viewModel.SelectedDraft is not null, "进入草稿总览时应默认显示第一项草稿详情");
                    TestAssert.Equal(ammoDraft.Summary, viewModel.SelectedDraft!.Summary, "草稿详情应对应列表选中项");
                    RunWithDispatcher(viewModel.OpenProjectAsync(divisionRoot), window.Dispatcher);
                    TestAssert.True(viewModel.DivisionWorkspace is not null, "打开完整战术师 Mod 后应建立战术师工作区");
                    TestAssert.Equal(1, viewModel.DivisionWorkspace!.Divisions.Count, "WPF 回归应加载一个战术师");
                    TestAssert.True(viewModel.DivisionWorkspace.TransportCandidates.All(item => item.Name == "Descriptor_Unit_P5_Transport"), "运输多选候选必须只包含结构确认的运输 Unit");
                    viewModel.DivisionWorkspace.IsTransportPickerOpen = true;
                    viewModel.DivisionWorkspace.TransportSearchText = "p5_trans";
                    TestAssert.Equal(1, viewModel.DivisionWorkspace.TransportCandidatesView.Cast<object>().Count(), "运输多选搜索应支持内部名任意片段");
                    viewModel.DivisionWorkspace.TransportSearchText = "tank";
                    TestAssert.Equal(0, viewModel.DivisionWorkspace.TransportCandidatesView.Cast<object>().Count(), "非运输 Unit 不得出现在运输搜索结果中");
                    viewModel.DivisionWorkspace.CancelTransportSelection();
                    RunWithDispatcher(viewModel.OpenProjectAsync(root), window.Dispatcher);
                    viewModel.SelectedModule = viewModel.Modules.Single(m => m.Key == "strategic");
                    DrainDispatcher(window.Dispatcher);
                    Assert(viewModel.IsStrategicModule && viewModel.StrategicWorkspace?.Companies.Count == 1, "战略工作区导航与树绑定");
                    var strategicVm = viewModel.StrategicWorkspace!;
                    strategicVm.Companies[0].Children[0].Children[0].Count = 3;
                    RunWithDispatcher(strategicVm.FlushAsync(), window.Dispatcher);
                    viewModel.AdvancedMode = true;
                    viewModel.AdvancedMode = false;
                    Assert(viewModel.UnitWorkspace!.DraftItems.Any(d => d.Resolved.Operation.TargetKind == DraftTargetKind.StrategicPlan), "切换模式保留并显示战略草稿");
                    WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("en");
                    DrainDispatcher(window.Dispatcher);
                    SaveUiSnapshot(window, "debug-en.png");
                    Assert(FindVisualChildren<System.Windows.Controls.TextBlock>((System.Windows.DependencyObject)window.Content).Any(t => t.Text == "Army General"), "英文导航即时刷新: " + WarnoLiteModdingTool.App.Localisation.UiText.T("将军模式") + " / " + string.Join(",", typeof(WarnoLiteModdingTool.App.Localisation.UiText).Assembly.GetManifestResourceNames()));
                    var strategicTree = FindVisualChildren<TreeView>((System.Windows.DependencyObject)window.Content).First(t => t.Items.Count > 0);
                    if (strategicTree.ItemContainerGenerator.ContainerFromIndex(0) is TreeViewItem firstCompany) { firstCompany.IsExpanded = true; firstCompany.IsSelected = true; }
                    DrainDispatcher(window.Dispatcher);
                    SaveUiSnapshot(window, "strategic-en.png");
                    var settingsWindow = new WarnoLiteModdingTool.App.Settings.SettingsWindow();
                    DrainDispatcher(window.Dispatcher); SaveUiSnapshot(settingsWindow, "settings-en.png"); FindVisualChildren<TabControl>((System.Windows.DependencyObject)settingsWindow.Content).Single().SelectedIndex=1;DrainDispatcher(window.Dispatcher);SaveUiSnapshot(settingsWindow,"settings-appearance.png");settingsWindow.Close();
                    WarnoLiteModdingTool.App.Localisation.UiText.Current.SetLanguage("zh-CN");
                    DrainDispatcher(window.Dispatcher);
                    SaveUiSnapshot(window, "strategic-zh.png");
                    Assert(strategicVm.Roots.Count == 1 && strategicVm.Roots[0].Children == strategicVm.Companies, "营团为显式树根，仍共享同一编制集合");
                    foreach(var theme in Enum.GetValues<AppTheme>()) { ThemeManager.ApplyTheme(theme);DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"theme-"+theme+".png"); }
                    ThemeManager.ApplyTheme(AppTheme.DarkBlue);
                    viewModel.AdvancedMode = false;
                    using(var mappedStore = new DraftStore(root)) { var mapping = new WarnoLiteModdingTool.App.Controls.StrategicMappingWindow(strategicVm);SaveUiSnapshot(mapping,"mapping-basic.png");var grid=FindVisualChildren<DataGrid>((System.Windows.DependencyObject)mapping.Content).Single();Assert(grid.Columns[0].IsReadOnly,"普通模式索引只读");mapping.Close(); }
                    viewModel.AdvancedMode = true;
                    var advancedMapping = new WarnoLiteModdingTool.App.Controls.StrategicMappingWindow(strategicVm);SaveUiSnapshot(advancedMapping,"mapping-advanced.png");Assert(!FindVisualChildren<DataGrid>((System.Windows.DependencyObject)advancedMapping.Content).Single().Columns[0].IsReadOnly,"高级模式允许数字索引");advancedMapping.Close();
                    viewModel.AdvancedMode = false;
                    viewModel.SelectedModule = viewModel.Modules.Single(m=>m.Key=="drafts");DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"drafts.png");
                    var draftItems=viewModel.UnitWorkspace!.DraftItems;var savedItems=draftItems.ToArray();draftItems.Clear();
                    foreach(var item in savedItems)draftItems.Add(new WarnoLiteModdingTool.App.ViewModels.Drafts.DraftItemViewModel(item.Resolved with {Operation=item.Resolved.Operation with {GroupId="qa-batch"}}));
                    DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"draft-batch-collapsed.png");
                    var batchExpander=FindVisualChildren<Expander>((DataGrid)window.FindName("DraftOverviewGrid")).First(e=>e.Name=="BatchExpander");Assert(!batchExpander.IsExpanded,"批次默认折叠");batchExpander.IsExpanded=true;DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"draft-batch-expanded.png");draftItems.Clear();foreach(var item in savedItems)draftItems.Add(item);
                    var mini=new WarnoLiteModdingTool.App.Controls.UnitMiniFilter {ItemsSource=viewModel.UnitWorkspace.Units,IsExpanded=true};var miniWindow=new System.Windows.Window {Content=mini,Width=600,Height=360};SaveUiSnapshot(miniWindow,"mini-filter.png");
                    var countryChoice=FindVisualChildren<CheckBox>(mini).First(c=>Equals(c.Tag,"US"));countryChoice.IsChecked=true;Assert(viewModel.UnitWorkspace.Units.Where(u=>mini.Matches(u)).All(u=>u.Unit.Country=="US"),"小筛选国家条件生效");miniWindow.Close();
                    viewModel.SelectedModule = viewModel.Modules.Single(m=>m.Key=="units");
                    Assert(viewModel.UnitWorkspace!.Fields.Where(f=>f.Key=="structure.country" || f.Key=="structure.coalition").All(f=>f.IsEditable),"基本身份字段不因模式锁定");
                    var backdrop=Path.Combine(root,"test-background.png");
                    var pixels = new byte[32*24*4];for(var x=0;x<pixels.Length;x+=4){pixels[x]=240;pixels[x+1]=120;pixels[x+2]=20;pixels[x+3]=255;}
                    var bitmap=System.Windows.Media.Imaging.BitmapSource.Create(32,24,96,96,PixelFormats.Bgra32,null,pixels,128);var encoder=new System.Windows.Media.Imaging.PngBitmapEncoder();encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var file=File.Create(backdrop))encoder.Save(file);
                    VerifyBatch18Ui(viewModel,window,root);
                    Verify181Ui(viewModel);
                    Verify182Ui(viewModel,window);
                    Verify184Ui(viewModel,window);
                    Verify185Ui(viewModel,window);
                    BackgroundAppearance.Apply(new WarnoLiteModdingTool.App.Settings.UiPreferences(BackgroundImage:backdrop,BackgroundOpacity:.2));DrainDispatcher(window.Dispatcher);SaveUiSnapshot(window,"background.png");Assert(application.Resources["MainBackgroundBrush"] is System.Windows.Media.ImageBrush,"背景图片作为独立画刷，不透明度不影响文字");
                    ThemeManager.ApplyTheme(AppTheme.DarkBlue);
                    RunWithDispatcher(strategicVm.UndoAsync(), window.Dispatcher);
                    viewModel.AdvancedMode = true;
                    RunWithDispatcher(viewModel.OpenProjectAsync(ammoOnlyRoot), window.Dispatcher);
                    TestAssert.Equal(0, viewModel.UnitWorkspace?.Units.Count ?? -1, "缺少 Unit 文件时应建立空事务工作区而不是阻断 Ammo");
                    TestAssert.True(viewModel.WeaponWorkspace is null, "缺少 Weapon 文件时只应禁用 Weapon 编辑器");
                    TestAssert.True(viewModel.AmmoWorkspace is not null, "Ammo 源文件独立存在时仍应开放直接编辑器");
                    TestAssert.Equal("ammo", viewModel.SelectedModule?.Key, "Ammo-only 项目应默认进入首个可用的弹药模块");
                    TestAssert.True(viewModel.IsAmmoModule, "Ammo-only 项目不应退回通用只读索引");
                    RunWithDispatcher(viewModel.OpenProjectAsync(root), window.Dispatcher);
                    Verify186Ui(viewModel, window, root);
                    application.Shutdown();
                    if (failure is not null)
                    {
                        completion.TrySetException(failure);
                    }
                    else
                    {
                        completion.TrySetResult();
                    }
                }
                catch (Exception exception)
                {
                    completion.TrySetException(exception);
                    application?.Shutdown();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            await completion.Task.WaitAsync(TimeSpan.FromSeconds(30));
            TestAssert.True(thread.Join(TimeSpan.FromSeconds(5)), "WPF 回归线程应正常退出");
        }
        finally
        {
            DeleteTemporaryFixture(root);
            DeleteTemporaryFixture(divisionRoot);
            DeleteTemporaryFixture(ammoOnlyRoot);
        }
    }

    private static void RunWithDispatcher(Task task, Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = task.ContinueWith(
            _ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
        Dispatcher.PushFrame(frame);
        task.GetAwaiter().GetResult();
    }

    private static void DrainDispatcher(Dispatcher dispatcher)
    {
        var frame = new DispatcherFrame();
        _ = dispatcher.BeginInvoke(
            new Action(() => frame.Continue = false),
            DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }

    private static UnitBatchPreview Batch(
        IReadOnlyList<UnitRecord> targets,
        string fieldKey,
        UnitBatchOperation operation,
        string operand,
        UnitBatchRounding rounding = UnitBatchRounding.None,
        string? minimum = null,
        string? maximum = null,
        IReadOnlyList<DraftOperation>? existingDrafts = null) =>
        UnitBatchPlanner.Preview(new UnitBatchRequest(
            Guid.NewGuid().ToString("N"),
            targets,
            fieldKey,
            operation,
            operand,
            rounding,
            minimum,
            maximum,
            existingDrafts ?? []));

    private static DivisionEditState BuildP5Target(DivisionRecord division)
    {
        var baseline = division.Baseline;
        return baseline with
        {
            MaxActivationPoints = 12,
            Tags = baseline.Tags.Concat(["EDITOR_TEST"]).ToArray(),
            UnitRules = baseline.UnitRules.Select(rule => rule.Unit == "Descriptor_Unit_P5_Tank"
                ? rule with { NumberOfUnitInPack = 5 }
                : rule).ToArray(),
            CostCurves = baseline.CostCurves.Select(curve => curve.Category == "Tanks"
                ? curve with { Costs = [1, 3] }
                : curve).ToArray()
        };
    }

    private static DraftOperation CreateDivisionDraft(DivisionRecord division, DivisionEditState target)
    {
        var baselineRaw = DivisionDraftCodec.Serialize(division.Baseline);
        var targetRaw = DivisionDraftCodec.Serialize(target);
        return new DraftOperation(
            DraftOperation.CreateId(DraftTargetKind.DivisionPlan, division.Source.RelativeSourceFile, division.Name, "division.plan"),
            $"division:{division.Name}",
            DraftTargetKind.DivisionPlan,
            "divisions",
            division.Source.RelativeSourceFile,
            division.Name,
            division.Source.TypeName,
            "division.plan",
            "Division+DivisionRule+Deck+DeckPack+CostMatrix",
            "DivisionEditState",
            "P5 baseline",
            baselineRaw,
            "P5 target",
            targetRaw,
            $"{division.DisplayName} · P5 test",
            null,
            false,
            DateTimeOffset.UtcNow);
    }

    private static async Task<(ModProjectContext Context, UnitWorkspaceData Units, DivisionWorkspaceData Divisions)> LoadP5Async(string root)
    {
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        var divisions = await new DivisionProjectLoader().LoadAsync(context, index, units);
        return (context, units, divisions);
    }

    private static async Task<(UnitWorkspaceData Workspace, UnitRecord Tank)> LoadTankAsync(string root)
    {
        var workspace = await LoadWorkspaceAsync(root);
        return (workspace, workspace.Units.Single(unit => unit.Name == "Descriptor_Unit_Test_Tank_US"));
    }

    private static async Task<UnitWorkspaceData> LoadWorkspaceAsync(string root)
    {
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context);
        return await new UnitProjectLoader().LoadAsync(context, index);
    }

    private static async Task<(ModProjectContext Context, UnitWorkspaceData Units, WeaponWorkspaceData Weapons)> LoadP4Async(string root)
    {
        var context = new ModProjectDetector().Detect(root);
        var index = await new ProjectIndexer().IndexAsync(context);
        var units = await new UnitProjectLoader().LoadAsync(context, index);
        var weapons = await new WeaponProjectLoader().LoadAsync(context, index, units);
        return (context, units, weapons);
    }

    private static DraftOperation CreateWeaponDraft(
        WeaponFieldValue field,
        string target,
        DraftEditScope scope,
        IReadOnlyList<string> selectedUnits,
        string? contextWeapon)
    {
        TestAssert.True(WeaponValueConverter.TryFormat(field, target, out var normalized, out var raw, out var error), $"P4 测试目标应有效：{error}");
        var kind = field.Definition.Owner switch
        {
            WeaponFieldOwner.Ammo => DraftTargetKind.AmmoField,
            WeaponFieldOwner.MountedWeapon when field.Definition.FieldName == "Ammunition" => DraftTargetKind.MountedWeaponAmmo,
            _ => DraftTargetKind.WeaponField
        };
        var module = kind == DraftTargetKind.AmmoField ? "ammo" : "weapons";
        return new DraftOperation(
            DraftOperation.CreateId(kind, field.Location.RelativeSourceFile, field.OwnerObjectName, field.Key),
            null,
            kind,
            module,
            field.Location.RelativeSourceFile,
            field.OwnerObjectName,
            field.OwnerObjectType,
            field.Key,
            field.Location.FieldPath,
            field.Definition.ValueKind.ToString(),
            field.DisplayValue,
            field.RawValue,
            normalized,
            raw,
            $"{field.OwnerObjectName} · {field.Definition.Label}: {field.DisplayValue} → {normalized}",
            null,
            false,
            DateTimeOffset.UtcNow,
            EditScope: scope,
            SelectedUnitNames: selectedUnits,
            ContextWeaponName: contextWeapon);
    }

    private static DraftOperation CreateUnitWeaponDraft(
        UnitRecord unit,
        string sourceWeapon,
        string targetWeapon,
        DraftEditScope scope,
        IReadOnlyList<string> selected)
    {
        var key = $"weapon.reference.{sourceWeapon}";
        return new DraftOperation(
            DraftOperation.CreateId(DraftTargetKind.UnitWeaponReference, unit.Source.RelativeSourceFile, unit.Name, key),
            "weapon-replace-test",
            DraftTargetKind.UnitWeaponReference,
            "units",
            unit.Source.RelativeSourceFile,
            unit.Name,
            unit.Source.TypeName,
            key,
            "ModulesDescriptors.WeaponDescriptor",
            "Reference",
            sourceWeapon,
            $"$/GFX/Weapon/{sourceWeapon}",
            targetWeapon,
            $"$/GFX/Weapon/{targetWeapon}",
            $"{unit.Name} · Weapon：{sourceWeapon} → {targetWeapon}",
            null,
            false,
            DateTimeOffset.UtcNow,
            EditScope: scope,
            SelectedUnitNames: selected,
            ContextWeaponName: sourceWeapon);
    }

    private static string Candidate(ApplyPreview preview, string fileName)
    {
        var file = preview.Files.Single(item => item.RelativePath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase));
        return new UTF8Encoding(false, true).GetString(file.CandidateBytes);
    }

    private static DraftOperation CreateNameDraft(UnitRecord unit, string targetName, string targetToken) =>
        new(
            DraftOperation.CreateId(DraftTargetKind.UnitName, unit.UnitsCsvRelativePath!, unit.Name, "localisation.gameName"),
            $"name:{unit.Name}",
            DraftTargetKind.UnitName,
            "units",
            unit.UnitsCsvRelativePath!,
            unit.Name,
            unit.Source.TypeName,
            "localisation.gameName",
            "UNITS.csv.REFTEXT",
            "String",
            unit.DisplayName,
            unit.DisplayName,
            targetName,
            targetName,
            $"{unit.DisplayName} · 游戏内名称：{unit.DisplayName} → {targetName}",
            targetToken,
            unit.NameTokenRequiresReplacement,
            DateTimeOffset.UtcNow,
            unit.NameToken);

    private static DraftOperation CreateFieldDraft(
        UnitRecord unit,
        UnitFieldValue field,
        string targetValue,
        string targetRaw)
    {
        var relative = field.Location!.RelativeSourceFile;
        return new DraftOperation(
            DraftOperation.CreateId(DraftTargetKind.NdfField, relative, unit.Name, field.Definition.Key),
            null,
            DraftTargetKind.NdfField,
            "units",
            relative,
            unit.Name,
            unit.Source.TypeName,
            field.Definition.Key,
            field.Location.FieldPath,
            field.Definition.ValueKind.ToString(),
            field.DisplayValue,
            field.RawValue,
            targetValue,
            targetRaw,
            $"{unit.DisplayName} · {field.Definition.Label}: {field.DisplayValue} → {targetValue}",
            null,
            false,
            DateTimeOffset.UtcNow);
    }

    private static string CreateTemporaryFixtureCopy(string name)
    {
        var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "warno-editor-tests"));
        var target = Path.Combine(safeRoot, Guid.NewGuid().ToString("N"));
        CopyDirectory(Fixture(name), target);
        return target;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)));
        }
    }

    private static void DeleteTemporaryFixture(string root)
    {
        var safeRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "warno-editor-tests"));
        var resolved = Path.GetFullPath(root);
        if (resolved.StartsWith(safeRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
            Directory.Exists(resolved))
        {
            Directory.Delete(resolved, true);
        }
    }

    private static string Fixture(string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    private static Dictionary<string, (long Length, DateTime LastWriteUtc)> SnapshotFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => new FileInfo(path))
            .ToDictionary(
                file => Path.GetRelativePath(root, file.FullName),
                file => (file.Length, file.LastWriteTimeUtc),
                StringComparer.OrdinalIgnoreCase);

    private static Dictionary<string, (long Length, DateTime LastWriteUtc)> SnapshotFormalFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).StartsWith(".warno-editor" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            .Select(path => new FileInfo(path))
            .ToDictionary(
                file => Path.GetRelativePath(root, file.FullName),
                file => (file.Length, file.LastWriteTimeUtc),
                StringComparer.OrdinalIgnoreCase);
}
