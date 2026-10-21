namespace WarnoLiteModdingTool.App.ViewModels;

public sealed class FieldSectionViewModel<T> : ObservableObject
{
    private bool _isExpanded;
    private readonly System.Collections.ObjectModel.ObservableCollection<FieldGroupViewModel<T>> _visibleGroups = [];
    private int _publishGeneration;

    public FieldSectionViewModel(
        string title,
        IReadOnlyList<FieldGroupViewModel<T>> groups,
        bool isExpanded)
    {
        Title = title;
        Groups = new(groups);
        _isExpanded = isExpanded;
        PublishGroups();
    }

    public string Title { get; }

    public System.Collections.ObjectModel.ObservableCollection<FieldGroupViewModel<T>> Groups { get; }
    public IReadOnlyList<FieldGroupViewModel<T>> VisibleGroups => _visibleGroups;

    public int FieldCount => Groups.Sum(group => group.Fields.Count);
    public void UpdateGroups(IReadOnlyList<FieldGroupViewModel<T>> groups)
    {
        if (Groups.Select(g => g.Title).SequenceEqual(groups.Select(g => g.Title)))
            for (var i = 0; i < Groups.Count; i++) Groups[i].UpdateFields(groups[i].Fields);
        else { Groups.Clear(); foreach (var group in groups) Groups.Add(group); PublishGroups(); }
        OnPropertyChanged(nameof(FieldCount));
    }

    public bool IsExpanded
    {
        get => _isExpanded;
        set { if (SetProperty(ref _isExpanded, value)) { PublishGroups(); OnPropertyChanged(nameof(VisibleGroups)); } }
    }

    private void PublishGroups()
    {
        var generation = ++_publishGeneration;
        _visibleGroups.Clear();
        if (!IsExpanded) return;
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        { foreach (var group in Groups) _visibleGroups.Add(group); return; }
        var next = 0;
        void PublishNext()
        {
            if (generation != _publishGeneration || !IsExpanded || next >= Groups.Count) return;
            _visibleGroups.Add(Groups[next++]);
            if (next < Groups.Count) dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(PublishNext));
        }
        // Let layout/render and input run between groups rather than materializing
        // every field template in a single opening/selection dispatcher turn.
        dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Background, new Action(PublishNext));
    }
}

public sealed class FieldGroupViewModel<T>(string title, IReadOnlyList<T> fields)
{
    public string Title { get; } = title;
    public System.Collections.ObjectModel.ObservableCollection<T> Fields { get; } = new(fields);
    public void UpdateFields(IReadOnlyList<T> fields)
    {
        for (var i = 0; i < fields.Count; i++)
            if (i >= Fields.Count) Fields.Add(fields[i]);
            else if (!ReferenceEquals(Fields[i], fields[i])) Fields[i] = fields[i];
        while (Fields.Count > fields.Count) Fields.RemoveAt(Fields.Count - 1);
    }
}

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
