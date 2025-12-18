using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;

namespace DukascopyDownloader.Tests.Download.Fakes;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>> _responses;

    public FakeHttpMessageHandler(params Func<HttpRequestMessage, HttpResponseMessage>[] responses)
    {
        _responses = new ConcurrentQueue<Func<HttpRequestMessage, HttpResponseMessage>>(responses);
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_responses.TryDequeue(out var factory))
        {
            return Task.FromResult(factory(request));
        }

        return Task.FromResult(CreateResponse(HttpStatusCode.OK, Array.Empty<byte>()));
    }

    public static Func<HttpRequestMessage, HttpResponseMessage> Respond(HttpStatusCode statusCode, byte[] payload)
    {
        return _ => CreateResponse(statusCode, payload);
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode statusCode, byte[] payload)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new ByteArrayContent(payload)
        };
    }
}
