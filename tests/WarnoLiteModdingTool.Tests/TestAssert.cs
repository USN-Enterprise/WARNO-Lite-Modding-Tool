namespace WarnoLiteModdingTool.Tests;

internal static class TestAssert
{
    public static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public static void False(bool condition, string message) => True(!condition, message);

    public static void Equal<T>(T expected, T actual, string message)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"{message} 预期：{expected}；实际：{actual}。" );
        }
    }

    public static T Single<T>(IReadOnlyList<T> values, string message)
    {
        Equal(1, values.Count, message);
        return values[0];
    }

    public static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"{message} 预期异常：{typeof(TException).Name}。");
    }
}
