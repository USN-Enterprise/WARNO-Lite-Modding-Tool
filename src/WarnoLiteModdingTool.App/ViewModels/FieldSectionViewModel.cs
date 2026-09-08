namespace WarnoLiteModdingTool.App.ViewModels;

public sealed class FieldSectionViewModel<T> : ObservableObject
{
    private bool _isExpanded;

    public FieldSectionViewModel(
        string title,
        IReadOnlyList<FieldGroupViewModel<T>> groups,
        bool isExpanded)
    {
        Title = title;
        Groups = groups;
        _isExpanded = isExpanded;
    }

    public string Title { get; }

    public IReadOnlyList<FieldGroupViewModel<T>> Groups { get; }

    public int FieldCount => Groups.Sum(group => group.Fields.Count);

    public bool IsExpanded
    {
        get => _isExpanded;
        set => SetProperty(ref _isExpanded, value);
    }
}

public sealed record FieldGroupViewModel<T>(string Title, IReadOnlyList<T> Fields);

public static class FieldSectionBuilder
{
    public static IReadOnlyList<FieldSectionViewModel<T>> Build<T>(
        IEnumerable<T> fields,
        Func<T, string> sectionSelector,
        Func<T, string> groupSelector)
    {
        var ordered = fields.ToArray();
        var sectionOrder = ordered.Select(sectionSelector).Distinct(StringComparer.Ordinal).ToArray();
        var result = new List<FieldSectionViewModel<T>>(sectionOrder.Length);
        for (var sectionIndex = 0; sectionIndex < sectionOrder.Length; sectionIndex++)
        {
            var section = sectionOrder[sectionIndex];
            var sectionFields = ordered.Where(field => sectionSelector(field) == section).ToArray();
            var groups = sectionFields
                .Select(groupSelector)
                .Distinct(StringComparer.Ordinal)
                .Select(group => new FieldGroupViewModel<T>(
                    group,
                    sectionFields.Where(field => groupSelector(field) == group).ToArray()))
                .ToArray();
            result.Add(new FieldSectionViewModel<T>(section, groups, sectionIndex == 0));
        }

        return result;
    }
}
