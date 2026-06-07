using SplunkSdk.Models;

namespace SplunkSdk.Search;

/// <summary>
/// Executes Splunk searches and retrieves result rows.
/// </summary>
/// <remarks>
/// Use this surface for trusted SPL or when you need direct access to Splunk
/// search job lifecycle operations. Use <see cref="SplunkSdk.Analytics.ISplunkAnalyticsClient"/>
/// for safe generated metric queries.
/// </remarks>
public interface ISplunkSearchClient
{
    /// <summary>
    /// Streams rows from the Splunk search export endpoint.
    /// </summary>
    /// <param name="request">Trusted SPL request and REST search options.</param>
    /// <param name="cancellationToken">Cancellation token for the REST call and stream enumeration.</param>
    /// <returns>An asynchronous stream of Splunk result rows.</returns>
    /// <remarks>
    /// Splunk export can return status/message frames as well as result rows.
    /// The SDK skips benign message frames and throws <see cref="SplunkApiException"/>
    /// for streamed <c>ERROR</c> or <c>FATAL</c> messages.
    /// </remarks>
    IAsyncEnumerable<SplunkSearchResult> ExportAsync(
        SplunkSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a Splunk search job and returns its search ID.
    /// </summary>
    /// <param name="request">Trusted SPL request and dispatch options.</param>
    /// <param name="executionMode">Splunk job execution mode.</param>
    /// <param name="cancellationToken">Cancellation token for the dispatch call.</param>
    /// <returns>The created Splunk search job.</returns>
    /// <remarks>
    /// Result-only options such as <c>count</c> and <c>preview</c> are not sent
    /// to the job dispatch endpoint. Use <see cref="GetResultsAsync"/> with
    /// <see cref="SplunkResultRequest"/> to page or limit results.
    /// </remarks>
    Task<SplunkSearchJob> StartAsync(
        SplunkSearchRequest request,
        SplunkExecutionMode executionMode = SplunkExecutionMode.Normal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches transformed result rows from an existing search job.
    /// </summary>
    /// <param name="searchId">Splunk search job ID, also called <c>sid</c>.</param>
    /// <param name="request">Optional result paging, field, namespace, and post-process options.</param>
    /// <param name="cancellationToken">Cancellation token for the results request.</param>
    /// <returns>The fetched result rows.</returns>
    /// <remarks>
    /// Result rows are buffered into memory. The default request is bounded;
    /// <c>Count = 0</c> is rejected by this buffered API.
    /// </remarks>
    Task<IReadOnlyList<SplunkSearchResult>> GetResultsAsync(
        string searchId,
        SplunkResultRequest? request = null,
        CancellationToken cancellationToken = default);
}
