using System.Threading.Channels;
using ContextMemory.Core.Contracts;

namespace ContextMemory.Core.Session;

public sealed class WikiUpdateQueue : IWikiUpdateQueue
{
    private readonly Channel<WikiUpdateJob> _channel = Channel.CreateBounded<WikiUpdateJob>(
        new BoundedChannelOptions(512)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    public ChannelReader<WikiUpdateJob> Reader => _channel.Reader;

    public ValueTask EnqueueAsync(WikiUpdateJob job, CancellationToken cancellationToken = default) =>
        _channel.Writer.WriteAsync(job, cancellationToken);
}
