using System.Collections.ObjectModel;
using System.ComponentModel;

namespace WarnoLiteModdingTool.App.ViewModels;

// Retain edited fields after selection changes until their saves are confirmed.
internal sealed class PendingFieldEdits<T> where T : ObservableObject
{
    private readonly List<T> _edited = [];
    private readonly Func<T, Task> _flush;
    private readonly Func<T, bool> _dirty;

    public PendingFieldEdits(ObservableCollection<T> fields, Func<T, Task> flush, Func<T, bool> dirty)
    {
        _flush = flush;
        _dirty = dirty;
        fields.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is null) return;
            foreach (T field in e.NewItems)
            {
                field.PropertyChanged -= Changed;
                field.PropertyChanged += Changed;
            }
        };
    }

    public T Restore(T candidate, Func<T, bool> matches) => _edited.LastOrDefault(field => _dirty(field) && matches(field)) ?? candidate;

    private void Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != "EditValue" || sender is not T field) return;
        _edited.RemoveAll(item => !_dirty(item) || ReferenceEquals(item, field));
        if (_dirty(field)) _edited.Add(field);
    }

    public async Task FlushAsync()
    {
        foreach (var field in _edited.ToArray())
        {
            await _flush(field);
            if (!_dirty(field)) _edited.Remove(field);
        }
    }
}
