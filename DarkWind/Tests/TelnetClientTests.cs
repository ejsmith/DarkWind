using System;
using System.Threading;
using System.Threading.Tasks;
using DarkWind.Server.Hubs;
using DarkWind.Shared;
using Foundatio.Xunit;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace DarkWind.Tests {
    public class TelnetClientTests : TestWithLoggingBase {
        public TelnetClientTests(ITestOutputHelper output) : base(output) { }

        [Fact]
        public async Task CanConnect() {
            var client = new TelnetClient();
            client.AddOption(new TelnetOption {
                Option = TelnetClient.KnownTelnetOptions.GMCP,
                IsWanted = true,
                Initialize = async (client) => {
                    await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Hello {\"Client\":\"DarkWind\",\"Version\":\"1.0.0\"}");
                    await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Supports.Set [ \"Char 1\", \"Char.Skills 1\", \"Char.Items 1\" ]");
                    await client.SendSubCommandAsync(TelnetClient.KnownTelnetOptions.GMCP, "Core.Ping");
                }
            });

            await client.ConnectAsync("darkwind.org", 3000);

            var cts = new CancellationTokenSource();
            TelnetMessage message;
            do {
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                message = await client.Messages.ReadAsync();
                if (!cts.TryReset())
                    cts = new CancellationTokenSource();
                _logger.LogInformation(message.Message);
            } while (message.Message!.Contains("what name") == false);

            await client.WriteLineAsync("SuperZ");

            do {
                cts.CancelAfter(TimeSpan.FromSeconds(5));
                message = await client.Messages.ReadAsync();
                if (!cts.TryReset())
                    cts = new CancellationTokenSource();
                _logger.LogInformation(message.Message);
            } while (message.Message!.Contains("password") == false);

            await client.WriteLineAsync("banana");

            cts.CancelAfter(TimeSpan.FromMinutes(1));
            do {
                message = await client.Messages.ReadAsync(cts.Token);
                _logger.LogInformation(message.Message);
            } while (true);
        }
    }
}