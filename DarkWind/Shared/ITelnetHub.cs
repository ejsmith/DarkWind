using System.Threading.Channels;

namespace DarkWind.Shared;

public interface ITelnetHub {
    Task<ChannelReader<string>> Connect(CancellationToken cancellationToken);
    Task Send(string data);
}

