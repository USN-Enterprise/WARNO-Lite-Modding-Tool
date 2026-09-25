using System.Text;
using WarnoLiteModdingTool.Core.Ndf;
using WarnoLiteModdingTool.Core.Units;
using static WarnoLiteModdingTool.Core.Projects.ProjectLoadCache;

namespace WarnoLiteModdingTool.Core.Projects;

// Compact, versioned UI cache payload. Repeated field keys/paths/values share a string table.
internal static class UnitCacheCodec
{
    public static void Write(Stream stream, SavedUnit[] units)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        var strings = new Dictionary<string, int>(StringComparer.Ordinal);
        void Text(string? value)
        {
            if (value is null) { writer.Write(-1); return; }
            if (strings.TryGetValue(value, out var id)) writer.Write(id);
            else { id = strings.Count; strings.Add(value, id); writer.Write(id); writer.Write(value); }
        }
        void List(IReadOnlyList<string> values) { writer.Write(values.Count); foreach (var value in values) Text(value); }
        writer.Write(units.Length);
        foreach (var unit in units)
        {
            var s = unit.Source;
            Text(s.ModuleKey); Text(s.Name); Text(s.DisplayName); Text(s.TypeName); Text(s.SourceFile); Text(s.RelativeSourceFile);
            writer.Write(s.CharacterOffset); writer.Write(s.CharacterLength); writer.Write(s.ByteOffset); writer.Write(s.ByteLength); writer.Write(s.LineNumber);
            writer.Write(unit.Fields.Length);
            foreach (var field in unit.Fields)
            {
                Text(field.Key); writer.Write((int)field.Availability); Text(field.DisplayValue); Text(field.RawValue); Text(field.Reason);
                writer.Write(field.Location is not null);
                if (field.Location is { } location)
                { Text(location.RelativeSourceFile); writer.Write(location.CharacterOffset); writer.Write(location.CharacterLength); writer.Write(location.LineNumber); Text(location.FieldPath); }
            }
            writer.Write(unit.HasTransporter); List(unit.Weapons); List(unit.Ammunition); List(unit.Divisions); List(unit.PresentationReferences);
            Text(unit.NameToken); writer.Write(unit.UniqueName);
        }
    }
    public static SavedUnit[] Read(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        var strings = new List<string>();
        string? Text()
        {
            var id = reader.ReadInt32(); if (id == -1) return null;
            if (id < 0 || id > strings.Count) throw new InvalidDataException("Invalid cache string index");
            if (id == strings.Count) strings.Add(reader.ReadString());
            return strings[id];
        }
        string Required() => Text() ?? throw new InvalidDataException("Missing cache string");
        int Count(int max = 1_000_000) { var count = reader.ReadInt32(); return count < 0 || count > max ? throw new InvalidDataException("Invalid cache count") : count; }
        string[] List() { var count = Count(); var list = new string[count]; for (var i = 0; i < count; i++) list[i] = Required(); return list; }
        var units = new SavedUnit[Count()];
        for (var i = 0; i < units.Length; i++)
        {
            var source = new NdfObjectInfo(Required(), Required(), Required(), Required(), Required(), Required(),
                reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt64(), reader.ReadInt32(), reader.ReadInt32());
            var fields = new SavedField[Count(1024)];
            for (var f = 0; f < fields.Length; f++)
            {
                var key = Required(); var availability = (UnitFieldAvailability)reader.ReadInt32();
                if (!Enum.IsDefined(availability)) throw new InvalidDataException("Invalid cache availability");
                var display = Required(); var raw = Required(); var reason = Required();
                UnitSourceLocation? location = reader.ReadBoolean() ? new(Required(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), Required()) : null;
                fields[f] = new(key, availability, display, raw, reason, location);
            }
            units[i] = new(source, fields, reader.ReadBoolean(), List(), List(), List(), List(), Text(), reader.ReadBoolean());
        }
        return units;
    }
}
