using DarkWind.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DarkWind.Server.Hubs;

public class TelnetHub : Hub<ITelnetHub> {
    private static readonly ConcurrentDictionary<string, TelnetState> _connections = new();

    public ChannelReader<string> Connect(CancellationToken cancellationToken) {
        var telnetState = new TelnetState(cancellationToken);
        _connections.TryAdd(Context.ConnectionId, telnetState);

        _ = telnetState.Connect();

        return telnetState.Channel.Reader;
    }

    public Task Send(string data) {
        if (!_connections.TryGetValue(Context.ConnectionId, out var telnetState))
            return Task.CompletedTask;

        return telnetState.Send(data);
    }

    public override Task OnDisconnectedAsync(Exception? exception) {
        _connections.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    class TelnetState : IDisposable {
        private readonly CancellationToken _cancellationToken;
        private PrimS.Telnet.Client? _client;

        public TelnetState(CancellationToken cancellationToken) {
            _cancellationToken = cancellationToken;
            Channel = System.Threading.Channels.Channel.CreateUnbounded<string>();
        }

        public Channel<string> Channel { get; }

        public async Task Connect() {
            Exception? localException = null;
            try {
                _client = new PrimS.Telnet.Client("darkwind.org", 3000, _cancellationToken);
                if (!_client.IsConnected)
                    return;

                do {
                    var data = await _client.ReadAsync();
                    if (data != null)
                        await Channel.Writer.WriteAsync(data);
                }
                while (true);
            }
            catch (Exception ex) {
                localException = ex;
            }
            finally {
                Channel.Writer.Complete(localException);
            }
        }

        public async Task Send(string data) {
            await _client!.Write(data);
        }

        public void Dispose() {
            _client?.Dispose();
        }
    }
}
