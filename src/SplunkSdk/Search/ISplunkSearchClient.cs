using Marouanvs.Splunk.Models;

namespace Marouanvs.Splunk.Search;

/// <summary>
/// Executes Splunk searches and retrieves result rows.
/// </summary>
/// <remarks>
/// Use this surface for trusted SPL or when you need direct access to Splunk
/// search job lifecycle operations. Use <see cref="Marouanvs.Splunk.Analytics.ISplunkAnalyticsClient"/>
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
    /// <para>
    /// Splunk export can return status/message frames as well as result rows.
    /// The SDK skips benign message frames and throws <see cref="SplunkApiException"/>
    /// for streamed <c>ERROR</c> or <c>FATAL</c> messages.
    /// </para>
    /// <para>
    /// Unless <see cref="SplunkSearchRequest.Preview"/> is set, the SDK sends
    /// <c>preview=false</c> so the stream contains final results only. Set
    /// <c>Preview = true</c> to opt into in-progress preview frames and check
    /// <see cref="SplunkSearchResult.Preview"/> on each row.
    /// </para>
    /// <para>
    /// The returned sequence is deferred: every enumeration re-dispatches the
    /// search on Splunk and consumes search quota. Buffer the rows if you need
    /// to read them more than once.
    /// </para>
    /// <para>
    /// The REST <c>output_mode</c> is fixed to JSON by design because the
    /// SDK's result parser is JSON-only.
    /// </para>
    /// </remarks>
    IAsyncEnumerable<SplunkSearchResult> ExportAsync(
        SplunkSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a oneshot search and returns the buffered final results in one call.
    /// </summary>
    /// <param name="request">Trusted SPL request and REST search options.</param>
    /// <param name="cancellationToken">Cancellation token for the REST call.</param>
    /// <returns>The buffered final result rows.</returns>
    /// <remarks>
    /// <para>
    /// Oneshot dispatches the job with <c>exec_mode=oneshot</c> and returns the
    /// final results directly in the dispatch response — no search ID, polling,
    /// or job cleanup is involved. Prefer it for small synchronous queries when
    /// you do not need job lifecycle control. Prefer <see cref="ExportAsync"/>
    /// for large result sets, because oneshot buffers the entire response in
    /// memory and the dispatch POST is never retried by the SDK.
    /// </para>
    /// <para>
    /// Use <see cref="SplunkSearchRequest.Count"/> to bound the number of rows
    /// returned. The REST <c>output_mode</c> is fixed to JSON by design because
    /// the SDK's result parser is JSON-only.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SplunkSearchResult>> OneshotSearchAsync(
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
    /// <see cref="SplunkExecutionMode.Oneshot"/> is rejected here because its
    /// dispatch response contains results rather than a search ID; use
    /// <see cref="OneshotSearchAsync"/> instead.
    /// </remarks>
    Task<SplunkSearchJob> StartAsync(
        SplunkSearchRequest request,
        SplunkExecutionMode executionMode = SplunkExecutionMode.Normal,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current lifecycle status of an existing search job.
    /// </summary>
    /// <param name="searchId">Splunk search job ID, also called <c>sid</c>.</param>
    /// <param name="cancellationToken">Cancellation token for the status request.</param>
    /// <returns>A snapshot of the job's lifecycle state.</returns>
    /// <remarks>
    /// This is a read-only GET request and benefits from the SDK's built-in
    /// transient-failure retries. Use it after <see cref="StartAsync"/> with
    /// <see cref="SplunkExecutionMode.Normal"/> to detect when results are
    /// ready, because fetching results from an unfinished job can silently
    /// return empty or partial rows.
    /// </remarks>
    Task<SplunkSearchJobStatus> GetJobStatusAsync(
        string searchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Polls a search job until it completes, fails, or the timeout elapses.
    /// </summary>
    /// <param name="searchId">Splunk search job ID, also called <c>sid</c>.</param>
    /// <param name="pollInterval">Delay between status polls. Defaults to 500 milliseconds.</param>
    /// <param name="timeout">Maximum total wait time. Defaults to 5 minutes.</param>
    /// <param name="cancellationToken">Cancellation token for polling.</param>
    /// <returns>The final job status once the job is done.</returns>
    /// <remarks>
    /// Throws <see cref="SplunkApiException"/> when the job reaches a failed
    /// state and <see cref="TimeoutException"/> when the timeout elapses before
    /// completion. Exception messages contain only the job's dispatch state,
    /// never the search ID or SPL.
    /// </remarks>
    Task<SplunkSearchJobStatus> WaitForJobCompletionAsync(
        string searchId,
        TimeSpan? pollInterval = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an existing search job and its cached results.
    /// </summary>
    /// <param name="searchId">Splunk search job ID, also called <c>sid</c>.</param>
    /// <param name="cancellationToken">Cancellation token for the delete request.</param>
    /// <returns>A task that completes when Splunk acknowledges the deletion.</returns>
    /// <remarks>
    /// Deleting a job is idempotent on the SDK side and uses HTTP DELETE, so it
    /// benefits from the SDK's built-in transient-failure retries. Delete
    /// long-lived jobs you no longer need to free search head dispatch storage.
    /// </remarks>
    Task DeleteJobAsync(
        string searchId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Fetches transformed result rows from an existing search job.
    /// </summary>
    /// <param name="searchId">Splunk search job ID, also called <c>sid</c>.</param>
    /// <param name="request">Optional result paging, field, namespace, and post-process options.</param>
    /// <param name="cancellationToken">Cancellation token for the results request.</param>
    /// <returns>The fetched result rows.</returns>
    /// <remarks>
    /// <para>
    /// Result rows are buffered into memory. The default request is bounded;
    /// <c>Count = 0</c> is rejected by this buffered API.
    /// </para>
    /// <para>
    /// Plain result paging uses GET with query parameters and benefits from
    /// the SDK's built-in retries for idempotent requests. When
    /// <see cref="SplunkResultRequest.PostProcessSearch"/> is set, the request
    /// is sent as a POST form instead, because the v2 results endpoint accepts
    /// the post-process <c>search</c> parameter only through POST; the SDK
    /// does not retry that POST.
    /// </para>
    /// <para>
    /// Fetching results from a job that has not finished can return empty or
    /// partial rows. Use <see cref="WaitForJobCompletionAsync"/> first for
    /// jobs started with <see cref="SplunkExecutionMode.Normal"/>.
    /// </para>
    /// </remarks>
    Task<IReadOnlyList<SplunkSearchResult>> GetResultsAsync(
        string searchId,
        SplunkResultRequest? request = null,
        CancellationToken cancellationToken = default);
}
