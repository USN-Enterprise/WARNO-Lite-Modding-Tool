using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;

namespace WarnoLiteModdingTool.Core.Weapons;

public enum WeaponFieldOwner
{
    Weapon,
    MountedWeapon,
    Turret,
    Ammo
}

public enum WeaponValueKind
{
    Integer,
    Decimal,
    Boolean,
    Reference,
    Degrees,
    Choice,
    Text,
    CatalogChoice,
    Tags
}

public sealed record WeaponFieldDefinition(
    string Key,
    string Group,
    string Label,
    string Hint,
    WeaponFieldOwner Owner,
    string FieldName,
    WeaponValueKind ValueKind,
    bool NonNegative = false,
    string? Suffix = null,
    string? ArgumentName = null,
    string? MapKey = null,
    string Section = "其他",
    bool Professional = false,
    bool CanInsert = false,
    string? Constructor = null);

public enum WeaponFieldState { Declared, Missing, Unavailable }

public sealed record WeaponFieldLocation(
    string RelativeSourceFile,
    string FieldPath,
    int CharacterOffset,
    int CharacterLength,
    int LineNumber);

public sealed record WeaponFieldValue(
    WeaponFieldDefinition Definition,
    string OwnerObjectName,
    string OwnerObjectType,
    string DisplayValue,
    string RawValue,
    WeaponFieldLocation Location,
    IReadOnlyList<string> Choices,
    WeaponFieldState State = WeaponFieldState.Declared,
    string Reason = "",
    string InsertPrefix = "",
    string InsertSuffix = "")
{
    public string Key => Definition.Key;
    public bool CanEdit => State != WeaponFieldState.Unavailable;
    public bool IsMissing => State == WeaponFieldState.Missing;
}

public sealed class AmmoRecord
{
    private readonly Dictionary<string, WeaponFieldValue> _fields;

    public AmmoRecord(NdfObjectInfo source, IReadOnlyList<WeaponFieldValue> fields)
    {
        Source = source;
        Fields = fields;
        _fields = fields.ToDictionary(item => item.Key, StringComparer.Ordinal);
    }

    public NdfObjectInfo Source { get; }
    public string Name => Source.Name;
    public IReadOnlyList<WeaponFieldValue> Fields { get; }
    public WeaponFieldValue? Field(string key) => _fields.GetValueOrDefault(key);
    public IReadOnlyDictionary<string, string> SafetyValues { get; init; } = new Dictionary<string, string>();
    public string NameToken {get;internal set;}="";
    public string DisplayName {get;internal set;}="";
    public string ChineseName {get;internal set;}="";
    public WeaponFieldLocation? NameLocation {get;internal set;}
    public string NameRaw {get;internal set;}="";
    public bool CanEditName {get;internal set;}
    public string SearchText=>Name+" "+DisplayName+" "+ChineseName+" "+NameToken;
    public int? ShotsPerSalvo => ParseInt("ammo.shotsPerSalvo");
    public int? DisplayPerSalvo => ParseInt("ammo.displayPerSalvo");

    private int? ParseInt(string key) =>
        int.TryParse(Field(key)?.DisplayValue, out var value) ? value : null;
}

public sealed record MountedWeaponRecord(
    int Index,
    int TurretIndex,
    string TurretType,
    int? AmmoBoxIndex,
    string AmmoName,
    IReadOnlyList<WeaponFieldValue> Fields,
    string EffectTag,
    string WeaponAlternative,
    string AnimationKeys,
    string PresentationReferences);

public sealed class WeaponRecord
{
    private readonly Dictionary<string, WeaponFieldValue> _fields;

    public WeaponRecord(
        NdfObjectInfo source,
        IReadOnlyList<WeaponFieldValue> fields,
        IReadOnlyList<MountedWeaponRecord> mounts)
    {
        Source = source;
        Fields = fields;
        Mounts = mounts;
        _fields = fields.ToDictionary(item => item.Key, StringComparer.Ordinal);
    }

    public NdfObjectInfo Source { get; }
    public string Name => Source.Name;
    public IReadOnlyList<WeaponFieldValue> Fields { get; }
    public IReadOnlyList<MountedWeaponRecord> Mounts { get; }
    public WeaponFieldValue? Field(string key) => _fields.GetValueOrDefault(key);
}

public sealed record WeaponReferenceIndex(
    IReadOnlyDictionary<string, IReadOnlyList<string>> WeaponUnits,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AmmoWeapons,
    IReadOnlyDictionary<string, IReadOnlyList<string>> AmmoUnits);

public sealed record WeaponWorkspaceData(
    IReadOnlyList<WeaponRecord> Weapons,
    IReadOnlyList<AmmoRecord> Ammunition,
    IReadOnlyList<UnitRecord> Units,
    WeaponReferenceIndex References,
    IReadOnlyList<string> Diagnostics)
{
    public IReadOnlyList<string> CatalogFiles { get; init; } = [];
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<Rules.DamageWorkspace>? DamageSource { get; init; }
    [System.Text.Json.Serialization.JsonIgnore]
    public Func<UnitProjectGraph>? MotionSource { get; init; }
    private readonly object _lookupGate = new();
    private (IReadOnlyList<WeaponRecord> Source, Dictionary<string, WeaponRecord> Index)? _weaponIndex;
    private (IReadOnlyList<AmmoRecord> Source, Dictionary<string, AmmoRecord> Index)? _ammoIndex;
    public WeaponRecord? Weapon(string name)
    {
        lock (_lookupGate)
        {
            if (_weaponIndex is not { } index || !ReferenceEquals(index.Source, Weapons))
                _weaponIndex = (Weapons, Weapons.GroupBy(w => w.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
            return _weaponIndex.Value.Index.GetValueOrDefault(name);
        }
    }
    public AmmoRecord? Ammo(string name)
    {
        lock (_lookupGate)
        {
            if (_ammoIndex is not { } index || !ReferenceEquals(index.Source, Ammunition))
                _ammoIndex = (Ammunition, Ammunition.GroupBy(a => a.Name, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));
            return _ammoIndex.Value.Index.GetValueOrDefault(name);
        }
    }
}
