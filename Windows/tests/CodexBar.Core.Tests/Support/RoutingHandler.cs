using System.Net;
using System.Net.Http;

namespace CodexBar.Core.Tests.Support;

internal sealed record CapturedRequest(
    Uri Uri,
    HttpMethod Method,
    string? Authorization,
    string? UserAgent,
    string Body);

/// <summary>
/// Routes stub responses by request path so multi-call providers can be tested without network. Requests
/// are captured eagerly because the provider disposes them (and their content) after the call.
/// </summary>
internal sealed class RoutingHandler : HttpMessageHandler
{
    private readonly Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes;

    public RoutingHandler(Dictionary<string, Func<HttpRequestMessage, HttpResponseMessage>> routes) =>
        this.routes = routes;

    public List<CapturedRequest> Requests { get; } = [];

    public RoutingHandler Add(string path, HttpStatusCode statusCode, string body)
    {
        this.routes[path] = _ => new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"),
        };
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        this.Requests.Add(new CapturedRequest(
            request.RequestUri!,
            request.Method,
            request.Headers.Authorization?.ToString(),
            request.Headers.UserAgent.ToString(),
            request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult() ?? string.Empty));

        var path = request.RequestUri!.AbsolutePath;
        if (this.routes.TryGetValue(path, out var responder))
        {
            return Task.FromResult(responder(request));
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = new StringContent($"{{\"error\":\"no stub for {path}\"}}", System.Text.Encoding.UTF8, "application/json"),
        });
    }
}
