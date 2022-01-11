using System.Text;
using System.Net.Sockets;
using System.Threading.Channels;
using System.IO.Pipelines;
using System.Diagnostics;
using DarkWind.Shared;

namespace DarkWind.Server.Hubs;

public class TelnetClient : IAsyncDisposable {
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private Channel<TelnetMessage>? _channel;
    private Task? _readerTask;
    private CancellationTokenSource? _cancellationTokenSource;
    private readonly List<ITelnetOption> _options = new();

    public bool IsConnected { get; private set; }
    public ChannelReader<TelnetMessage> Messages => _channel!.Reader;
    public IReadOnlyCollection<ITelnetOption> Options => _options;

    public Task ConnectAsync(string host, int port) {
        if (IsConnected)
            return Task.CompletedTask;

        var tcpClient = new TcpClient(host, port);
        IsConnected = tcpClient.Connected;

        return ConnectAsync(tcpClient.GetStream());
    }

    public Task ConnectAsync(Stream stream) {
        _cancellationTokenSource = new();
        IsConnected = true;

        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);

        _channel = Channel.CreateBounded<TelnetMessage>(new BoundedChannelOptions(25) {
            FullMode = BoundedChannelFullMode.Wait
        });
        _readerTask = StartReading(_cancellationTokenSource.Token);

        return Task.CompletedTask;
    }

    public void AddOption(ITelnetOption option) {
        _options.Add(option);
    }

    private async Task StartReading(CancellationToken cancellationToken) {
        try {
            var sb = new StringBuilder();
            ReadResult result;
            SequencePosition examined;
            byte subNegotiationOption = 0;

            while (!cancellationToken.IsCancellationRequested) {
                result = await _reader!.ReadAsync(cancellationToken).ConfigureAwait(false);
                if (result.IsCanceled)
                    break;

                var buffer = result.Buffer;
                var position = buffer.Start;
                examined = buffer.End;

                Debug.Assert(buffer.IsSingleSegment);

                foreach (var segment in buffer) {
                    // TODO: Remove this
                    var debugText = Encoding.ASCII.GetString(segment.Span);

                    byte current;
                    for (int i = 0; i < segment.Span.Length; i++) {
                        current = segment.Span[i];

                        if (current == TelnetCommand.IAC) {
                            // if we don't have at least 2 more characters then use AdvanceTo without examining
                            // interpret as command
                            if (segment.Span.Length < i + 2) {
                                // TODO: AdvanceTo without examining
                                break;
                            }

                            i++;
                            var inputVerb = segment.Span[i];
                            byte inputOption = 0;
                            if (inputVerb != TelnetCommand.SE) {
                                i++;
                                inputOption = segment.Span[i];
                            } else {
                                inputOption = subNegotiationOption;
                            }

                            var option = _options.FirstOrDefault(o => o.Option == inputOption);
                            if (option == null) {
                                option = new TelnetOption { Option = inputOption };

                                // we want this option
                                if (option.Option == KnownTelnetOptions.SuppressGoAhead)
                                    option.IsWanted = true;

                                _options.Add(option);
                            }

                            switch (inputVerb) {
                                case TelnetCommand.IAC:
                                    // literal IAC = 255 escaped, so append char 255 to string
                                    sb.Append((char)inputVerb);
                                    continue;
                                case TelnetCommand.DO:
                                    // check to see if client has preference
                                    if (option.IsWanted.HasValue && option.IsWanted.Value) {
                                        // client wants option
                                        if (option.NegotiatedValue.HasValue == false || option.NegotiatedValue.Value == false) {
                                            await SendCommandAsync(TelnetCommand.WILL, inputOption);
                                            option.NegotiatedValue = true;
                                            if (option?.Initialize != null)
                                                await option.Initialize(this);
                                        }
                                    } else if (option.IsWanted.HasValue && option.IsWanted.Value == false) {
                                        // client doesn't want option
                                        if (option.NegotiatedValue.HasValue == false || option.NegotiatedValue.Value == true) {
                                            await SendCommandAsync(TelnetCommand.WONT, inputOption);
                                            option.NegotiatedValue = false;
                                        }
                                    }
                                    else {
                                        // client has no preference
                                        await SendCommandAsync(TelnetCommand.WONT, inputOption); ;
                                        option.NegotiatedValue = false;
                                    }
                                    break;
                                case TelnetCommand.DONT:
                                    // if we had a different value or haven't seen the option, then acknowledge
                                    if (option.NegotiatedValue.HasValue == false || (option.NegotiatedValue.HasValue && option.NegotiatedValue.Value == true))
                                        await SendCommandAsync(TelnetCommand.WONT, inputOption);

                                    option.NegotiatedValue = false;
                                    break;
                                case TelnetCommand.WONT:
                                    // if we had a different value or haven't seen the option, then acknowledge
                                    if (option.NegotiatedValue.HasValue == false || (option.NegotiatedValue.HasValue && option.NegotiatedValue.Value == true))
                                        await SendCommandAsync(TelnetCommand.DONT, inputOption);

                                    option.NegotiatedValue = false;
                                    break;
                                case TelnetCommand.WILL:
                                    // check to see if client has preference
                                    if (option.IsWanted.HasValue && option.IsWanted.Value) {
                                        // client wants option
                                        if (option.NegotiatedValue.HasValue == false || option.NegotiatedValue.Value == false) {
                                            await SendCommandAsync(TelnetCommand.DO, inputOption);
                                            option.NegotiatedValue = true;
                                            if (option?.Initialize != null)
                                                await option.Initialize(this);
                                        }
                                    } else if (option.IsWanted.HasValue && option.IsWanted.Value == false) {
                                        // client doesn't want option
                                        if (option.NegotiatedValue.HasValue == false || option.NegotiatedValue.Value == true) {
                                            await SendCommandAsync(TelnetCommand.DONT, inputOption);
                                            option.NegotiatedValue = false;
                                        }
                                    } else {
                                        // client has no preference
                                        await SendCommandAsync(TelnetCommand.DONT, inputOption); ;
                                        option.NegotiatedValue = false;
                                    }
                                    break;
                                case TelnetCommand.SB:
                                    subNegotiationOption = inputOption;
                                    break;
                                case TelnetCommand.SE:
                                    await _channel!.Writer.WriteAsync(new TelnetMessage { Option = inputOption, Message = sb.ToString() }, cancellationToken).ConfigureAwait(false);
                                    sb.Clear();
                                    subNegotiationOption = 0;
                                    break;
                                default:
                                    break;
                            }

                            if (sb.Length > 0 && subNegotiationOption == 0) {
                                await _channel!.Writer.WriteAsync(new TelnetMessage { Message = sb.ToString() }, cancellationToken).ConfigureAwait(false);
                                sb.Clear();
                            }
                        } else {
                            if (AppendValue(current, sb) || i == segment.Span.Length - 1) {
                                if (subNegotiationOption == 0) {
                                    await _channel!.Writer.WriteAsync(new TelnetMessage { Message = sb.ToString() }, cancellationToken).ConfigureAwait(false);
                                    sb.Clear();
                                }
                            }
                        }
                    }
                }

                // Required to signal an end of the read operation.
                _reader.AdvanceTo(buffer.End, examined);

                // Stop reading if there's no more data coming.
                if (result.IsCompleted)
                    break;
            }
        }
        catch (Exception ex) when (ex is IOException && !cancellationToken.IsCancellationRequested) {
            Abort();
        }
        catch (Exception ex) when (ex is IOException) {
            Abort();
        }
        catch (Exception ex) when (ex is OperationCanceledException) {
            // We're getting here because someone called StopAsync on the connection.
            // Reasons might be:
            // - Server detected a closed connection in another part of the communication stack
            // - QUIT command
        }
        catch (Exception) {
            Abort();
        }
        finally {
            _reader?.Complete();
        }
    }

    public async ValueTask SendCommandAsync(byte command, byte option) {
        if (!IsConnected) return;

        await _writer!.WriteAsync(new byte[] { TelnetCommand.IAC, command, option }, _cancellationTokenSource!.Token);
    }

    public async ValueTask SendSubCommandAsync(byte option, string data) {
        if (!IsConnected) return;

        await _writer!.WriteAsync(new[] { TelnetCommand.IAC, TelnetCommand.SB, option }, _cancellationTokenSource!.Token);
        await _writer!.WriteAsync(Encoding.ASCII.GetBytes(data), _cancellationTokenSource!.Token);
        await _writer!.WriteAsync(new[] { TelnetCommand.IAC, TelnetCommand.SE }, _cancellationTokenSource!.Token);
    }

    public ValueTask WriteLineAsync(string data) {
        return WriteAsync(data + "\r\n");
    }

    public async ValueTask WriteAsync(string data) {
        if (!IsConnected) return;

        var buffer = Encoding.ASCII.GetBytes(data.Replace("\0xFF", "\0xFF\0xFF"));
        await _writer!.WriteAsync(buffer, _cancellationTokenSource!.Token);
    }

    public async ValueTask WriteAsync(ReadOnlyMemory<byte> data) {
        if (!IsConnected) return;

        await _writer!.WriteAsync(data, _cancellationTokenSource!.Token);
    }

    private void Abort() {
        _cancellationTokenSource?.Cancel();
    }

    private bool AppendValue(byte value, StringBuilder sb) {
        switch (value) {
            case 0: // NULL
                break;
            case 1: // Start of Heading
                sb.Append("\n \n");
                break;
            case 2: // Start of Text
                sb.Append('\t');
                break;
            case 3: // End of Text or "break" CTRL+C
                sb.Append("^C");
                System.Diagnostics.Debug.WriteLine("^C");
                break;
            case 4: // End of Transmission
                sb.Append("^D");
                break;
            case 5: // Enquiry
                //await _writer.WriteAsync(new byte[] { 6 }); // Send ACK
                break;
            case 6: // Acknowledge
                // We got an ACK
                break;
            case 7: // Bell character
                //Console.Beep();
                break;
            case 8: // Backspace
                    // We could delete a character from sb, or just swallow the char here.
                break;
            case 11: // Vertical TAB
            case 12: // Form Feed
                sb.Append('\n');
                return true;
            case 21:
                sb.Append("NAK: Retransmit last message.");
                break;
            case 31: // Unit Separator
                sb.Append(',');
                break;
            default:
                sb.Append((char)value);
                if (value == '\n')
                    return true;

                break;
        }

        return false;
    }

    public async ValueTask DisposeAsync() {
        _cancellationTokenSource?.Cancel();
        if (_readerTask != null)
            await _readerTask;
    }

    public class TelnetCommand {
        /// <summary>
        /// End of subnegotiation parameters
        /// </summary>
        public const byte SE = 240;

        /// <summary>
        /// No operation
        /// </summary>
        public const byte NOP = 241;

        /// <summary>
        /// Data Mark (part of the Synch function). Indicates the position of a Synch event within the data stream.
        /// </summary>
        public const byte DM = 242;

        /// <summary>
        /// NYT character break.
        /// </summary>
        public const byte BRK = 243;

        /// <summary>
        /// ESuspend, interrupt or abort the process to which the NVT is connected.
        /// </summary>
        public const byte IP = 244;

        /// <summary>
        /// Abort Output. Allows the current process to run to completion but do not send its output to the user.
        /// </summary>
        public const byte AO = 245;

        /// <summary>
        /// Are you there? AYT is used to determine if the remote TELNET partner is still up and running.
        /// </summary>
        public const byte AYT = 246;

        /// <summary>
        /// Erase character. Erase character is used to indicate the receiver should delete the last preceding undeleted character from the data stream.
        /// </summary>
        public const byte EC = 247;

        /// <summary>
        /// Erase line. Delete characters from the data stream back to but not including the previous CRLF.
        /// </summary>
        public const byte EL = 248;

        /// <summary>
        /// Go ahead. Go ahead is used in half-duplex mode to indicate the other end that it can transmit.
        /// </summary>
        public const byte GA = 249;

        /// <summary>
        /// Begin of subnegotiation
        /// </summary>
        public const byte SB = 250;

        /// <summary>
        /// The sender wants to enable an option
        /// </summary>
        public const byte WILL = 251;

        /// <summary>
        /// The sender do not wants to enable an option.
        /// </summary>
        public const byte WONT = 252;

        /// <summary>
        /// Sender asks receiver to enable an option.
        /// </summary>
        public const byte DO = 253;

        /// <summary>
        /// Sender asks receiver not to enable an option.
        /// </summary>
        public const byte DONT = 254;

        /// <summary>
        /// IAC (Interpret as Command)
        /// </summary>
        public const byte IAC = 255;
    }

    public class KnownTelnetOptions {
        public const byte Echo = 1;
        public const byte SuppressGoAhead = 3;
        public const byte Status = 5;
        public const byte TimingMark = 6;
        public const byte OutputLineWidth = 8;
        public const byte OutputPageSize = 9;
        public const byte OutputCarriageReturnDisposition = 10;
        public const byte OutputHorizontalTabstops = 11;
        public const byte OutputHorizontalTabDisposition = 12;
        public const byte OutputVerticalTabstops = 14;
        public const byte OutputVerticalTabDisposition = 15;
        public const byte Logout = 18;
        public const byte TerminalType = 24;
        public const byte EndOfRecord = 25;
        public const byte UserIdentification = 26;
        public const byte WindowSize = 31;
        public const byte TerminalSpeed = 32;
        public const byte RemoteFlowControl = 33;
        public const byte Linemode = 34;
        public const byte XDisplayLocation = 35;
        public const byte EnvironmentVariables = 36;
        public const byte TelnetEnvironmentOption = 39;
        public const byte GMCP = 201;
        public const byte MSSP = 70;

        public static string GetOptionName(byte option) {
            return option switch {
                Echo => "Echo",
                SuppressGoAhead => "SuppressGoAhead",
                Status => "Status",
                TimingMark => "TimingMark",
                OutputLineWidth => "OutputLineWidth",
                OutputPageSize => "OutputPageSize",
                OutputCarriageReturnDisposition => "OutputCarriageReturnDisposition",
                OutputHorizontalTabstops => "OutputHorizontalTabstops",
                OutputHorizontalTabDisposition => "OutputHorizontalTabDisposition",
                OutputVerticalTabstops => "OutputVerticalTabstops",
                OutputVerticalTabDisposition => "OutputVerticalTabDisposition",
                Logout => "Logout",
                TerminalType => "TerminalType",
                EndOfRecord => "EndOfRecord",
                UserIdentification => "UserIdentification",
                WindowSize => "WindowSize",
                TerminalSpeed => "TerminalSpeed",
                RemoteFlowControl => "RemoteFlowControl",
                Linemode => "Linemode",
                XDisplayLocation => "XDisplayLocation",
                EnvironmentVariables => "EnvironmentVariables",
                TelnetEnvironmentOption => "TelnetEnvironmentOption",
                GMCP => "GMCP",
                MSSP => "MSSP",
                _ => String.Empty,
            };
        }
    }
}

public interface ITelnetOption {
    byte Option { get; }
    string OptionName { get; }
    bool? IsWanted { get; internal set; }
    bool? NegotiatedValue { get; internal set; }
    Func<TelnetClient, Task>? Initialize { get; }
}

public class TelnetOption : ITelnetOption {
    public byte Option { get; set; }
    public string OptionName => TelnetClient.KnownTelnetOptions.GetOptionName(Option);
    public bool? IsWanted { get; set; }
    public bool? NegotiatedValue { get; set; }
    public Func<TelnetClient, Task>? Initialize { get; set; }
}
