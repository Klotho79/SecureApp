using System.Collections.ObjectModel;

namespace SecureApp.Presentation.Infrastructure;

/// <summary>
/// Fills an <see cref="ObservableCollection{T}"/> bound to a non-virtualized BindableLayout board in
/// small batches, so the first screenful paints immediately and the rest is appended below the fold
/// instead of one multi-hundred-ms native layout pass freezing the UI thread (measured on the S23+,
/// 2026-10-22). Only worth using when the board is empty — rebuilding a board that's already on
/// screen incrementally would make it shrink and re-grow (and jump the scroll position).
/// </summary>
public static class IncrementalFill
{
    /// <summary>Creates the collection holding the first <paramref name="firstBatch"/> items and, if more remain, appends them in the background while <paramref name="isCurrent"/> stays true.</summary>
    public static ObservableCollection<T> Start<T>(IReadOnlyList<T> items, int firstBatch, int nextBatch, int delayMs, Func<bool> isCurrent)
    {
        var collection = new ObservableCollection<T>(items.Take(firstBatch));
        if (items.Count > firstBatch)
            _ = AppendAsync(collection, items.Skip(firstBatch).ToList(), nextBatch, delayMs, isCurrent);
        return collection;
    }

    private static async Task AppendAsync<T>(ObservableCollection<T> collection, List<T> remaining, int batchSize, int delayMs, Func<bool> isCurrent)
    {
        for (var i = 0; i < remaining.Count; i += batchSize)
        {
            await Task.Delay(delayMs);
            if (!isCurrent()) return;

            var batch = remaining.Skip(i).Take(batchSize).ToList();
            var done = new TaskCompletionSource();
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (isCurrent())
                    foreach (var item in batch) collection.Add(item);
                done.SetResult();
            });
            await done.Task;
        }
    }
}
