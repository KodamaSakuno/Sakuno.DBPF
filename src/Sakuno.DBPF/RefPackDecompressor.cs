using System.Buffers.Binary;

namespace Sakuno.DBPF;

internal static class RefPackDecompressor
{
    private const int HeaderSize = 9;

    internal static byte[] Decompress(Stream input, uint expectedSize, Tgi identifier)
    {
        if (input.Length < HeaderSize)
            throw Error(identifier, $"the RefPack header is truncated: expected at least {HeaderSize} bytes, found {input.Length}");

        Span<byte> header = stackalloc byte[HeaderSize];
        try
        {
            input.ReadExactly(header);
        }
        catch (EndOfStreamException exception)
        {
            throw Error(identifier, "the RefPack header is truncated", exception);
        }

        var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (storedSize != input.Length)
            throw Error(identifier, $"the stored-size prefix is {storedSize}, but the resource contains {input.Length} bytes");

        if (header[4] != 0x10 || header[5] != 0xFB)
        {
            throw new NotSupportedException(
                $"Resource {identifier} does not use the supported SimCity 4 RefPack header 10 FB.");
        }

        var headerSize = (uint)(header[6] << 16 | header[7] << 8 | header[8]);
        if (headerSize != expectedSize)
            throw Error(identifier, $"the RefPack header declares {headerSize} output bytes, but the compression directory declares {expectedSize}");

        var outputSize = (int)expectedSize;
        ProcessCommands(input, null, outputSize, identifier);

        var output = new byte[outputSize];
        input.Position = HeaderSize;
        ProcessCommands(input, output, outputSize, identifier);
        return output;
    }

    private static void ProcessCommands(Stream input, byte[]? output, int outputSize, Tgi identifier)
    {
        var outputPosition = 0;
        while (true)
        {
            if (input.Position >= input.Length)
                throw Error(identifier, "the command stream ended without a final command");

            var commandOffset = input.Position;
            var b0 = (byte)input.ReadByte();
            if (b0 >= 0xFC)
            {
                CopyLiterals(input, output, ref outputPosition, outputSize, b0 & 3, identifier, commandOffset);
                if (outputPosition != outputSize)
                    throw Error(identifier, $"the final command produced {outputPosition} bytes, but {outputSize} were declared");
                if (input.Position != input.Length)
                    throw Error(identifier, $"{input.Length - input.Position} trailing bytes follow the final command");

                return;
            }

            int literalCount;
            int matchLength;
            int distance;
            if (b0 <= 0x7F)
            {
                var b1 = ReadCommandByte(input, identifier, commandOffset);
                literalCount = b0 & 3;
                matchLength = ((b0 >> 2) & 7) + 3;
                distance = ((b0 & 0x60) << 3) + b1 + 1;
            }
            else if (b0 <= 0xBF)
            {
                var b1 = ReadCommandByte(input, identifier, commandOffset);
                var b2 = ReadCommandByte(input, identifier, commandOffset);
                literalCount = (b1 >> 6) & 3;
                matchLength = (b0 & 0x3F) + 4;
                distance = ((b1 & 0x3F) << 8) + b2 + 1;
            }
            else if (b0 <= 0xDF)
            {
                var b1 = ReadCommandByte(input, identifier, commandOffset);
                var b2 = ReadCommandByte(input, identifier, commandOffset);
                var b3 = ReadCommandByte(input, identifier, commandOffset);
                literalCount = b0 & 3;
                matchLength = ((b0 & 0x0C) << 6) + b3 + 5;
                distance = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else
            {
                literalCount = ((b0 & 0x1F) << 2) + 4;
                CopyLiterals(input, output, ref outputPosition, outputSize, literalCount, identifier, commandOffset);
                continue;
            }

            CopyLiterals(input, output, ref outputPosition, outputSize, literalCount, identifier, commandOffset);
            CopyMatch(output, ref outputPosition, outputSize, matchLength, distance, identifier, commandOffset);
        }
    }

    private static byte ReadCommandByte(Stream input, Tgi identifier, long commandOffset)
    {
        var value = input.ReadByte();
        if (value < 0)
            throw Error(identifier, $"the command at input offset {commandOffset} has truncated command or literal bytes");

        return (byte)value;
    }

    private static void CopyLiterals(
        Stream input,
        byte[]? output,
        ref int outputPosition,
        int outputSize,
        int count,
        Tgi identifier,
        long commandOffset)
    {
        EnsureInput(input, count, identifier, commandOffset);
        EnsureOutput(outputPosition, outputSize, count, identifier, commandOffset);

        if (output is null)
        {
            input.Position += count;
        }
        else
        {
            input.ReadExactly(output.AsSpan(outputPosition, count));
        }

        outputPosition += count;
    }

    private static void CopyMatch(
        byte[]? output,
        ref int outputPosition,
        int outputSize,
        int count,
        int distance,
        Tgi identifier,
        long commandOffset)
    {
        if (distance > outputPosition)
        {
            throw Error(
                identifier,
                $"the command at input offset {commandOffset} refers {distance} bytes back with only {outputPosition} bytes available");
        }

        EnsureOutput(outputPosition, outputSize, count, identifier, commandOffset);
        if (output is null)
        {
            outputPosition += count;
            return;
        }

        for (var index = 0; index < count; index++, outputPosition++)
            output[outputPosition] = output[outputPosition - distance];
    }

    private static void EnsureInput(Stream input, int count, Tgi identifier, long commandOffset)
    {
        if (count > input.Length - input.Position)
            throw Error(identifier, $"the command at input offset {commandOffset} has truncated command or literal bytes");
    }

    private static void EnsureOutput(int position, int outputSize, int count, Tgi identifier, long commandOffset)
    {
        if (count > outputSize - position)
            throw Error(identifier, $"the command at input offset {commandOffset} exceeds the declared output size");
    }

    private static InvalidDataException Error(Tgi identifier, string message, Exception? innerException = null) =>
        new($"Cannot decompress resource {identifier}: {message}.", innerException);
}
