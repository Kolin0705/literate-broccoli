using Xunit;

namespace ScreenTranslator.Core.Tests;

public sealed class TranslationCacheTests
{
    [Fact]
    public void EvictsLeastRecentlyReadEntry()
    {
        var cache = new TranslationCache(2);
        cache.Store("first", "一");
        cache.Store("second", "二");
        Assert.True(cache.TryGet("first", out var first));
        Assert.Equal("一", first);

        cache.Store("third", "三");

        Assert.False(cache.TryGet("second", out _));
        Assert.True(cache.TryGet("first", out _));
        Assert.True(cache.TryGet("third", out _));
    }

    [Fact]
    public void UpdatingEntryRefreshesRecencyWithoutConsumingCapacity()
    {
        var cache = new TranslationCache(2);
        cache.Store("a", "old");
        cache.Store("b", "b");
        cache.Store("a", "new");
        cache.Store("c", "c");

        Assert.True(cache.TryGet("a", out var value));
        Assert.Equal("new", value);
        Assert.False(cache.TryGet("b", out _));
    }

    [Fact]
    public void DefaultCapacityIs32AndClearRemovesAllEntries()
    {
        var cache = new TranslationCache();
        for (var i = 0; i < 33; i++)
            cache.Store(i.ToString(), i.ToString());

        Assert.False(cache.TryGet("0", out _));
        for (var i = 1; i < 33; i++)
            Assert.True(cache.TryGet(i.ToString(), out _));

        cache.Clear();
        for (var i = 0; i < 33; i++)
            Assert.False(cache.TryGet(i.ToString(), out _));
    }

    [Fact]
    public void KeysAreCaseSensitive()
    {
        var cache = new TranslationCache();
        cache.Store("US", "美国");
        Assert.False(cache.TryGet("us", out _));
    }

    [Fact]
    public void ConcurrentAccessDoesNotCorruptCache()
    {
        var cache = new TranslationCache();
        Parallel.For(0, 1000, i =>
        {
            cache.Store(i.ToString(), "value");
            cache.TryGet((i - 1).ToString(), out _);
            if (i % 79 == 0)
                cache.Clear();
        });
        cache.Store("final", "最终");
        Assert.True(cache.TryGet("final", out var result));
        Assert.Equal("最终", result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsInvalidCapacity(int capacity) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new TranslationCache(capacity));
}
