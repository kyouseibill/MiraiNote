using System.Text;
using MiraiNote.Core.Services;
using Xunit;

namespace MiraiNote.Tests;

public class ChatStreamDeltaTests
{
    [Fact]
    public void Short_incremental_chunks_skip_the_accumulated_prefix()
    {
        var previous = new StringBuilder(new string('a', 4000));
        Assert.Equal("z", ChatService.NormalizeStreamDelta(previous, "z", cumulative: true));
    }

    [Fact]
    public void Cumulative_snapshot_returns_only_the_new_suffix()
    {
        var previous = new StringBuilder("思考");
        Assert.Equal("完毕", ChatService.NormalizeStreamDelta(previous, "思考完毕", cumulative: true));
        Assert.Equal("nope-longer-than-prev", ChatService.NormalizeStreamDelta(previous, "nope-longer-than-prev", cumulative: true));
    }
}
