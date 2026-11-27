using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace DeadCellsMultiplayerMod;

internal static class LanRoomCode
{
    internal const int DefaultPort = 1234;
    private const string Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    internal static string Format(IPAddress address, int port)
    {
        var bytes = address.GetAddressBytes();
        uint value = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var code = string.Empty;
        do
        {
            code = Digits[(int)(value % 36)] + code;
            value /= 36;
        } while (value != 0);

        return port == DefaultPort ? code : FormattableString.Invariant($"{code}:{port}");
    }

    internal static bool TryParse(string? input, out IPEndPoint endpoint)
    {
        endpoint = null!;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        var addressText = input.Trim();
        if (addressText.StartsWith("DC-", StringComparison.OrdinalIgnoreCase))
            addressText = addressText[3..];

        var port = DefaultPort;
        var colon = addressText.IndexOf(':');
        if (colon >= 0)
        {
            if (!int.TryParse(addressText.AsSpan(colon + 1), NumberStyles.None, CultureInfo.InvariantCulture, out port))
                return false;
            addressText = addressText[..colon];
        }
        if (port is < 1 or > 65535)
            return false;

        if (addressText.Contains('.'))
        {
            if (!IPAddress.TryParse(addressText, out var address) || address.AddressFamily != AddressFamily.InterNetwork)
                return false;
            endpoint = new IPEndPoint(address, port);
            return true;
        }

        if (addressText.Length is < 1 or > 7)
            return false;

        uint value = 0;
        foreach (var c in addressText)
        {
            var digit = Digits.IndexOf(char.ToUpperInvariant(c));
            if (digit < 0 || value > (uint.MaxValue - (uint)digit) / 36)
                return false;
            value = value * 36 + (uint)digit;
        }

        endpoint = new IPEndPoint(new IPAddress([(byte)(value >> 24), (byte)(value >> 16), (byte)(value >> 8), (byte)value]), port);
        return true;
    }
}
