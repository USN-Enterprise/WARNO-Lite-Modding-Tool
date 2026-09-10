using System.Text;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Indexing;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Projects;
using WarnoLiteModdingTool.Core.Units;
using WarnoLiteModdingTool.Core.Weapons;
using WarnoLiteModdingTool.Core.Divisions;
using WarnoLiteModdingTool.Core.Strategic;

namespace WarnoLiteModdingTool.Core.Transactions;

public sealed class UnitApplyPlanner(
    ModProjectDetector? detector = null,
    ProjectIndexer? indexer = null,
    UnitProjectLoader? unitLoader = null,
    WeaponProjectLoader? weaponLoader = null,
    DivisionProjectLoader? divisionLoader = null)
{
    private readonly ModProjectDetector _detector = detector ?? new ModProjectDetector();
    private readonly ProjectIndexer _indexer = indexer ?? new ProjectIndexer();
    private readonly UnitProjectLoader _unitLoader = unitLoader ?? new UnitProjectLoader();
    private readonly WeaponProjectLoader _weaponLoader = weaponLoader ?? new WeaponProjectLoader();
    private readonly DivisionProjectLoader _divisionLoader = divisionLoader ?? new DivisionProjectLoader();
    private readonly NdfFieldLocator _fieldLocator = new();
    private readonly NdfTopLevelScanner _scanner = new();

    public async Task<ApplyPreview> PrepareAsync(
        string projectRoot,
        IReadOnlyList<DraftOperation> operations,
        CancellationToken cancellationToken = default)
    {
        if (operations.Count == 0)
        {
            throw new TransactionValidationException("没有可应用的草稿。");
        }

        if (operations.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count() != operations.Count)
        {
            throw new TransactionValidationException("草稿包含重复操作编号。");
        }

        var root = Path.GetFullPath(projectRoot);
        var context = _detector.Detect(root);
        if (!context.IsRecognized)
        {
            throw new TransactionValidationException(context.Summary);
        }

        var index = await _indexer.IndexAsync(context, cancellationToken: cancellationToken);
        var unitCapability = index.Modules.FirstOrDefault(item => item.Key == "units");
        if (operations.Any(o => o.TargetKind is not (DraftTargetKind.StrategicPlan or DraftTargetKind.GlobalRule)) &&
            (unitCapability?.CanScan != true || unitCapability.Availability == ModuleAvailability.ParseError))
        {
            throw new TransactionValidationException("单位模块当前不可安全写入；请先处理扫描诊断。");
        }

        var hasWeaponOperations = operations.Any(item => item.TargetKind is
            DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo or DraftTargetKind.AmmoField or DraftTargetKind.UnitWeaponReference or DraftTargetKind.UnitCreate or DraftTargetKind.AmmoName);
        if (hasWeaponOperations)
        {
            var weaponCapability = index.Modules.FirstOrDefault(item => item.Key == "weapons");
            var ammoCapability = index.Modules.FirstOrDefault(item => item.Key == "ammo");
            if (weaponCapability?.CanScan != true || weaponCapability.Availability == ModuleAvailability.ParseError ||
                ammoCapability?.CanScan != true || ammoCapability.Availability == ModuleAvailability.ParseError)
            {
                throw new TransactionValidationException("Weapon/Ammo 模块当前不完整或存在解析错误，无法安全计算共享影响。");
            }
        }

        var workspace = await _unitLoader.LoadAsync(context, index, cancellationToken);
        WeaponWorkspaceData? weaponWorkspace = null;
        if (hasWeaponOperations)
        {
            weaponWorkspace = await _weaponLoader.LoadAsync(context, index, workspace, cancellationToken);
        }
        DivisionWorkspaceData? divisionWorkspace = null;
        if (operations.Any(item => item.TargetKind == DraftTargetKind.DivisionPlan || item.TargetKind==DraftTargetKind.UnitCreate && UnitCreation.Read(item).Divisions.Count>0))
        {
            var divisionCapability = index.Modules.FirstOrDefault(item => item.Key == "divisions");
            if (divisionCapability?.CanScan != true || divisionCapability.Availability == ModuleAvailability.ParseError)
            {
                throw new TransactionValidationException("战术师模块存在解析错误，无法安全应用 P5 草稿。");
            }

            divisionWorkspace = await _divisionLoader.LoadAsync(context, index, workspace, cancellationToken);
            if (!divisionWorkspace.HasCompleteFileSet)
            {
                throw new TransactionValidationException("战术师编辑所需的五个文件不完整。");
            }
        }

        StrategicWorkspace? strategic = null;
        if (operations.Any(o => o.TargetKind == DraftTargetKind.StrategicPlan))
            strategic = await new StrategicLoader().LoadAsync(context, index, workspace, cancellationToken);
        var resolved = DraftResolver.Resolve(workspace, weaponWorkspace, divisionWorkspace, operations, strategic);
        var conflicts = resolved.Where(item => item.Status == DraftResolutionStatus.Conflict).ToArray();
        if (conflicts.Length > 0)
        {
            throw new TransactionValidationException(
                "草稿基线冲突：" + string.Join("；", conflicts.Take(5).Select(item => $"{item.Operation.Summary}（{item.Reason}）")));
        }

        ValidateOperationIdentity(operations);
        if (operations.Any(o=>o.Module != "rules")) ValidateArmorFamilies(workspace, operations);
        if (weaponWorkspace is not null)
        {
            ValidateDamageFamilies(workspace, weaponWorkspace, operations);
        }
        var units = workspace.Units.ToDictionary(item => item.Name, StringComparer.Ordinal);
        var snapshots = new Dictionary<string, TextFileSnapshot>(StringComparer.OrdinalIgnoreCase);
        var replacements = new Dictionary<string, List<TextReplacement>>(StringComparer.OrdinalIgnoreCase);
        var summaries = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        TextFileSnapshot NdfSnapshot(string relativePath)
        {
            var normalized = Normalize(relativePath);
            if (!snapshots.TryGetValue(normalized, out var snapshot))
            {
                snapshot = TextFileSnapshot.Load(root, normalized, FormalTextFileKind.Ndf);
                snapshots[normalized] = snapshot;
            }

            return snapshot;
        }

        void AddReplacement(string relativePath, TextReplacement replacement, string summary)
        {
            var normalized = Normalize(relativePath);
            _ = NdfSnapshot(normalized);
            if (!replacements.TryGetValue(normalized, out var list))
            {
                list = [];
                replacements[normalized] = list;
            }

            list.Add(replacement);
            if (!summaries.TryGetValue(normalized, out var fileSummaries))
            {
                fileSummaries = [];
                summaries[normalized] = fileSummaries;
            }

            fileSummaries.Add(summary);
        }

        foreach (var operation in operations.Where(item => item.TargetKind == DraftTargetKind.NdfField))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[operation.ObjectName];
            var field = unit.Field(operation.FieldKey)
                ?? throw new TransactionValidationException($"字段不存在：{operation.FieldKey}");
            if (!UnitValueConverter.TryFormatTarget(field, operation.TargetValue, out var normalized, out var targetRaw, out var error))
            {
                throw new TransactionValidationException($"目标值校验失败：{operation.Summary}（{error}）");
            }

            if (!string.Equals(normalized, operation.TargetValue, StringComparison.Ordinal) ||
                !string.Equals(targetRaw, operation.TargetRaw, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"草稿目标值与当前字段规则不一致：{operation.Summary}");
            }

            var location = field.Location!;
            var snapshot = NdfSnapshot(location.RelativeSourceFile);
            AddReplacement(
                location.RelativeSourceFile,
                new TextReplacement(
                    location.CharacterOffset,
                    location.CharacterLength,
                    operation.BaselineRaw,
                    operation.TargetRaw,
                    operation.Summary),
                operation.Summary);
        }

        foreach (var operation in operations.Where(item => item.TargetKind == DraftTargetKind.OptionalUnitModule))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var unit = units[operation.ObjectName];
            var field = unit.Field(operation.FieldKey)
                ?? throw new TransactionValidationException($"字段不存在：{operation.FieldKey}");
            var error = string.Empty;
            if (!field.Definition.CanInsertWhenMissing || field.Availability != UnitFieldAvailability.Missing ||
                !UnitValueConverter.TryFormatTarget(field, operation.TargetValue, out var normalized, out var targetRaw, out error) ||
                !string.Equals(normalized, operation.TargetValue, StringComparison.Ordinal) ||
                !string.Equals(targetRaw, operation.TargetRaw, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"补建字段校验失败：{operation.Summary}（{error}）");
            }

            var snapshot = NdfSnapshot(unit.Source.RelativeSourceFile);
            var document = new NdfSyntaxDocument(snapshot.Text, unit.Source.CharacterOffset, unit.Source.CharacterLength);
            if (operation.FieldKey == "structure.specialties")
            {
                var modules = document.FindConstructors("TUnitUIModuleDescriptor");
                if (modules.Count != 1 || document.FindDirectAssignments(modules[0], "SpecialtiesList").Count != 0)
                    throw new TransactionValidationException("单位特性插入锚点不唯一");
                var offset = document.StartOffset(new NdfValueSpan(modules[0].CloseTokenIndex, modules[0].CloseTokenIndex));
                AddReplacement(snapshot.RelativePath, new TextReplacement(offset, 0, "", "SpecialtiesList = " + operation.TargetRaw + snapshot.NewLine, operation.Summary), operation.Summary);
                continue;
            }
            var existing = document.FindConstructors("TDeploymentShiftModuleDescriptor");
            var upkeep = document.FindConstructors("TUnitUpkeepModuleDescriptor");
            var visual = document.FindConstructors("TVisualShootPositionsModuleDescriptor");
            if (existing.Count != 0 || upkeep.Count != 1 || visual.Count != 1)
            {
                throw new TransactionValidationException($"{unit.DisplayName} 的前置部署插入锚点不唯一");
            }

            var upkeepOffset = document.StartOffset(new NdfValueSpan(upkeep[0].TypeTokenIndex, upkeep[0].CloseTokenIndex));
            var visualOffset = document.StartOffset(new NdfValueSpan(visual[0].TypeTokenIndex, visual[0].CloseTokenIndex));
            if (upkeepOffset >= visualOffset)
            {
                throw new TransactionValidationException($"{unit.DisplayName} 的模块顺序不支持安全插入前置部署");
            }

            var lineStart = snapshot.Text.LastIndexOf('\n', Math.Max(0, visualOffset - 1));
            lineStart = lineStart < 0 ? 0 : lineStart + 1;
            var indentation = snapshot.Text[lineStart..visualOffset];
            if (indentation.Any(character => character is not (' ' or '\t')))
            {
                throw new TransactionValidationException($"{unit.DisplayName} 的前置部署插入缩进无法识别");
            }

            var insertion = $"TDeploymentShiftModuleDescriptor( DeploymentShiftGRU = {operation.TargetRaw} ),{snapshot.NewLine}{indentation}";
            AddReplacement(
                unit.Source.RelativeSourceFile,
                new TextReplacement(visualOffset, 0, string.Empty, insertion, operation.Summary),
                operation.Summary);
        }

        var nameOperations = operations.Where(item => item.TargetKind == DraftTargetKind.UnitName).ToArray();
        ValidateAndPlanNameTokens(nameOperations, workspace, units, NdfSnapshot, AddReplacement);
        ValidateUpgradeReferences(operations, workspace);

        var weaponPlan = weaponWorkspace is null
            ? new WeaponApplyPlan([], new Dictionary<string, IReadOnlyList<string>>(), [])
            : new WeaponApplyPlanner().Plan(root, workspace, weaponWorkspace, operations, NdfSnapshot);
        foreach (var item in weaponPlan.Replacements)
        {
            AddReplacement(item.RelativePath, item.Replacement, item.Summary);
        }

        var divisionPlanner = new DivisionApplyPlanner();
        var divisionPlan = divisionPlanner.Plan(root, divisionWorkspace, operations, NdfSnapshot);
        foreach (var item in divisionPlan.Replacements)
        {
            AddReplacement(item.RelativePath, item.Replacement, item.Summary);
        }

        var plannedFiles = new List<PlannedFileChange>();
        foreach (var pair in replacements.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
        {
            var snapshot = snapshots[pair.Key];
            var candidate = SemicolonCsvDocument.ApplyReplacements(snapshot.Text, pair.Value);
            var expectedNewObjects = (weaponPlan.NewObjectsByFile.GetValueOrDefault(pair.Key) ?? [])
                .Concat(divisionPlan.NewObjectsByFile.GetValueOrDefault(pair.Key) ?? [])
                .ToArray();
            ValidateNdfCandidate(snapshot, candidate, operations.Where(item =>
                item.TargetKind is DraftTargetKind.NdfField or DraftTargetKind.OptionalUnitModule &&
                string.Equals(Normalize(item.RelativeSourceFile), pair.Key, StringComparison.OrdinalIgnoreCase)).ToArray(),
                expectedNewObjects);
            plannedFiles.Add(ToWriteChange(snapshot, candidate, summaries[pair.Key]));
        }

        foreach (var csvGroup in nameOperations.GroupBy(item => Normalize(item.RelativeSourceFile), StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = TextFileSnapshot.Load(root, csvGroup.Key, FormalTextFileKind.Csv, allowMissing: true);
            var candidate = BuildCsvCandidate(snapshot, csvGroup.ToArray());
            plannedFiles.Add(ToWriteChange(snapshot, candidate, csvGroup.Select(item => item.Summary).ToArray()));
        }

        if (strategic is not null)
        {
            var strategicChanges = new StrategicPlanner().Plan(strategic, operations, plannedFiles);
            foreach (var change in strategicChanges)
            {
                plannedFiles.RemoveAll(f => f.RelativePath.Equals(change.RelativePath, StringComparison.OrdinalIgnoreCase));
                plannedFiles.Add(change);
            }
        }
        workspace.Rules?.Plan(operations, plannedFiles);
        divisionPlanner.ValidateCandidates(divisionWorkspace, divisionPlan, plannedFiles);
        if(operations.Any(o=>o.TargetKind==DraftTargetKind.UnitCreate))UnitCreation.Plan(root,workspace,weaponWorkspace!,divisionWorkspace,index,operations,plannedFiles);
        if(weaponWorkspace is not null)AmmoNames.Plan(root,workspace,weaponWorkspace,operations,plannedFiles);
        var backupId = CreateBackupId("apply");
        var preparedUtc = DateTimeOffset.UtcNow;
        if (weaponWorkspace is not null)
        {
            ValidateP4ReferenceClosure(index, plannedFiles, weaponWorkspace, weaponPlan);
        }
        var validation = new List<string>
        {
            $"已重新索引并确认 {operations.Count} 项草稿基线",
            $"已构建并验证 {plannedFiles.Count} 个正式文件候选",
            "NDF 对象签名、字段目标、引用和 NameToken 唯一性通过",
            "战术默认 Deck 不含战略 PackIndex；引用与容量已按 P5 规则校验"
        };
        validation.AddRange(weaponPlan.ValidationMessages);
        validation.AddRange(divisionPlan.ValidationMessages);
        if (strategic is not null) validation.Add("战略编组、单位/运输、共享隔离及 PackIndex 候选已校验");
        var logRelativePath = Normalize(Path.Combine("logs", $"WARNO Lite Modding Tool-{backupId}.md"));
        var logSnapshot = TextFileSnapshot.Load(root, logRelativePath, FormalTextFileKind.Log, allowMissing: true);
        if (logSnapshot.Existed)
        {
            throw new TransactionValidationException($"目标日志已存在：{logRelativePath}");
        }

        var logText = BuildApplyLog(backupId, preparedUtc, operations, plannedFiles, validation);
        plannedFiles.Add(ToWriteChange(logSnapshot, logText, ["写入本次应用记录"]));

        return new ApplyPreview(
            root,
            backupId,
            preparedUtc,
            operations.ToArray(),
            plannedFiles,
            validation.ToArray());
    }

    internal static string CreateBackupId(string prefix) =>
        $"{prefix}-{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}-{Guid.NewGuid():N}"[..42];

    internal static PlannedFileChange ToWriteChange(
        TextFileSnapshot snapshot,
        string candidate,
        IReadOnlyList<string> summaries) =>
        new(
            snapshot.RelativePath,
            snapshot.FullPath,
            snapshot.Kind,
            PlannedFileAction.Write,
            snapshot.Existed,
            snapshot.OriginalBytes,
            snapshot.Encode(candidate),
            snapshot.LastWriteUtc,
            summaries);

    private static void ValidateOperationIdentity(IReadOnlyList<DraftOperation> operations)
    {
        foreach (var operation in operations)
        {
            var expectedModule = operation.TargetKind switch
            {
                DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo => "weapons",
                DraftTargetKind.AmmoField or DraftTargetKind.AmmoName => "ammo",
                DraftTargetKind.DivisionPlan => "divisions",
                DraftTargetKind.StrategicPlan => "strategic",
                DraftTargetKind.GlobalRule => "rules",
                _ => "units"
            };
            if (!string.Equals(operation.Module, expectedModule, StringComparison.Ordinal) ||
                !string.Equals(
                    operation.Id,
                    DraftOperation.CreateId(operation.TargetKind, operation.RelativeSourceFile, operation.ObjectName, operation.FieldKey),
                    StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"草稿身份无效：{operation.Summary}");
            }
        }
    }

    private static void ValidateArmorFamilies(UnitWorkspaceData workspace, IReadOnlyList<DraftOperation> operations)
    {
        var active = operations
            .Where(item => item.TargetKind == DraftTargetKind.NdfField && item.Module == "units")
            .ToDictionary(item => (item.ObjectName, item.FieldKey), item => item, EqualityComparer<(string, string)>.Default);
        foreach (var unit in workspace.Units)
        {
            foreach (var side in new[] { "front", "side", "rear", "top" })
            {
                var familyKey = $"armor.{side}.family";
                var indexKey = $"armor.{side}";
                var familyRaw = active.GetValueOrDefault((unit.Name, familyKey))?.TargetRaw ?? unit.Field(familyKey)?.RawValue ?? string.Empty;
                var familyName = NdfSyntaxDocument.Leaf(familyRaw);
                var index = active.GetValueOrDefault((unit.Name, indexKey))?.TargetValue ?? unit.Field(indexKey)?.DisplayValue;
                if (workspace.DamageResistance.ResistanceFamilies.FirstOrDefault(item => item.Name == familyName) is { } family &&
                    (!int.TryParse(index, out var parsed) || parsed < family.MinimumIndex || parsed > family.MaximumIndex))
                {
                    throw new TransactionValidationException($"{unit.DisplayName} 的{side}护甲索引超出 {familyName} 的 {family.MinimumIndex}–{family.MaximumIndex} 范围");
                }

                if (!string.Equals(familyName, "ResistanceFamily_infanterie", StringComparison.Ordinal))
                {
                    continue;
                }

                if (!string.Equals(index, "1", StringComparison.Ordinal))
                {
                    throw new TransactionValidationException($"{unit.DisplayName} 的{side}步兵护甲族装甲索引必须为 1");
                }
            }
        }
    }

    private static void ValidateDamageFamilies(
        UnitWorkspaceData units,
        WeaponWorkspaceData weapons,
        IReadOnlyList<DraftOperation> operations)
    {
        var relevant = operations.Where(item => item.TargetKind == DraftTargetKind.AmmoField).ToArray();
        foreach (var ammo in weapons.Ammunition)
        {
            var familyRaw = relevant.FirstOrDefault(item => item.ObjectName == ammo.Name && item.FieldKey == "ammo.damage.family")?.TargetRaw
                            ?? ammo.Field("ammo.damage.family")?.RawValue;
            var indexValue = relevant.FirstOrDefault(item => item.ObjectName == ammo.Name && item.FieldKey == "ammo.damage.index")?.TargetValue
                             ?? ammo.Field("ammo.damage.index")?.DisplayValue;
            if (familyRaw is null || indexValue is null)
            {
                continue;
            }

            var familyName = NdfSyntaxDocument.Leaf(familyRaw);
            var family = units.DamageResistance.DamageFamilies.FirstOrDefault(item => item.Name == familyName);
            if (family is not null && (!int.TryParse(indexValue, out var index) || index < family.MinimumIndex || index > family.MaximumIndex))
            {
                throw new TransactionValidationException($"{ammo.Name} 的伤害索引超出 {familyName} 的 {family.MinimumIndex}–{family.MaximumIndex} 范围");
            }
        }
    }

    private void ValidateAndPlanNameTokens(
        IReadOnlyList<DraftOperation> operations,
        UnitWorkspaceData workspace,
        IReadOnlyDictionary<string, UnitRecord> units,
        Func<string, TextFileSnapshot> getSnapshot,
        Action<string, TextReplacement, string> addReplacement)
    {
        if (operations.Count == 0)
        {
            return;
        }

        var desiredByUnit = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var operation in operations)
        {
            var unit = units[operation.ObjectName];
            var baselineToken = operation.BaselineNameToken
                ?? throw new TransactionValidationException($"名称草稿缺少 NameToken 基线：{operation.Summary}");
            var targetToken = operation.NameToken
                ?? throw new TransactionValidationException($"名称草稿缺少目标 NameToken：{operation.Summary}");
            if (!string.Equals(unit.NameToken, baselineToken, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"NameToken 基线已变化：{operation.Summary}");
            }

            WarnoLiteModdingTool.Core.Localisation.VanillaNames.RequireAvailable();
            if (operation.RequiresNameTokenChange && WarnoLiteModdingTool.Core.Localisation.VanillaNames.Lookup("UNITS", targetToken) is not null)
                throw new TransactionValidationException("名称token已占用");
            var mustReplace = unit.NameTokenRequiresReplacement;
            if ((mustReplace && !operation.RequiresNameTokenChange) || targetToken.Length != 10 ||
                (operation.RequiresNameTokenChange == string.Equals(targetToken, baselineToken, StringComparison.Ordinal)))
            {
                throw new TransactionValidationException($"NameToken 变更规则无效：{operation.Summary}");
            }

            desiredByUnit[unit.Name] = targetToken;
            if (!operation.RequiresNameTokenChange)
            {
                continue;
            }

            var snapshot = getSnapshot(unit.Source.RelativeSourceFile);
            var document = new NdfSyntaxDocument(snapshot.Text, unit.Source.CharacterOffset, unit.Source.CharacterLength);
            var match = _fieldLocator.Locate(document, new NdfFieldSelector("TUnitUIModuleDescriptor", "NameToken"));
            if (match.Values.Count != 1)
            {
                throw new TransactionValidationException($"NameToken 字段不唯一：{unit.Name}");
            }

            var span = match.Values[0];
            var raw = document.Raw(span);
            if (raw.Length < 2 || raw[0] is not ('\'' or '"') || raw[^1] != raw[0] ||
                !string.Equals(NdfSyntaxDocument.Unquote(raw), baselineToken, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"NameToken 不是可安全替换的字符串：{unit.Name}");
            }

            var targetRaw = $"{raw[0]}{targetToken}{raw[0]}";
            addReplacement(
                unit.Source.RelativeSourceFile,
                new TextReplacement(
                    document.StartOffset(span),
                    document.Length(span),
                    raw,
                    targetRaw,
                    $"{unit.DisplayName} · NameToken：{baselineToken} → {targetToken}"),
                $"{unit.DisplayName} · NameToken：{baselineToken} → {targetToken}");
        }

        var finalTokens = workspace.Units
            .Select(unit => desiredByUnit.TryGetValue(unit.Name, out var target) ? target : unit.NameToken)
            .Where(token => !string.IsNullOrWhiteSpace(token))
            .GroupBy(token => token!, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        foreach (var targetToken in desiredByUnit.Values)
        {
            if (finalTokens.GetValueOrDefault(targetToken) != 1)
            {
                throw new TransactionValidationException($"目标 NameToken 不唯一：{targetToken}");
            }
        }
    }

    private static void ValidateUpgradeReferences(
        IReadOnlyList<DraftOperation> operations,
        UnitWorkspaceData workspace)
    {
        var upgradeOperations = operations.Where(item => item.FieldKey == "structure.upgradeFrom").ToArray();
        if (upgradeOperations.Length == 0)
        {
            return;
        }

        var names = workspace.Units.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var graph = workspace.Units.ToDictionary(
            unit => unit.Name,
            unit => NdfSyntaxDocument.Leaf(unit.Field("structure.upgradeFrom")?.RawValue ?? string.Empty),
            StringComparer.Ordinal);
        foreach (var operation in upgradeOperations)
        {
            var target = NdfSyntaxDocument.Leaf(operation.TargetRaw);
            if (!names.Contains(target))
            {
                throw new TransactionValidationException($"UpgradeFromUnit 目标不存在：{target}");
            }

            graph[operation.ObjectName] = target;
        }

        foreach (var operation in upgradeOperations)
        {
            var visited = new HashSet<string>(StringComparer.Ordinal);
            var current = operation.ObjectName;
            while (!string.IsNullOrWhiteSpace(current) && graph.TryGetValue(current, out var next))
            {
                if (!visited.Add(current))
                {
                    throw new TransactionValidationException($"UpgradeFromUnit 会形成循环：{operation.ObjectName}");
                }

                current = next;
            }
        }
    }

    private void ValidateNdfCandidate(
        TextFileSnapshot snapshot,
        string candidate,
        IReadOnlyList<DraftOperation> fieldOperations,
        IReadOnlyList<string> expectedNewObjects)
    {
        var module = Path.GetFileName(snapshot.FullPath) switch
        {
            "WeaponDescriptor.ndf" => "weapons",
            "Ammunition.ndf" or "AmmunitionMissiles.ndf" => "ammo",
            _ => "units"
        };
        var originalScan = _scanner.Scan(snapshot.Text, snapshot.FullPath, module, snapshot.ProjectRoot);
        var candidateScan = _scanner.Scan(candidate, snapshot.FullPath, module, snapshot.ProjectRoot);
        if (candidateScan.Diagnostics.Any(item => item.Severity == NdfDiagnosticSeverity.Error))
        {
            throw new TransactionValidationException($"候选 NDF 结构校验失败：{snapshot.RelativePath}");
        }

        var originalSignature = originalScan.Objects.Select(item => (item.Name, item.TypeName)).ToArray();
        var candidateSignature = candidateScan.Objects.Select(item => (item.Name, item.TypeName)).ToArray();
        if (!originalSignature.All(candidateSignature.Contains) ||
            candidateSignature.Length != originalSignature.Length + expectedNewObjects.Count ||
            expectedNewObjects.Any(name => candidateSignature.Count(item => item.Name == name) != 1))
        {
            throw new TransactionValidationException($"候选 NDF 顶层对象签名发生变化：{snapshot.RelativePath}");
        }

        if (fieldOperations.Count == 0)
        {
            return;
        }

        var candidateUnits = new UnitCatalogBuilder().Build(
            candidateScan.Objects,
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [snapshot.FullPath] = candidate });
        foreach (var operation in fieldOperations)
        {
            var field = candidateUnits.Single(unit => unit.Name == operation.ObjectName).Field(operation.FieldKey);
            if (field is null || !string.Equals(field.RawValue, operation.TargetRaw, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"候选字段值校验失败：{operation.Summary}");
            }
        }
    }

    private void ValidateP4ReferenceClosure(
        ProjectIndexResult index,
        IReadOnlyList<PlannedFileChange> files,
        WeaponWorkspaceData workspace,
        WeaponApplyPlan plan)
    {
        if (plan.ValidationMessages.Count == 0)
        {
            return;
        }

        var weaponNames = workspace.Weapons.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        var ammoNames = workspace.Ammunition.Select(item => item.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var names in plan.NewObjectsByFile)
        {
            if (Path.GetFileName(names.Key).Equals("WeaponDescriptor.ndf", StringComparison.OrdinalIgnoreCase))
            {
                weaponNames.UnionWith(names.Value);
            }
            else
            {
                ammoNames.UnionWith(names.Value);
            }
        }

        foreach (var file in files.Where(item => item.Kind == FormalTextFileKind.Ndf))
        {
            var candidate = new UTF8Encoding(false, true).GetString(file.CandidateBytes);
            var module = Path.GetFileName(file.RelativePath).Equals("WeaponDescriptor.ndf", StringComparison.OrdinalIgnoreCase) ? "weapons" : "units";
            var scan = _scanner.Scan(candidate, file.FullPath, module, Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(file.FullPath))) ?? string.Empty);
            foreach (var descriptor in scan.Objects)
            {
                var document = new NdfSyntaxDocument(candidate, descriptor.CharacterOffset, descriptor.CharacterLength);
                if (descriptor.ModuleKey == "units")
                {
                    var missing = document.FindReferenceLeaves("WeaponDescriptor_").Where(name => !weaponNames.Contains(name)).ToArray();
                    if (missing.Length > 0)
                    {
                        throw new TransactionValidationException($"候选 Unit 存在悬空 Weapon 引用：{string.Join(", ", missing.Take(3))}");
                    }
                }
                else if (descriptor.ModuleKey == "weapons")
                {
                    var missing = document.FindReferenceLeaves("Ammo_").Where(name => !ammoNames.Contains(name)).ToArray();
                    if (missing.Length > 0)
                    {
                        throw new TransactionValidationException($"候选 Weapon 存在悬空 Ammo 引用：{string.Join(", ", missing.Take(3))}");
                    }
                }
            }
        }
    }

    private static string BuildCsvCandidate(TextFileSnapshot snapshot, IReadOnlyList<DraftOperation> operations)
    {
        var source = snapshot.Text;
        if (source.Length == 0)
        {
            source = "\"TOKEN\";\"REFTEXT\"";
        }

        var document = SemicolonCsvDocument.Parse(source);
        var header = document.Rows.FirstOrDefault();
        if (header is null || header.Fields.Count < 2 ||
            !string.Equals(header.Fields[0].Value.Trim(), "TOKEN", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(header.Fields[1].Value.Trim(), "REFTEXT", StringComparison.OrdinalIgnoreCase))
        {
            throw new TransactionValidationException($"UNITS.csv 表头不是 TOKEN;REFTEXT：{snapshot.RelativePath}");
        }

        var replacements = new List<TextReplacement>();
        var appended = new List<string>();
        var usedRows = new HashSet<int>();
        foreach (var operation in operations)
        {
            var baselineToken = operation.BaselineNameToken!;
            var targetToken = operation.NameToken!;
            var baselineRows = document.Rows.Skip(1)
                .Where(row => row.Fields.Count >= 2 && string.Equals(row.Fields[0].Value.Trim(), baselineToken, StringComparison.Ordinal))
                .ToArray();
            if (baselineRows.Length > 1)
            {
                throw new TransactionValidationException($"UNITS.csv token 行不唯一：{baselineToken}");
            }

            var targetRows = document.Rows.Skip(1)
                .Where(row => row.Fields.Count >= 1 && string.Equals(row.Fields[0].Value.Trim(), targetToken, StringComparison.Ordinal))
                .ToArray();
            if (!string.Equals(targetToken, baselineToken, StringComparison.Ordinal) && targetRows.Length > 0)
            {
                throw new TransactionValidationException($"目标 token 已存在于 UNITS.csv：{targetToken}");
            }

            if (baselineRows.Length == 0 || operation.RequiresNameTokenChange)
            {
                appended.Add($"{SemicolonCsvDocument.Quote(targetToken)};{SemicolonCsvDocument.Quote(operation.TargetValue)}");
                continue;
            }

            var row = baselineRows[0];
            if (!usedRows.Add(row.Offset))
            {
                throw new TransactionValidationException($"多个名称操作命中同一 CSV 行：{baselineToken}");
            }

            if (!string.Equals(targetToken, baselineToken, StringComparison.Ordinal))
            {
                var tokenField = row.Fields[0];
                replacements.Add(new TextReplacement(
                    tokenField.Offset,
                    tokenField.Length,
                    tokenField.Raw,
                    SemicolonCsvDocument.Quote(targetToken),
                    $"UNITS.csv token {baselineToken}"));
            }

            var nameField = row.Fields[1];
            replacements.Add(new TextReplacement(
                nameField.Offset,
                nameField.Length,
                nameField.Raw,
                SemicolonCsvDocument.Quote(operation.TargetValue),
                $"UNITS.csv 名称 {targetToken}"));
        }

        var candidate = SemicolonCsvDocument.ApplyReplacements(source, replacements);
        if (appended.Count > 0)
        {
            var endsWithNewLine = candidate.EndsWith('\n') || candidate.EndsWith('\r');
            if (!endsWithNewLine)
            {
                candidate += snapshot.NewLine;
            }

            candidate += string.Join(snapshot.NewLine, appended);
            if (endsWithNewLine)
            {
                candidate += snapshot.NewLine;
            }
        }

        var verified = SemicolonCsvDocument.Parse(candidate);
        foreach (var operation in operations)
        {
            var rows = verified.Rows.Skip(1).Where(row =>
                row.Fields.Count >= 2 && string.Equals(row.Fields[0].Value.Trim(), operation.NameToken, StringComparison.Ordinal)).ToArray();
            if (rows.Length != 1 || !string.Equals(rows[0].Fields[1].Value, operation.TargetValue, StringComparison.Ordinal))
            {
                throw new TransactionValidationException($"UNITS.csv 候选校验失败：{operation.Summary}");
            }
        }

        return candidate;
    }

    private static string BuildApplyLog(
        string backupId,
        DateTimeOffset preparedUtc,
        IReadOnlyList<DraftOperation> operations,
        IReadOnlyList<PlannedFileChange> files,
        IReadOnlyList<string> validation)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# WARNO Lite Modding Tool 应用记录");
        builder.AppendLine();
        builder.AppendLine($"- 应用时间：{preparedUtc:O}");
        builder.AppendLine($"- 备份编号：`{backupId}`");
        var modules = operations.Select(item => item.Module).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        builder.AppendLine($"- 模块：{string.Join(" / ", modules)}");
        builder.AppendLine($"- 操作数量：{operations.Count}");
        builder.AppendLine();
        builder.AppendLine("## 修改");
        builder.AppendLine();
        foreach (var operation in operations)
        {
            var scope = operation.EditScope switch
            {
                DraftEditScope.SelectedUnits => $"所选 Unit（{operation.SelectedUnitNames?.Count ?? 0}）",
                DraftEditScope.AllReferences => "全部引用",
                DraftEditScope.CurrentUnit => "仅当前 Unit",
                _ => "当前 Unit"
            };
            var isolation = operation.TargetKind switch
            {
                DraftTargetKind.AmmoField when operation.EditScope != DraftEditScope.AllReferences => "自动克隆最小 Ammo→Weapon→Unit 链",
                DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo when operation.EditScope != DraftEditScope.AllReferences => "必要时克隆 Weapon 并重定向 Unit",
                DraftTargetKind.AmmoField or DraftTargetKind.WeaponField or DraftTargetKind.MountedWeaponAmmo => "直接修改共享对象",
                _ => "不涉及 Weapon/Ammo 克隆"
            };
            builder.AppendLine($"- `{operation.ObjectName}` · `{operation.FieldPath}`：`{Inline(operation.BaselineValue)}` → `{Inline(operation.TargetValue)}`；文件 `{Normalize(operation.RelativeSourceFile)}`；影响范围：{scope}；共享/隔离：{isolation}。");
        }

        builder.AppendLine();
        builder.AppendLine("## 文件");
        builder.AppendLine();
        foreach (var file in files)
        {
            builder.AppendLine($"- `{file.RelativePath}`");
        }

        builder.AppendLine();
        builder.AppendLine("## 校验");
        builder.AppendLine();
        foreach (var message in validation)
        {
            builder.AppendLine($"- {message}");
        }

        return builder.ToString();
    }

    private static string Inline(string value) => value.Replace("`", "'", StringComparison.Ordinal).Replace("\r", " ").Replace("\n", " ");

    private static string Normalize(string path) => path.Replace('\\', '/');
}
