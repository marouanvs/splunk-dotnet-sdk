using System.Net;
using System.Text;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using SplunkSdk;
using SplunkSdk.Authentication;
using SplunkSdk.Configuration;
using SplunkSdk.Models;
using SplunkSdk.Search;

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

/// <summary>
/// Benchmarks generated SPL for common SDK helper paths.
/// </summary>
[MemoryDiagnoser]
public class QueryBuilderBenchmarks
{
    /// <summary>
    /// Builds an error count search with one exact field filter.
    /// </summary>
    [Benchmark]
    public string ErrorCountQuery() =>
        SplunkQueryBuilder.FromIndex("payments-prod")
            .SearchText("ERROR")
            .FieldEquals("service", "checkout")
            .StatsCount("error_count")
            .Build();

    /// <summary>
    /// Builds a time-bucketed average metric search.
    /// </summary>
    [Benchmark]
    public string TimechartAverageQuery() =>
        SplunkQueryBuilder.FromIndex("payments-prod")
            .SearchText("completed")
            .FieldEquals("operation", "AuthorizePayment")
            .TimechartAverage("5m", "duration_ms", "average_value")
            .Build();
}

/// <summary>
/// Benchmarks SDK export parsing with deterministic in-memory JSON frames.
/// </summary>
[MemoryDiagnoser]
public class SearchExportBenchmarks
{
    private SplunkClient _client = null!;
    private string _payload = string.Empty;

    /// <summary>
    /// Number of Splunk result frames in the generated payload.
    /// </summary>
    [Params(10, 1_000)]
    public int RowCount { get; set; }

    /// <summary>
    /// Prepares the fake Splunk client and payload.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        _payload = GeneratePayload(RowCount);
        var httpClient = new HttpClient(new BenchmarkHttpMessageHandler(() => _payload));
        _client = new SplunkClient(
            httpClient,
            new SplunkClientOptions
            {
                ManagementUri = new Uri("https://splunk.example.com:8089"),
                TokenProvider = new StaticSplunkTokenProvider("benchmark-token"),
                Retry = new SplunkRetryOptions { MaxRetries = 0 }
            });
    }

    /// <summary>
    /// Releases the SDK client after each benchmark configuration.
    /// </summary>
    [GlobalCleanup]
    public void Cleanup() => _client.Dispose();

    /// <summary>
    /// Streams all result rows through the public SDK export API.
    /// </summary>
    [Benchmark]
    public async Task<int> ExportAndReadRows()
    {
        var rows = 0;
        await foreach (var result in _client.Search.ExportAsync(new SplunkSearchRequest("search index=\"payments-prod\" | stats count AS event_count")))
        {
            if (result.GetInt64("event_count") is not null)
            {
                rows++;
            }
        }

        return rows;
    }

    private static string GeneratePayload(int rows)
    {
        var builder = new StringBuilder(rows * 96);
        builder.AppendLine("""{"messages":[{"type":"INFO","text":"benchmark payload"}]}""");

        for (var index = 0; index < rows; index++)
        {
            builder.Append("{\"preview\":false,\"offset\":");
            builder.Append(index);
            builder.Append(",\"result\":{\"event_count\":\"");
            builder.Append(index + 1);
            builder.AppendLine("\",\"service\":\"checkout\"}}");
        }

        builder.AppendLine("""{"lastrow":true}""");
        return builder.ToString();
    }
}

internal sealed class BenchmarkHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<string> _bodyFactory;

    public BenchmarkHttpMessageHandler(Func<string> bodyFactory)
    {
        _bodyFactory = bodyFactory;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_bodyFactory(), Encoding.UTF8, "application/json")
        });
}
