using System.Collections.ObjectModel;
using System.Collections.Specialized;

namespace Leaf.Collections;

/// <summary>
/// An ObservableCollection that supports batch operations with a single notification.
/// Use this to avoid O(n^2) performance when populating large collections.
/// </summary>
/// <typeparam name="T">The type of elements in the collection.</typeparam>
public class BulkObservableCollection<T> : ObservableCollection<T>
{
    private bool _suppressNotifications;

    /// <summary>
    /// Replaces all items in the collection with the specified items, firing only one Reset notification.
    /// </summary>
    /// <param name="items">The items to replace the collection contents with.</param>
    public void ReplaceAll(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        _suppressNotifications = true;
        try
        {
            Items.Clear();
            foreach (var item in items)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Adds multiple items to the collection, firing only one Reset notification.
    /// </summary>
    /// <param name="items">The items to add.</param>
    public void AddRange(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var itemsList = items.ToList();
        if (itemsList.Count == 0)
            return;

        _suppressNotifications = true;
        try
        {
            foreach (var item in itemsList)
            {
                Items.Add(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }

    /// <summary>
    /// Removes all items from the collection that match the predicate, firing only one Reset notification.
    /// </summary>
    /// <param name="predicate">The condition for items to remove.</param>
    /// <returns>The number of items removed.</returns>
    public int RemoveAll(Predicate<T> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        var itemsToRemove = Items.Where(item => predicate(item)).ToList();
        if (itemsToRemove.Count == 0)
            return 0;

        _suppressNotifications = true;
        try
        {
            foreach (var item in itemsToRemove)
            {
                Items.Remove(item);
            }
        }
        finally
        {
            _suppressNotifications = false;
        }

        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        return itemsToRemove.Count;
    }

    /// <summary>
    /// Creates a scope that suppresses notifications until disposed.
    /// On dispose, fires a single Reset notification.
    /// </summary>
    /// <returns>A disposable that fires the notification when disposed.</returns>
    public IDisposable SuppressNotifications()
    {
        return new NotificationSuppressionScope(this);
    }

    /// <inheritdoc/>
    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (!_suppressNotifications)
        {
            base.OnCollectionChanged(e);
        }
    }

    private sealed class NotificationSuppressionScope : IDisposable
    {
        private readonly BulkObservableCollection<T> _collection;
        private bool _disposed;

        public NotificationSuppressionScope(BulkObservableCollection<T> collection)
        {
            _collection = collection;
            _collection._suppressNotifications = true;
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _collection._suppressNotifications = false;
            _collection.OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
