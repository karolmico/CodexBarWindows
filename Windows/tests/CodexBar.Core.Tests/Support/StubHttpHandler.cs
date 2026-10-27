using System.Net;
using System.Net.Http;

namespace CodexBar.Core.Tests.Support;

/// <summary>Captures the outgoing request and returns a canned response, so provider tests never touch the network.</summary>
internal sealed class StubHttpHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> responder;

    public StubHttpHandler(HttpStatusCode statusCode, string body, string contentType = "application/json")
        : this(_ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, contentType),
        })
    {
    }

    public StubHttpHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) =>
        this.responder = responder;

    public HttpRequestMessage? LastRequest { get; private set; }

    public int RequestCount { get; private set; }

    public static HttpClient Client(StubHttpHandler handler) => new(handler, disposeHandler: false);

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        this.LastRequest = request;
        this.RequestCount++;
        return Task.FromResult(this.responder(request));
    }
}
