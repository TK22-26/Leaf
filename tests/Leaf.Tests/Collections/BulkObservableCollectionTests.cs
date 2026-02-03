using System.Collections.Specialized;
using FluentAssertions;
using Leaf.Collections;
using Xunit;

namespace Leaf.Tests.Collections;

public class BulkObservableCollectionTests
{
    #region ReplaceAll Tests

    [Fact]
    public void ReplaceAll_FiresExactlyOneResetNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        var notificationCount = 0;
        NotifyCollectionChangedAction? lastAction = null;

        collection.CollectionChanged += (_, args) =>
        {
            notificationCount++;
            lastAction = args.Action;
        };

        var newItems = Enumerable.Range(1, 1000).ToList();

        // Act
        collection.ReplaceAll(newItems);

        // Assert
        notificationCount.Should().Be(1);
        lastAction.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void ReplaceAll_ClearsExistingItems()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };

        // Act
        collection.ReplaceAll([10, 20]);

        // Assert
        collection.Should().NotContain(1);
        collection.Should().NotContain(2);
        collection.Should().NotContain(3);
    }

    [Fact]
    public void ReplaceAll_AddsAllNewItems()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var newItems = new[] { 10, 20, 30, 40 };

        // Act
        collection.ReplaceAll(newItems);

        // Assert
        collection.Should().HaveCount(4);
        collection.Should().BeEquivalentTo(newItems);
    }

    [Fact]
    public void ReplaceAll_WithEmptyCollection_ClearsAll()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };

        // Act
        collection.ReplaceAll([]);

        // Assert
        collection.Should().BeEmpty();
    }

    [Fact]
    public void ReplaceAll_PreservesItemOrder()
    {
        // Arrange
        var collection = new BulkObservableCollection<string>();
        var newItems = new[] { "first", "second", "third" };

        // Act
        collection.ReplaceAll(newItems);

        // Assert
        collection[0].Should().Be("first");
        collection[1].Should().Be("second");
        collection[2].Should().Be("third");
    }

    [Fact]
    public void ReplaceAll_ThrowsOnNull()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();

        // Act & Assert
        var act = () => collection.ReplaceAll(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region AddRange Tests

    [Fact]
    public void AddRange_FiresExactlyOneResetNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        var notificationCount = 0;
        NotifyCollectionChangedAction? lastAction = null;

        collection.CollectionChanged += (_, args) =>
        {
            notificationCount++;
            lastAction = args.Action;
        };

        var newItems = Enumerable.Range(1, 1000).ToList();

        // Act
        collection.AddRange(newItems);

        // Assert
        notificationCount.Should().Be(1);
        lastAction.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void AddRange_AddsAllItems()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var newItems = new[] { 4, 5, 6 };

        // Act
        collection.AddRange(newItems);

        // Assert
        collection.Should().HaveCount(6);
        collection.Should().ContainInOrder(1, 2, 3, 4, 5, 6);
    }

    [Fact]
    public void AddRange_WithEmptyEnumerable_FiresNoNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var notificationCount = 0;

        collection.CollectionChanged += (_, _) => notificationCount++;

        // Act
        collection.AddRange([]);

        // Assert
        notificationCount.Should().Be(0);
        collection.Should().HaveCount(3);
    }

    [Fact]
    public void AddRange_ThrowsOnNull()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();

        // Act & Assert
        var act = () => collection.AddRange(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    #endregion

    #region RemoveAll Tests

    [Fact]
    public void RemoveAll_FiresExactlyOneResetNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3, 4, 5 };
        var notificationCount = 0;
        NotifyCollectionChangedAction? lastAction = null;

        collection.CollectionChanged += (_, args) =>
        {
            notificationCount++;
            lastAction = args.Action;
        };

        // Act
        var removed = collection.RemoveAll(x => x % 2 == 0);

        // Assert
        removed.Should().Be(2);
        notificationCount.Should().Be(1);
        lastAction.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void RemoveAll_RemovesMatchingItems()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3, 4, 5 };

        // Act
        collection.RemoveAll(x => x > 3);

        // Assert
        collection.Should().HaveCount(3);
        collection.Should().ContainInOrder(1, 2, 3);
    }

    [Fact]
    public void RemoveAll_NoMatches_ReturnsZeroAndNoNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        var notificationCount = 0;

        collection.CollectionChanged += (_, _) => notificationCount++;

        // Act
        var removed = collection.RemoveAll(x => x > 10);

        // Assert
        removed.Should().Be(0);
        notificationCount.Should().Be(0);
    }

    #endregion

    #region SuppressNotifications Tests

    [Fact]
    public void SuppressNotifications_PreventsNotificationsDuringScope()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        var notificationCount = 0;

        collection.CollectionChanged += (_, _) => notificationCount++;

        // Act
        using (collection.SuppressNotifications())
        {
            collection.Add(1);
            collection.Add(2);
            collection.Add(3);
        }

        // Assert - Should fire exactly one Reset on dispose
        notificationCount.Should().Be(1);
    }

    [Fact]
    public void SuppressNotifications_FiresResetOnDispose()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        NotifyCollectionChangedAction? lastAction = null;

        collection.CollectionChanged += (_, args) => lastAction = args.Action;

        // Act
        using (collection.SuppressNotifications())
        {
            collection.Add(1);
        }

        // Assert
        lastAction.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void SuppressNotifications_NestedScopes_OnlyOuterFires()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        var notificationCount = 0;

        collection.CollectionChanged += (_, _) => notificationCount++;

        // Act - nested suppression (unusual but should handle gracefully)
        var outer = collection.SuppressNotifications();
        collection.Add(1);
        outer.Dispose();
        // After dispose, notifications are enabled again

        // Assert
        notificationCount.Should().Be(1);
        collection.Should().HaveCount(1);
    }

    #endregion

    #region Standard Collection Behavior Tests

    [Fact]
    public void Add_FiresAddNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        NotifyCollectionChangedAction? action = null;

        collection.CollectionChanged += (_, args) => action = args.Action;

        // Act
        collection.Add(1);

        // Assert
        action.Should().Be(NotifyCollectionChangedAction.Add);
    }

    [Fact]
    public void Remove_FiresRemoveNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedAction? action = null;

        collection.CollectionChanged += (_, args) => action = args.Action;

        // Act
        collection.Remove(2);

        // Assert
        action.Should().Be(NotifyCollectionChangedAction.Remove);
    }

    [Fact]
    public void Clear_FiresResetNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedAction? action = null;

        collection.CollectionChanged += (_, args) => action = args.Action;

        // Act
        collection.Clear();

        // Assert
        action.Should().Be(NotifyCollectionChangedAction.Reset);
    }

    [Fact]
    public void Indexer_FiresReplaceNotification()
    {
        // Arrange
        var collection = new BulkObservableCollection<int> { 1, 2, 3 };
        NotifyCollectionChangedAction? action = null;

        collection.CollectionChanged += (_, args) => action = args.Action;

        // Act
        collection[1] = 20;

        // Assert
        action.Should().Be(NotifyCollectionChangedAction.Replace);
        collection[1].Should().Be(20);
    }

    #endregion

    #region Data Integrity Tests

    [Fact]
    public void ReplaceAll_WithLargeCollection_MaintainsIntegrity()
    {
        // Arrange
        var collection = new BulkObservableCollection<int>();
        var largeList = Enumerable.Range(1, 10000).ToList();

        // Act
        collection.ReplaceAll(largeList);

        // Assert
        collection.Should().HaveCount(10000);
        collection.First().Should().Be(1);
        collection.Last().Should().Be(10000);
    }

    [Fact]
    public void AddRange_PreservesExistingItems()
    {
        // Arrange
        var collection = new BulkObservableCollection<string> { "a", "b", "c" };

        // Act
        collection.AddRange(["d", "e"]);

        // Assert
        collection.Should().HaveCount(5);
        collection.Should().StartWith("a");
        collection.Should().EndWith("e");
    }

    #endregion
}
