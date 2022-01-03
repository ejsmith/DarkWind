using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Components;
using System.Threading.Channels;
using DarkWind.Shared;

namespace DarkWind.Client.Hubs;

public class TelnetHub : IAsyncDisposable, ITelnetHub {
    protected bool _started = false;
    protected HubConnection _connection;

    public TelnetHub(NavigationManager navigationManager) {
        _connection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/telnethub"))
            .WithAutomaticReconnect()
            .Build();
    }

    public bool IsConnected => _connection.State == HubConnectionState.Connected;

    public async Task Start() {
        if (!_started) {
            await _connection.StartAsync();
            _started = true;
        }
    }

    public async Task<ChannelReader<string>> Connect(CancellationToken cancellationToken) {
        var channel = await _connection.StreamAsChannelAsync<string>("Connect", cancellationToken);
        return channel;
    }

    public Task Send(string data) {
        return _connection.InvokeAsync("Send", data);
    }

    public async ValueTask DisposeAsync() {
        if (_connection != null)
            await _connection.DisposeAsync();
    }
}