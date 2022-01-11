using System.Threading.Channels;

namespace DarkWind.Shared;

public interface ITelnetHub {
    Task<ChannelReader<TelnetMessage>> Connect(CancellationToken cancellationToken);
    Task Send(string data);
    Task SendGmcp(string data);
}

public class TelnetMessage {
    public byte Option { get; set; } = KnownOptions.Echo;
    public string? Message { get; set; }

    public class KnownOptions {
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
