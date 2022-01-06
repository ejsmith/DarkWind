using System.Text;
using System.Net.Sockets;
using System.Threading.Channels;
using System.IO.Pipelines;

namespace DarkWind.Server.Hubs;

public class TelnetClient {
    private TcpClient? _tcpClient;
    private NetworkStream? _stream;
    private PipeReader? _reader;
    private PipeWriter? _writer;
    private Channel<string>? _channel;
    private Task? _readerTask;
    private CancellationTokenSource? _cancellationTokenSource;

    public bool IsConnected => _tcpClient != null && _stream != null && _tcpClient.Connected;

    public Task ConnectAsync(string host, int port) {
        if (IsConnected)
            return Task.CompletedTask;

        _tcpClient = new TcpClient(host, port);

        return ConnectAsync(_tcpClient.GetStream());
    }

    public async Task ConnectAsync(Stream stream) {
        if (IsConnected)
            return;

        _cancellationTokenSource = new();

        _reader = PipeReader.Create(stream);
        _writer = PipeWriter.Create(stream);

        await NegotiateOptionsAsync();
    }

    public ValueTask SendCommandAsync(TelnetCommand command, TelnetOption option) {
        return SendCommandAsync((byte)command, (byte)option);
    }

    public async ValueTask SendCommandAsync(byte command, byte option) {
        if (!IsConnected) return;

        await _stream!.WriteAsync(new byte[] { (byte)TelnetCommand.IAC, command, option });
    }

    public async ValueTask SendSubCommandAsync(byte option, string data) {
        if (!IsConnected) return;
        
        await _stream!.WriteAsync(new byte[] { (byte)TelnetCommand.IAC, (byte)TelnetCommand.SB, option });
        await WriteAsync(data);
        await _stream!.WriteAsync(new byte[] { (byte)TelnetCommand.IAC, (byte)TelnetCommand.SE, option });
    }

    public async ValueTask SendSubCommandAsync(byte option, byte[] data) {
        if (!IsConnected) return;

        await _stream!.WriteAsync(new byte[] { (byte)TelnetCommand.IAC, (byte)TelnetCommand.SB, option });
        await WriteAsync(data);
        await _stream!.WriteAsync(new byte[] { (byte)TelnetCommand.IAC, (byte)TelnetCommand.SE, option });
    }

    public ValueTask WriteLineAsync(string data) {
        return WriteAsync(data + '\n');
    }

    public async ValueTask WriteAsync(string data) {
        if (!IsConnected) return;

        var buffer = Encoding.ASCII.GetBytes(data.Replace("\0xFF", "\0xFF\0xFF"));
        await _stream!.WriteAsync(buffer);
    }

    public ValueTask WriteAsync(byte[] data) {
        return WriteAsync(data, 0, data.Length);
    }

    public async ValueTask WriteAsync(byte[] buffer, int offset, int count) {
        if (!IsConnected) return;

        await _stream!.WriteAsync(buffer, offset, count);
    }

    enum ParseState {
        Normal,
        IAC,
        Neg,
        Sub
    }

    private ParseState _state = ParseState.Normal;

    public async Task<string?> ReadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) {
        if (!IsConnected) return null;

        timeout ??= TimeSpan.FromMilliseconds(2000);
        
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_cancellationTokenSource!.Token, cancellationToken);
        var readTimeout = new CancellationTokenSource();
        var sb = new StringBuilder();

        while (!cts.Token.IsCancellationRequested) {
            readTimeout.CancelAfter(timeout.Value);

            var cts2 = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, readTimeout.Token);
            
            // TODO: Handle read timeout
            var result = await _reader!.ReadAsync(cts2.Token).ConfigureAwait(false);
            if (result.IsCanceled)
                break;
            
            var buffer = result.Buffer;
            var position = buffer.Start;

            while (buffer.TryGet(ref position, out var memory)) {
                byte current;
                for (int i = 0; i < memory.Span.Length; i++) {
                    current = memory.Span[i];

                    if (current == (byte)TelnetCommand.IAC) {
                        // if we don't have at least 2 more characters then use AdvanceTo without examining
                        // interpret as command
                        if (memory.Span.Length < i + 2) {
                            // TODO: AdvanceTo without examining
                            break;
                        }

                        i++;
                        var inputVerb = memory.Span[i];
                        i++;
                        var inputOption = memory.Span[i];

                        switch (inputVerb) {
                            case (byte)TelnetCommand.IAC:
                                // literal IAC = 255 escaped, so append char 255 to string
                                sb.Append((char)inputVerb);
                                break;
                            case (byte)TelnetCommand.DO:
                            case (byte)TelnetCommand.DONT:
                            case (byte)TelnetCommand.WILL:
                            case (byte)TelnetCommand.WONT:
                                // TODO: Use options array to control what we want to happen and keep state of what the negotiated result is
                                await SendCommandAsync((byte)TelnetCommand.WONT, inputVerb);
                                break;
                            default:
                                break;
                        }
                    } else {
                        AppendValue(current, sb);
                    }
                }
            }

            // Required to signal an end of the read operation.
            _reader.AdvanceTo(buffer.End);

            // Stop reading if there's no more data coming.
            if (result.IsCompleted)
                break;
        }

        return sb.ToString();
    }

    private void Abort() { }

    public ChannelReader<string> Reader {
        get {
            if (_channel == null) {
                _channel = Channel.CreateUnbounded<string>();
                _readerTask = StartReading(_cancellationTokenSource!.Token);
            }

            return _channel.Reader;
        }
    }

    private async Task StartReading(CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested) {
                var result = await ReadAsync(TimeSpan.FromMilliseconds(100), cancellationToken);
                if (result == null)
                    continue;

                await _channel!.Writer.WriteAsync(result, cancellationToken).ConfigureAwait(false);
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
        catch (Exception ex) {
            Abort();
        }
        finally {
            _reader?.Complete();
        }
    }

    private Task NegotiateOptionsAsync() {
        return Task.CompletedTask;
    }

    private void AppendValue(byte value, StringBuilder sb) {
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
                break;
            case 21:
                sb.Append("NAK: Retransmit last message.");
                break;
            case 31: // Unit Separator
                sb.Append(',');
                break;
            default:
                sb.Append((char)value);
                break;
        }
    }
    
    public enum TelnetCommand : byte {
        SE = 240,   // End of subnegotiation parameters.
        NOP = 241,  // No operation.
        DM = 242,   // Data Mark (part of the Synch function). Indicates the position of a Synch event within the data stream.
        BRK = 243,  // NYT character break.
        IP = 244,   // Suspend, interrupt or abort the process to which the NVT is connected.
        AO = 245,   // Abort Output. Allows the current process to run to completion but do not send its output to the user.
        AYT = 246,  // Are you there? AYT is used to determine if the remote TELNET partner is still up and running.
        EC = 247,   // Erase character. Erase character is used to indicate the receiver should delete the last preceding undeleted character from the data stream.
        EL = 248,   // Erase line. Delete characters from the data stream back to but not including the previous CRLF.
        GA = 249,   // Go ahead. Go ahead is used in half-duplex mode to indicate the other end that it can transmit.
        SB = 250,   // Begin of subnegotiation
        WILL = 251, // The sender wants to enable an option
        WONT = 252, // The sender do not wants to enable an option.
        DO = 253,   // Sender asks receiver to enable an option.
        DONT = 254, // Sender asks receiver not to enable an option.
        IAC = 255,  // IAC (Interpret as Command)
    }

    public enum TelnetOption: byte {
        Echo = 1,
        SuppressGoAhead = 3,
        Status = 5,
        TimingMark = 6,
        OutputLineWidth = 8,
        OutputPageSize = 9,
        OutputCarriageReturnDisposition = 10,
        OutputHorizontalTabstops = 11,
        OutputHorizontalTabDisposition = 12,
        OutputVerticalTabstops = 14,
        OutputVerticalTabDisposition = 15,
        Logout = 18,
        TerminalType = 24,
        EndOfRecord = 25,
        UserIdentification = 26,
        WindowSize = 31,
        TerminalSpeed = 32,
        RemoteFlowControl = 33,
        Linemode = 34,
        XDisplayLocation = 35,
        EnvironmentVariables = 36,
        TelnetEnvironmentOption = 39,
        GMCP = 201
    }
}
