using System.Net;
using Xunit;

namespace Marouanvs.Splunk.Tests.TestInfrastructure;

internal sealed class QueueHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<RequestSnapshot> Requests { get; } = [];

    public void Enqueue(HttpStatusCode statusCode, string body, string mediaType = "application/json")
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, mediaType),
            ReasonPhrase = statusCode.ToString()
        }));
    }

    public void EnqueueException(Exception exception)
    {
        _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    public void AssertNoPendingResponses()
    {
        Assert.True(
            _responses.Count == 0,
            $"Expected every queued fake response to be consumed, but {_responses.Count} response(s) remained.");
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.Count == 0)
        {
            throw new InvalidOperationException("No fake response was queued.");
        }

        var body = request.Content is null
            ? string.Empty
            : await request.Content.ReadAsStringAsync(cancellationToken);

        Requests.Add(new RequestSnapshot(
            request.Method,
            request.RequestUri ?? throw new InvalidOperationException("Request URI missing."),
            request.Headers.Authorization,
            request.Headers.UserAgent.ToString(),
            body));

        return await _responses.Dequeue()(request, cancellationToken);
    }
}
