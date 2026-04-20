using System.Threading.Channels;
using Grpc.Core;
using Grpc.Net.Client;
using Stove.Dashboard.V1;

namespace Stove.Net.Dashboard.Internal;

/// <summary>
/// Sends DashboardEvents to the Stove UI via gRPC client-streaming (StreamEvents RPC).
/// Events are queued on an unbounded channel and drained by a background Task.
/// Auto-disables after MaxConsecutiveFailures consecutive send errors to prevent test noise.
/// </summary>
internal sealed class DashboardEmitter : IAsyncDisposable
{
    private readonly DashboardSystemOptions _options;
    private readonly Channel<DashboardEvent> _channel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _drainTask;
    private GrpcChannel? _grpcChannel;

    private int _consecutiveFailures;
    private bool _disabled;

    internal DashboardEmitter(DashboardSystemOptions options)
    {
        _options = options;
        _channel = System.Threading.Channels.Channel.CreateUnbounded<DashboardEvent>(
            new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

        _drainTask = Task.Run(DrainLoop);
    }

    /// <summary>Queue an event for async delivery. Returns immediately.</summary>
    internal void Enqueue(DashboardEvent evt)
    {
        if (_disabled) return;
        _channel.Writer.TryWrite(evt);
    }

    /// <summary>
    /// Drain remaining events and close the gRPC connection.
    /// Waits up to DrainTimeout for in-flight events to be sent.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _channel.Writer.TryComplete();

        using var drainCts = new CancellationTokenSource(_options.DrainTimeout);
        try
        {
            await _drainTask.WaitAsync(drainCts.Token);
        }
        catch (OperationCanceledException) { /* timeout — abandon remaining events */ }
        catch (Exception) { /* ignore drain errors on shutdown */ }

        await _cts.CancelAsync();
        _cts.Dispose();

        if (_grpcChannel != null)
        {
            await _grpcChannel.ShutdownAsync();
            _grpcChannel.Dispose();
        }
    }

    private async Task DrainLoop()
    {
        var reader = _channel.Reader;

        while (await reader.WaitToReadAsync(_cts.Token).ConfigureAwait(false))
        {
            while (reader.TryRead(out var evt))
            {
                if (_disabled) continue;
                await TrySendAsync(evt);
            }
        }
    }

    private async Task TrySendAsync(DashboardEvent evt)
    {
        try
        {
            var client = GetClient();
            await client.SendEventAsync(evt, deadline: DateTime.UtcNow.AddSeconds(5));
            _consecutiveFailures = 0;
        }
        catch (Exception)
        {
            _consecutiveFailures++;
            if (_consecutiveFailures >= _options.MaxConsecutiveFailures)
            {
                _disabled = true;
                Console.WriteLine(
                    $"[STOVE] Dashboard emitter disabled after {_consecutiveFailures} consecutive failures. " +
                    $"Is the dashboard running at {_options.Address}?");
            }
        }
    }

    private DashboardEventService.DashboardEventServiceClient GetClient()
    {
        if (_grpcChannel == null)
        {
            _grpcChannel = GrpcChannel.ForAddress(_options.Address, new GrpcChannelOptions
            {
                Credentials = ChannelCredentials.Insecure
            });
        }

        return new DashboardEventService.DashboardEventServiceClient(_grpcChannel);
    }
}
