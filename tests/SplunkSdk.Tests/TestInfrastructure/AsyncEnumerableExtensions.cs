namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> values)
    {
        var results = new List<T>();
        await foreach (var value in values)
        {
            results.Add(value);
        }

        return results;
    }
}
