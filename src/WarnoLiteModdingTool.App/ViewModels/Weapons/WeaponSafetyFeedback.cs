using System.IO;
using WarnoLiteModdingTool.Core.Drafts;
using WarnoLiteModdingTool.Core.Weapons;

namespace WarnoLiteModdingTool.App.ViewModels.Weapons;

internal sealed class WeaponSafetyFeedback : IDisposable
{
    internal static string MotionText(string value) => string.Join("\n", value.Split('\n').Select(line =>
    {
        var split = line.IndexOf(" / ", StringComparison.Ordinal);
        return split < 0 ? line : Localisation.UiText.Current.English ? line[(split + 3)..] : line[..split];
    }));
    private CancellationTokenSource? _pending;
    public bool Active { get; set; }
    public Task Completion { get; private set; } = Task.CompletedTask;
    public void Dispose() { _pending?.Cancel(); Active = false; }
    public void Refresh(IEnumerable<WeaponFieldViewModel> fields, WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations, string unit = "", string weapon = "", int mount = -1)
    {
        _pending?.Cancel();
        if (!Active) return;
        var rows = fields.ToArray();
        var cancellation = new CancellationTokenSource(); _pending = cancellation;
        foreach (var row in rows) { row.SafetyText = Localisation.UiText.T("正在检查关联…"); row.MissileEntity = null; }
        Completion = RunAsync();
        async Task RunAsync()
        {
            try
            {
                await Task.Delay(60, cancellation.Token);
                var result = await Task.Run(() =>
                {
                    using var scope = new Core.Projects.ProjectReadScope(cancellation: cancellation.Token).Enter();
                    return Calculate(rows.Select(r => new FeedbackRow(r.Field)).ToArray(), data, operations, unit, weapon, mount);
                }, cancellation.Token);
                if (cancellation.IsCancellationRequested || !ReferenceEquals(_pending, cancellation)) return;
                for (var i = 0; i < rows.Length; i++) { rows[i].SafetyText = result[i].SafetyText; rows[i].MissileEntity = result[i].MissileEntity; }
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested && ReferenceEquals(_pending, cancellation))
                    foreach (var row in rows) row.SafetyText = Localisation.UiText.T("关联预检未完成，请核对草稿") + " · " + ex.Message;
            }
            finally { if (ReferenceEquals(_pending, cancellation)) _pending = null; cancellation.Dispose(); }
        }
    }
    private sealed class FeedbackRow(WeaponFieldValue field)
    {
        public WeaponFieldValue Field { get; } = field;
        public string SafetyText { get; set; } = "";
        public Core.Ndf.NdfObjectInfo? MissileEntity { get; set; }
    }
    private static FeedbackRow[] Calculate(FeedbackRow[] rows, WeaponWorkspaceData data, IReadOnlyList<DraftOperation> operations, string unit, string weapon, int mount)
    {
        try
        {
            var ammo = rows.FirstOrDefault(f => f.Field.Definition.Owner == WeaponFieldOwner.Ammo)?.Field.OwnerObjectName;
            if (ammo is null) return rows;
            var issues = WeaponSafety.Preview(data, operations, ammo, unit, weapon, mount);
            foreach (var row in rows)
            {
                Core.Projects.ProjectReadScope.Current?.Cancellation.ThrowIfCancellationRequested();
                var related = issues.Where(i => i.Field.Split(" / ").Contains(row.Field.Definition.FieldName)).ToArray();
                row.SafetyText = string.Join("\n", related.Select(i =>
                {
                    var message = i.Message; var split = message.IndexOf(" / ", StringComparison.Ordinal);
                    if (i.Rule == "supply.info") message = Localisation.UiText.T("每发补给") + message[message.LastIndexOf(':')..];
                    if (split >= 0 && i.Rule != "supply.info") message = Localisation.UiText.Current.English ? message[(split + 3)..] : message[..split];
                    var prefix = i.Level == WeaponSafetyLevel.Error ? Localisation.UiText.T("需配套修改后应用") : i.Rule == "supply.info" ? "" : Localisation.UiText.T("请核对");
                    return (prefix.Length == 0 ? "" : prefix + " · ") + message + (i.Object == ammo ? "" : " · " + i.Object);
                }));
                if (MissileMotion.IsParameter(row.Field.Key))
                {
                    var motion = MissileMotion.Status(data, operations, ammo, row.Field.Key, unit);
                    row.SafetyText = string.Join("\n", new[] { row.SafetyText, MotionText(motion) }.Where(s => s.Length > 0));
                    try { row.MissileEntity = MissileMotion.EntitySource(data, operations, ammo, unit); }
                    catch (Exception e) when (e is InvalidDataException or Core.Transactions.TransactionValidationException or InvalidOperationException) { row.MissileEntity = null; }
                }
                if (row.Field.Key == ApBinding.FamilyKey)
                {
                    var binding = operations.SelectMany(o => o.ApBindings ?? []).FirstOrDefault(b => b.Ammo == ammo && (b.Scope == DraftEditScope.AllReferences || b.Units.Contains(unit)));
                    var reference = data.Ammo(ammo)?.Field(DamageDistance.ReferenceKey);
                    var distanceDraft = operations.FirstOrDefault(o => o.TargetKind == DraftTargetKind.DamageDistance && o.ObjectName == ammo &&
                        (o.EditScope == DraftEditScope.AllReferences || (o.SelectedUnitNames ?? []).Contains(unit)));
                    var state = distanceDraft is null ? binding?.Distance : DamageDistance.Read(distanceDraft);
                    var status = binding is not null
                        ? Localisation.UiText.T("AP联动：距离变化已启用（待应用）") + $" · {state!.Distance} GRU / {state.AP} AP"
                        : reference?.IsMissing == true ? Localisation.UiText.T("距离规则字段未声明")
                        : reference?.RawValue == "nil" ? Localisation.UiText.T("未启用距离变化") : Localisation.UiText.T("距离规则") + " · " + reference?.RawValue;
                    row.SafetyText = string.Join("\n", new[] { row.SafetyText, status, Localisation.UiText.T("保留原始Index；远距离穿深可能改变。") }.Where(s => s.Length > 0));
                }
                if (row.Field.Key == ApBinding.PiercingKey && operations.SelectMany(o => o.ApBindings ?? []).Any(b => b.Ammo == ammo && (b.Scope == DraftEditScope.AllReferences || b.Units.Contains(unit))))
                    row.SafetyText = Localisation.UiText.T("AP联动将设为True（待应用）；当前显示正式基线或独立草稿。");
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
        {
            foreach (var row in rows.Where(r => r.Field.Definition.FieldName is "Ammunition" or "ShotsCountPerSalvo"))
                row.SafetyText = Localisation.UiText.T("关联预检未完成，请核对草稿") + " · " + ex.Message;
        }
        return rows;
    }
}
