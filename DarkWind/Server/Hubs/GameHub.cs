using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DarkWind.Server.Hubs;

public interface IGameHub
{
    IAsyncEnumerable<string> Connect(CancellationToken cancellationToken);
    Task Send(string data);
}

public class GameHub : Hub<IGameHub>
{
    private readonly ConcurrentDictionary<string, GameState> _connections = new();

    public ChannelReader<string> Connect(CancellationToken cancellationToken)
    {
        var channel = Channel.CreateUnbounded<string>();
        _connections.TryAdd(Context.ConnectionId, new GameState(channel));

        // We don't want to await WriteItemsAsync, otherwise we'd end up waiting 
        // for all the items to be written before returning the channel back to
        // the client.
        _ = ConnectToServer(channel.Writer, cancellationToken);

        return channel.Reader;
    }

    public async Task ConnectToServer(ChannelWriter<string> writer, CancellationToken cancellationToken)
    {
        // TODO: Create a telnet connection and redirect output to this stream

        Exception? localException = null;
        try
        {
            for (var i = 0; i < 1000; i++)
            {
                await writer.WriteAsync($"{i}\r\n", cancellationToken);

                // Use the cancellationToken in other APIs that accept cancellation
                // tokens so the cancellation can flow down to them.
                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (Exception ex)
        {
            localException = ex;
        }
        finally
        {
            writer.Complete(localException);
        }
    }

    public Task Send(string data)
    {
        if (!_connections.TryGetValue(Context.ConnectionId, out var gameState))
            return Task.CompletedTask;

        // TODO: Send this data as input to the telnet connection
        gameState.Channel.Writer.WriteAsync(data);
        
        return Task.CompletedTask;
    }

    class GameState
    {
        public GameState(Channel<string> channel)
        {
            Channel = channel;
        }

        public Channel<string> Channel { get; }
    }
}
