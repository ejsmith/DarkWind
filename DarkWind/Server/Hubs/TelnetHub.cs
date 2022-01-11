using DarkWind.Shared;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;
using System.Threading.Channels;

namespace DarkWind.Server.Hubs;

public class TelnetHub : Hub<ITelnetHub> {
    private static readonly ConcurrentDictionary<string, TelnetState> _connections = new();

    public ChannelReader<TelnetMessage> Connect(CancellationToken cancellationToken) {
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

    public Task SendGmcp(string data) {
        if (!_connections.TryGetValue(Context.ConnectionId, out var telnetState))
            return Task.CompletedTask;

        return telnetState.SendGmcp(data);
    }

    public override Task OnDisconnectedAsync(Exception? exception) {
        _connections.TryRemove(Context.ConnectionId, out _);
        return base.OnDisconnectedAsync(exception);
    }

    class TelnetState : IAsyncDisposable {
        private readonly CancellationToken _cancellationToken;
        private TelnetClient? _client;

        public TelnetState(CancellationToken cancellationToken) {
            _cancellationToken = cancellationToken;
            Channel = System.Threading.Channels.Channel.CreateUnbounded<TelnetMessage>();
        }

        public Channel<TelnetMessage> Channel { get; }

        public async Task Connect() {
            Exception? localException = null;
            try {
                _client = new TelnetClient();
                _client.AddOption(new TelnetOption {
                    Option = TelnetClient.KnownTelnetOptions.GMCP,
                    IsWanted = true,
                    Initialize = async (client) => {
                        await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Hello {\"Client\":\"DarkWind\",\"Version\":\"1.0.0\"}");
                        await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Supports.Set [ \"Char 1\", \"Char.Skills 1\", \"Char.Items 1\" ]");
                        await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Ping");
                    },
                });
                await _client.ConnectAsync("darkwind.org", 3000);

                do {
                    var data = await _client.Messages.ReadAsync();
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
            if (_client == null)
                return;

            await _client.WriteAsync(data);
        }

        public async Task SendGmcp(string data) {
            if (_client == null)
                return;

            await _client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, data);
        }

        public ValueTask DisposeAsync() {
            if (_client != null)
                return _client.DisposeAsync();

            return ValueTask.CompletedTask;
        }
    }
}
