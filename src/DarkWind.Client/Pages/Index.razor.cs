using System.Text.Json;
using DarkWind.Client.Hubs;
using DarkWind.Shared;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using XtermBlazor;

namespace DarkWind.Client.Pages;

public partial class Index 
{
    int health = 0;
    int maxHealth = 0;
    int spellpoints = 0;
    int maxSpellpoints = 0;
    int healthPercent = 100;
    int magicPercent = 100;
    string enemyName = "Enemy";
    int enemyHealth = 0;
    int enemyMaxHealth = 0;
    int enemyHealthPercent = 100;
    string enemyHealthDescription = "";


    private Xterm? _terminal;
    private Xterm? _gmcp;
    private string _input = String.Empty;
    private CancellationTokenSource cts = new CancellationTokenSource();

    [Inject] TelnetHub Hub { get; set; }
    [Inject] IJSRuntime Javascript { get; set; }

    private Task HandleGmcpMessage(string command, string data) 
    {
        switch (command) 
        {
            case "Char.Vitals":
                var vitals = JsonSerializer.Deserialize<CharVitals>(data);
                if (vitals == null) return Task.CompletedTask;

                health = vitals.Health;
                maxHealth = vitals.MaxHealth;
                spellpoints = vitals.Spellpoints;
                maxSpellpoints = vitals.MaxSpellpoints;

                healthPercent = (int)(((float)health / (float)maxHealth) * 100f);
                magicPercent = (int)(((float)spellpoints / (float)maxSpellpoints) * 100f);


                StateHasChanged();

                break;
            case "Char.Enemy":
                var enemy = JsonSerializer.Deserialize<CharEnemy>(data);
                if (enemy == null) return Task.CompletedTask;

                enemyName = enemy.EnemyName;
                enemyHealth = enemy.EnemyCurrentHealth;
                enemyMaxHealth = enemy.EnemyMaxHealth;
                enemyHealthDescription = enemy.EnemyHealthDescription;

                enemyHealthPercent = (int)(((float)enemyHealth / (float)enemyMaxHealth) * 100f);

                break;
        }
        
        return Task.CompletedTask;
    }

    private async Task SendCommand(string command) 
    {
        await Hub.Send(command + '\r');
        await _terminal!.Write(command + "\r\n");
        await Javascript.InvokeVoidAsync("selectText", "input");
    }

    private async Task SendGmcpCommand(string command) 
    {
        if (String.IsNullOrEmpty(command)) return;

        await Hub.SendGmcp(command);
    }

    private Task SendCommand() 
    {
        if (String.IsNullOrEmpty(_input)) return Task.CompletedTask;

        return SendCommand(_input);
    }


    private async Task OnFirstRender() 
    {
        await Javascript.InvokeVoidAsync("selectText", "input");

        await Hub.Start();

        var channel = await Hub.Connect(cts.Token);

        while (await channel.WaitToReadAsync()) 
        {
            while (channel.TryRead(out var message)) 
            {
                if (message.Option == TelnetMessage.KnownOptions.Echo) 
                {
                    await _terminal!.Write(message.Data);
                }
                else if (message.Option == TelnetMessage.KnownOptions.GMCP) 
                {
                    if (String.IsNullOrEmpty(message.Data)) continue;

                    await _gmcp!.WriteLine(message.Data);

                    var firstSpace = message.Data.IndexOf(' ');
                    var command = firstSpace > 0 ? message.Data.Substring(0, firstSpace) : message.Data;
                    var gmcpData = firstSpace > 0 && message.Data.Length > firstSpace ? message.Data.Substring(firstSpace) : String.Empty;

                    try 
                    {
                        await HandleGmcpMessage(command, gmcpData);
                    }
                    catch (Exception ex) 
                    {
                        await _gmcp!.WriteLine("ERR: " + ex.Message);
                    }
                }
            }
        }
    }

    private TerminalOptions _options = new TerminalOptions 
    {
        Rows = 30,
        CursorStyle = CursorStyle.Bar
    };

    private TerminalOptions _gmcpOptions = new TerminalOptions 
    {
        Rows = 10,
        CursorStyle = CursorStyle.Bar
    };
}
