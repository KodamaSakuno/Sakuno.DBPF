using System.Globalization;

namespace Sakuno.DBPF.Cli;

internal static class TgiParser
{
    internal static Tgi Parse(string value)
    {
        var parts = value.Split('-');
        if (parts.Length != 3 || parts.Any(part => part.Length != 8))
            throw new FormatException("TGI must use the TTTTTTTT-GGGGGGGG-IIIIIIII hexadecimal format.");

        try
        {
            return new Tgi(
                uint.Parse(parts[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                uint.Parse(parts[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                uint.Parse(parts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
        catch (OverflowException exception)
        {
            throw new FormatException("TGI components must be unsigned 32-bit hexadecimal values.", exception);
        }
    }
}
