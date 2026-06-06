using JuegoFramework.Helpers;
using Xunit;

namespace JuegoFrameworkTests;

public class WebSocketBackplaneTests
{
    [Fact]
    public void TryGetInstanceId_ParsesInstancePrefix()
    {
        Assert.True(WebSocketBackplane.TryGetInstanceId("inst7:abc123", out var id));
        Assert.Equal("inst7", id);
    }

    [Theory]
    [InlineData("")]
    [InlineData("nocolon")]
    [InlineData(":leadingcolon")]
    public void TryGetInstanceId_RejectsMalformed(string cid)
    {
        Assert.False(WebSocketBackplane.TryGetInstanceId(cid, out _));
    }

    [Fact]
    public void NewConnectionId_EmbedsInstanceIdAndIsParseable()
    {
        var cid = WebSocketBackplane.NewConnectionId();
        Assert.True(WebSocketBackplane.TryGetInstanceId(cid, out var id));
        Assert.Equal(WebSocketBackplane.InstanceId, id);
        Assert.True(WebSocketBackplane.IsForThisInstance(cid));
    }

    [Fact]
    public void GroupByInstance_GroupsAndDropsUnroutable()
    {
        var groups = WebSocketBackplane.GroupByInstance(
            new[] { "a:1", "a:2", "b:3", "bad" });
        Assert.Equal(2, groups.Count);
        Assert.Equal(new[] { "a:1", "a:2" }, groups["a"]);
        Assert.Equal(new[] { "b:3" }, groups["b"]);
    }

    [Theory]
    [InlineData("{\"type\":\"ping\"}", true)]
    [InlineData("{ \"type\" : \"ping\" }", true)]
    [InlineData("{\"type\":\"pong\"}", false)]
    [InlineData("{\"method\":\"POST\"}", false)]
    [InlineData("not json", false)]
    [InlineData("", false)]
    public void IsPingMessage_DetectsPingEnvelopeOnly(string msg, bool expected)
    {
        Assert.Equal(expected, WebSocketBackplane.IsPingMessage(msg));
    }
}
