using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WarnoLiteModdingTool.Core.Changes;

public enum NumericPolicy { Delta, Ratio }
public enum FilePresence { Missing, Present, Unknown }
public sealed record ContentStamp(FilePresence Presence, long Length = 0, string? Sha256 = null)
{
    public static ContentStamp Of(byte[]? bytes) => bytes is null ? new(FilePresence.Missing) : new(FilePresence.Present, bytes.LongLength, Convert.ToHexString(SHA256.HashData(bytes)));
}
public sealed record ChangeFile(string Path, ContentStamp Before, ContentStamp After, string? BeforePayload, string? AfterPayload)
{
    [JsonIgnore] public string Kind => Before.Presence == FilePresence.Unknown ? "基础未知" : Before.Presence == FilePresence.Missing ? "新增" : After.Presence == FilePresence.Missing ? "删除" : "修改";
}
public sealed record InventoryFile(string Path, ContentStamp Before, ContentStamp After);
public sealed record ChangeManifest(string Format, int Version, string Id, DateTimeOffset CreatedUtc, NumericPolicy NumericPolicy,
    string SourceName, string BaselineName, bool Complete, IReadOnlyList<string> Issues, IReadOnlyList<string> Excluded,
    IReadOnlyList<InventoryFile> Inventory, IReadOnlyList<ChangeFile> Files);
public sealed class ChangePackage(ChangeManifest manifest, IReadOnlyDictionary<string, byte[]> payloads)
{
    public ChangeManifest Manifest { get; } = manifest;
    public IReadOnlyDictionary<string, byte[]> Payloads { get; } = payloads;
    public byte[]? Before(ChangeFile file) => file.BeforePayload is null ? null : Payloads[file.BeforePayload];
    public byte[]? After(ChangeFile file) => file.AfterPayload is null ? null : Payloads[file.AfterPayload];
    public ChangePackage WithPolicy(NumericPolicy policy) => new(Manifest with { NumericPolicy = policy }, Payloads);
    public string Identity => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(Manifest, ChangeJson.Options)));
}
public static class ChangeJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase, WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}
public sealed record ChangeProgress(string Phase, int Completed, int Total, string Path);
public enum TextConflictChoice { KeepTarget, UseRecorded }
public enum ChangeConflictKind { DictionaryText, NdfScalar }
// Token/Column also hold the declaration name/field key for NDF scalar evidence.
public sealed record ChangeTextConflict(string Path, string Token, string Column, string Before, string After, string Target)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ChangeConflictKind Kind { get; init; }
}
public sealed record ChangeTextDecision(ChangeTextConflict Conflict, TextConflictChoice Choice);
public sealed record ChangeDetail(string Object, string Field, string Before, string After, string Target, string Result, string Status)
{
    public ChangeTextConflict? Conflict { get; init; }
}
public sealed record RestoreFile(string Path, byte[]? Before, byte[]? After, string Status, IReadOnlyList<ChangeDetail> Details, string? Error = null)
{
    public string? SourcePath { get; init; }
    public string RecordPath => SourcePath ?? Path;
}
public sealed record ChangeFileMapping(string SourcePath, string TargetPath, IReadOnlyDictionary<string, string> Objects);
public sealed record ChangeObjectTarget(string Path, string Name, string Type);
public sealed record ChangeRestoreOptions(IReadOnlyList<string>? SelectedPaths = null, IReadOnlyList<ChangeFileMapping>? Mappings = null,
    IReadOnlyList<ChangeTextDecision>? Decisions = null);
public sealed record ChangeRestoreGroup(string Id, IReadOnlyList<string> Paths, bool Selected, bool Applied, string Reason);
public sealed record ChangePreview(string Root, string PackageIdentity, NumericPolicy Policy, IReadOnlyList<RestoreFile> Files,
    IReadOnlyList<string> Errors, IReadOnlyDictionary<string, ContentStamp> Dependencies, bool AlreadyApplied = false)
{
    public IReadOnlyList<string> Impacts { get; init; } = [];
    public IReadOnlyList<RestoreFile> AllFiles { get; init; } = Files;
    public IReadOnlyList<ChangeRestoreGroup> Groups { get; init; } = [];
    public IReadOnlyList<string> SelectedPaths { get; init; } = [];
    public IReadOnlyList<string> DeferredPaths { get; init; } = [];
    public IReadOnlyList<string> AppliedPaths { get; init; } = [];
    public IReadOnlyList<ChangeFileMapping> Mappings { get; init; } = [];
    public IReadOnlyList<ChangeTextDecision> Decisions { get; init; } = [];
    public bool AllProcessed { get; init; }
    public int RetainedChanges => Decisions.Count(d => d.Choice == TextConflictChoice.KeepTarget && AppliedPaths.Contains(d.Conflict.Path, StringComparer.OrdinalIgnoreCase));
    public IReadOnlyDictionary<string, ContentStamp> TargetBaseline { get; init; } = Dependencies;
    public string ReceiptState { get; init; } = "";
    public bool CanApply => !AlreadyApplied && Errors.Count == 0 && (SelectedPaths.Count > 0 || Files.Any(f => !ChangePaths.Equal(f.Before, f.After)));
}
