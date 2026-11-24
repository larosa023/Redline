using System.Collections;

namespace Xunit;

[AttributeUsage(AttributeTargets.Method)]
public class FactAttribute : Attribute { public string? Skip { get; set; } public string? DisplayName { get; set; } }

[AttributeUsage(AttributeTargets.Method)]
public sealed class TheoryAttribute : FactAttribute { }

[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class InlineDataAttribute(params object?[] data) : Attribute { public object?[] Data { get; } = data; }

[AttributeUsage(AttributeTargets.Class)]
public sealed class CollectionAttribute(string name) : Attribute { public string Name { get; } = name; }

[AttributeUsage(AttributeTargets.Class)]
public sealed class CollectionDefinitionAttribute(string name) : Attribute { public string Name { get; } = name; public bool DisableParallelization { get; set; } }

public interface IAsyncLifetime { Task InitializeAsync(); Task DisposeAsync(); }

public sealed class XunitException(string message) : Exception(message);

public static class Assert
{
    static string S(object? o) => o switch { null => "null", string s => $"\"{s}\"", IEnumerable e => "[" + string.Join(", ", e.Cast<object?>().Select(S)) + "]", _ => o.ToString() ?? "" };

    static bool Eq(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is not string && b is not string && a is IEnumerable ea && b is IEnumerable eb)
            return ea.Cast<object?>().SequenceEqual(eb.Cast<object?>(), new Cmp());
        return Equals(a, b);
    }
    sealed class Cmp : IEqualityComparer<object?> { public new bool Equals(object? x, object? y) => Eq(x, y); public int GetHashCode(object? o) => 0; }

    public static void Equal<T>(T expected, T actual) { if (!Eq(expected, actual)) throw new XunitException($"Assert.Equal failed. Expected: {S(expected)} Actual: {S(actual)}"); }
    public static void Equal(double expected, double actual, int precision) { if (Math.Round(expected, precision) != Math.Round(actual, precision)) throw new XunitException($"Assert.Equal failed. Expected: {expected} Actual: {actual} (precision {precision})"); }
    public static void Equal(decimal expected, decimal actual, int precision) { if (Math.Round(expected, precision) != Math.Round(actual, precision)) throw new XunitException($"Assert.Equal failed. Expected: {expected} Actual: {actual}"); }
    public static void NotEqual<T>(T expected, T actual) { if (Eq(expected, actual)) throw new XunitException($"Assert.NotEqual failed. Both: {S(actual)}"); }
    public static void True(bool condition, string? userMessage = null) { if (!condition) throw new XunitException("Assert.True failed. " + userMessage); }
    public static void False(bool condition, string? userMessage = null) { if (condition) throw new XunitException("Assert.False failed. " + userMessage); }
    public static void Null(object? o) { if (o is not null) throw new XunitException($"Assert.Null failed. Actual: {S(o)}"); }
    public static void NotNull(object? o) { if (o is null) throw new XunitException("Assert.NotNull failed."); }
    public static void Same(object? a, object? b) { if (!ReferenceEquals(a, b)) throw new XunitException("Assert.Same failed."); }
    public static void Fail(string message) => throw new XunitException("Assert.Fail: " + message);
    public static T IsType<T>(object? o) { if (o is null || o.GetType() != typeof(T)) throw new XunitException($"Assert.IsType failed. Expected {typeof(T).Name}, got {o?.GetType().Name ?? "null"}"); return (T)o; }
    public static T IsAssignableFrom<T>(object? o) { if (o is not T t) throw new XunitException($"Assert.IsAssignableFrom failed. Expected {typeof(T).Name}, got {o?.GetType().Name ?? "null"}"); return t; }
    public static void InRange<T>(T actual, T low, T high) where T : IComparable<T> { if (actual.CompareTo(low) < 0 || actual.CompareTo(high) > 0) throw new XunitException($"Assert.InRange failed. {actual} not in [{low},{high}]"); }
    public static void Contains(string expectedSubstring, string? actual) { if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal)) throw new XunitException($"Assert.Contains failed. \"{expectedSubstring}\" not in {S(actual)}"); }
    public static void Contains(string expectedSubstring, string? actual, StringComparison cmp) { if (actual is null || !actual.Contains(expectedSubstring, cmp)) throw new XunitException($"Assert.Contains failed. \"{expectedSubstring}\" not in {S(actual)}"); }
    public static void Matches(string pattern, string? actual)
    {
        if (actual is null || !System.Text.RegularExpressions.Regex.IsMatch(actual, pattern)) throw new XunitException($"Assert.Matches failed. Pattern: {pattern} Actual: {actual}");
    }

    public static void StartsWith(string expected, string? actual) { if (actual is null || !actual.StartsWith(expected, StringComparison.Ordinal)) throw new XunitException($"Assert.StartsWith failed. {S(actual)} doesn't start with \"{expected}\""); }
    public static void EndsWith(string expected, string? actual) { if (actual is null || !actual.EndsWith(expected, StringComparison.Ordinal)) throw new XunitException($"Assert.EndsWith failed. {S(actual)}"); }
    public static void DoesNotContain(string sub, string? actual) { if (actual is not null && actual.Contains(sub, StringComparison.Ordinal)) throw new XunitException($"Assert.DoesNotContain failed. \"{sub}\" in {S(actual)}"); }
    public static void Contains<T>(T expected, IEnumerable<T> collection) { if (!collection.Any(x => Eq(x, expected))) throw new XunitException($"Assert.Contains failed. {S(expected)} not in {S(collection)}"); }
    public static void Contains<T>(IEnumerable<T> collection, Predicate<T> filter) { if (!collection.Any(x => filter(x))) throw new XunitException($"Assert.Contains failed. No match in {S(collection)}"); }
    public static void DoesNotContain<T>(T expected, IEnumerable<T> collection) { if (collection.Any(x => Eq(x, expected))) throw new XunitException($"Assert.DoesNotContain failed. {S(expected)} in collection"); }
    public static void DoesNotContain<T>(IEnumerable<T> collection, Predicate<T> filter) { if (collection.Any(x => filter(x))) throw new XunitException("Assert.DoesNotContain failed."); }
    public static void Empty(IEnumerable collection) { if (collection.Cast<object?>().Any()) throw new XunitException($"Assert.Empty failed. {S(collection)}"); }
    public static void NotEmpty(IEnumerable collection) { if (!collection.Cast<object?>().Any()) throw new XunitException("Assert.NotEmpty failed."); }
    public static T Single<T>(IEnumerable<T> collection) { var l = collection.ToList(); if (l.Count != 1) throw new XunitException($"Assert.Single failed. Count {l.Count}"); return l[0]; }
    public static T Single<T>(IEnumerable<T> collection, Predicate<T> p) { var l = collection.Where(x => p(x)).ToList(); if (l.Count != 1) throw new XunitException($"Assert.Single failed. Count {l.Count}"); return l[0]; }
    public static void All<T>(IEnumerable<T> collection, Action<T> action) { foreach (var x in collection) action(x); }

    public static T Throws<T>(Action a) where T : Exception
    {
        try { a(); } catch (Exception e) { if (e.GetType() == typeof(T)) return (T)e; throw new XunitException($"Assert.Throws failed. Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}"); }
        throw new XunitException($"Assert.Throws failed. No exception; expected {typeof(T).Name}");
    }
    public static T Throws<T>(Func<object?> f) where T : Exception => Throws<T>(() => { f(); });
    public static T ThrowsAny<T>(Action a) where T : Exception
    {
        try { a(); } catch (T e) { return e; } catch (Exception e) { throw new XunitException($"Assert.ThrowsAny failed. Got {e.GetType().Name}"); }
        throw new XunitException("Assert.ThrowsAny failed. No exception");
    }
    public static async Task<T> ThrowsAsync<T>(Func<Task> f) where T : Exception
    {
        try { await f(); } catch (Exception e) { if (e.GetType() == typeof(T)) return (T)e; throw new XunitException($"Assert.ThrowsAsync failed. Expected {typeof(T).Name}, got {e.GetType().Name}: {e.Message}"); }
        throw new XunitException($"Assert.ThrowsAsync failed. No exception; expected {typeof(T).Name}");
    }
    public static async Task<T> ThrowsAnyAsync<T>(Func<Task> f) where T : Exception
    {
        try { await f(); } catch (T e) { return e; } catch (Exception e) { throw new XunitException($"Assert.ThrowsAnyAsync failed. Got {e.GetType().Name}"); }
        throw new XunitException("Assert.ThrowsAnyAsync failed. No exception");
    }
}
