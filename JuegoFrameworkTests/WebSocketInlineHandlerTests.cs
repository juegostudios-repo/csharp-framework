using JuegoFramework.Helpers;
using Xunit;

namespace JuegoFrameworkTests;

// Verifies the pluggable inline socket-message handler dispatch decision in the receive
// loop: ping is always handled inline (unchanged); a registered handler that returns true
// short-circuits routing; one that returns false (or no handler) falls through to routing.
public class WebSocketInlineHandlerTests : IDisposable
{
    public WebSocketInlineHandlerTests() => WebSocketService.InlineMessageHandler = null;

    // Reset the static hook so tests don't leak into each other / other suites.
    public void Dispose() => WebSocketService.InlineMessageHandler = null;

    [Fact]
    public async Task NoHandler_NonPingMessage_Routes()
    {
        WebSocketService.InlineMessageHandler = null;

        var disposition = await WebSocketService.ClassifyInboundAsync("inst1:abc", "{\"method\":\"POST\"}");

        Assert.Equal(WebSocketService.InboundDisposition.Route, disposition);
    }

    [Fact]
    public async Task Ping_IsHandledInline_EvenWithHandlerRegistered()
    {
        // A registered handler that would claim everything must NOT intercept ping;
        // ping keeps its existing inline behavior and the handler is never consulted.
        var handlerInvoked = false;
        WebSocketService.InlineMessageHandler = (_, _) => { handlerInvoked = true; return Task.FromResult(true); };

        var disposition = await WebSocketService.ClassifyInboundAsync("inst1:abc", "{\"type\":\"ping\"}");

        Assert.Equal(WebSocketService.InboundDisposition.Ping, disposition);
        Assert.False(handlerInvoked);
    }

    [Fact]
    public async Task HandlerReturnsTrue_ConsumesMessage_NoRouting()
    {
        string? seenConnectionId = null;
        string? seenMessage = null;
        WebSocketService.InlineMessageHandler = (connectionId, message) =>
        {
            seenConnectionId = connectionId;
            seenMessage = message;
            return Task.FromResult(true);
        };

        var disposition = await WebSocketService.ClassifyInboundAsync("inst1:abc", "{\"action\":\"viewport_interest\"}");

        Assert.Equal(WebSocketService.InboundDisposition.HandledInline, disposition);
        // The handler receives the connect-time identity and the raw message text.
        Assert.Equal("inst1:abc", seenConnectionId);
        Assert.Equal("{\"action\":\"viewport_interest\"}", seenMessage);
    }

    [Fact]
    public async Task HandlerReturnsFalse_FallsThroughToRouting()
    {
        var handlerInvoked = false;
        WebSocketService.InlineMessageHandler = (_, _) => { handlerInvoked = true; return Task.FromResult(false); };

        var disposition = await WebSocketService.ClassifyInboundAsync("inst1:abc", "{\"method\":\"POST\"}");

        Assert.True(handlerInvoked);
        Assert.Equal(WebSocketService.InboundDisposition.Route, disposition);
    }
}
