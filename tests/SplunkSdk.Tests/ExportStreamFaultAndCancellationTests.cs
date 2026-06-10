using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Marouanvs.Splunk.Authentication;
using Marouanvs.Splunk.Configuration;
using Marouanvs.Splunk.Models;
using Marouanvs.Splunk.Tests.TestInfrastructure;
using Xunit;
using static Marouanvs.Splunk.Tests.TestInfrastructure.TestHelpers;

namespace Marouanvs.Splunk.Tests;

public sealed class ExportStreamFaultAndCancellationTests
{
    [Fact]
    public async Task MidStreamTransportFailureRaisesSanitizedInterruptedStreamError()
    {
        var stream = new ScriptedReadStream(
            ScriptedBytes("{\"preview\":false,\"offset\":0,\"result\":{\"service\":\"checkout\"}}\n"),
            ScriptedThrow(new IOException("Connection reset by peer while reading payload bytes.")));
        var handler = new ScriptedStreamHandler(stream);
        using var client = CreateStreamingClient(handler);

        var rows = new List<SplunkSearchResult>();
        var exception = await Assert.ThrowsAsync<SplunkResponseFormatException>(async () =>
        {
            await foreach (var row in client.Search.ExportAsync(
                new SplunkSearchRequest("search index=\"team\" | table service")))
            {
                rows.Add(row);
            }
        });

        Assert.Equal("checkout", Assert.Single(rows).GetString("service"));
        Assert.Equal("The Splunk search export stream was interrupted before completion.", exception.Message);
        Assert.Equal(HttpStatusCode.OK, exception.StatusCode);
        Assert.Equal("Unparseable response", exception.ReasonPhrase);
        Assert.IsType<IOException>(exception.InnerException);
        Assert.DoesNotContain("checkout", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Connection reset", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task CancellingMidExportEnumerationThrowsOperationCanceled()
    {
        var stream = new ScriptedReadStream(
            ScriptedBytes("{\"preview\":false,\"offset\":0,\"result\":{\"service\":\"checkout\"}}\n"),
            ScriptedBytes("{\"preview\":false,\"offset\":1,\"result\":{\"service\":\"billing\"}}\n"));
        var handler = new ScriptedStreamHandler(stream);
        using var client = CreateStreamingClient(handler);
        using var cancellation = new CancellationTokenSource();

        var rows = new List<SplunkSearchResult>();
        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var row in client.Search.ExportAsync(
                new SplunkSearchRequest("search index=\"team\" | table service"),
                cancellation.Token))
            {
                rows.Add(row);
                cancellation.Cancel();
            }
        });

        Assert.Equal("checkout", Assert.Single(rows).GetString("service"));
        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetResultsHonorsAPreCancelledTokenWithoutSendingARequest()
    {
        var handler = new QueueHttpMessageHandler();
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await client.Search.GetResultsAsync(
                "1700000000.51",
                new SplunkResultRequest { Count = 1 },
                cancellation.Token));

        Assert.Equal(cancellation.Token, exception.CancellationToken);
        Assert.Empty(handler.Requests);
    }

    private static Func<byte[]> ScriptedBytes(string text) => () => Encoding.UTF8.GetBytes(text);

    private static Func<byte[]> ScriptedThrow(Exception exception) => () => throw exception;

    private static SplunkClient CreateStreamingClient(HttpMessageHandler handler)
    {
        var options = new SplunkClientOptions
        {
            ManagementUri = new Uri("https://splunk.example.com:8089"),
            TokenProvider = new StaticSplunkTokenProvider("test-token"),
            Retry = new SplunkRetryOptions
            {
                MaxRetries = 0,
                BaseDelay = TimeSpan.Zero,
                MaxDelay = TimeSpan.Zero
            }
        };

        return new SplunkClient(new HttpClient(handler), options);
    }

    /// <summary>
    /// Returns one fake response whose body is served by a caller-scripted stream,
    /// so tests can fault or stall the export stream after headers are received.
    /// </summary>
    private sealed class ScriptedStreamHandler : HttpMessageHandler
    {
        private readonly Stream _responseStream;

        public ScriptedStreamHandler(Stream responseStream)
        {
            _responseStream = responseStream;
        }

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ScriptedStreamContent(_responseStream),
                ReasonPhrase = "OK"
            });
        }
    }

    private sealed class ScriptedStreamContent : HttpContent
    {
        private readonly Stream _stream;

        public ScriptedStreamContent(Stream stream)
        {
            _stream = stream;
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task<Stream> CreateContentReadStreamAsync() => Task.FromResult(_stream);

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            _stream.CopyToAsync(stream);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }

    /// <summary>
    /// Read-only stream that serves one scripted segment per read call. A segment
    /// can also throw, which simulates a transport failure between export frames.
    /// </summary>
    private sealed class ScriptedReadStream : Stream
    {
        private readonly Queue<Func<byte[]>> _reads;
        private byte[] _current = [];
        private int _position;

        public ScriptedReadStream(params Func<byte[]>[] reads)
        {
            _reads = new Queue<Func<byte[]>>(reads);
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(Read(buffer.Span));
        }

        public override int Read(Span<byte> buffer)
        {
            while (_position >= _current.Length)
            {
                if (_reads.Count == 0)
                {
                    return 0;
                }

                _current = _reads.Dequeue()();
                _position = 0;
            }

            var copied = Math.Min(buffer.Length, _current.Length - _position);
            _current.AsSpan(_position, copied).CopyTo(buffer);
            _position += copied;
            return copied;
        }

        public override int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
