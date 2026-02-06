using System.Threading.Channels;
using PaymentModule.Application.Common.Interfaces;

namespace PaymentModule.Infrastructure.Services;

public class OutboxTrigger : IOutboxTrigger
{
    // Unbounded channel to act as a signal. We only care that *something* happened.
    private readonly Channel<object> _signalChannel = Channel.CreateBounded<object>(new BoundedChannelOptions(1)
    {
        FullMode = BoundedChannelFullMode.DropOldest
    });

    public void Trigger()
    {
        // Try to write to the channel. If it's full (worker hasn't picked up the last one yet),
        // that's fine, it means the worker is about to check anyway.
        _signalChannel.Writer.TryWrite(new object());
    }

    public async Task WaitForTriggerAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Wait for a signal to be available
            await _signalChannel.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException)
        {
            // Channel closed, stop waiting
        }
    }
}
