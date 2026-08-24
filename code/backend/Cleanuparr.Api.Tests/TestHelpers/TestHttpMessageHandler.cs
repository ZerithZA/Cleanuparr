using System.Net;

namespace Cleanuparr.Api.Tests.TestHelpers;

/// <summary>
/// Test double for HttpMessageHandler since NSubstitute cannot mock protected methods.
/// Delegates to a configurable handler function.
/// </summary>
public sealed class TestHttpMessageHandler : HttpMessageHandler
{
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handler
        = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    public void SetupResponse(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
    {
        _handler = handler;
    }

    public void SetupThrow(Exception exception)
    {
        _handler = (_, _) => throw exception;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken) =>
        _handler(request, cancellationToken);
}
