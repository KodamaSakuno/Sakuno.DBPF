using System.Buffers.Binary;

namespace Sakuno.DBPF;

internal static class RefPackDecompressor
{
    private const int HeaderSize = 9;

    internal static byte[] Decompress(byte[] input, uint expectedSize, Tgi identifier)
    {
        if (input.Length < HeaderSize)
            throw Error(identifier, $"the RefPack header is truncated: expected at least {HeaderSize} bytes, found {input.Length}");

        var storedSize = BinaryPrimitives.ReadUInt32LittleEndian(input);
        if (storedSize != input.Length)
            throw Error(identifier, $"the stored-size prefix is {storedSize}, but the resource contains {input.Length} bytes");

        if (input[4] != 0x10 || input[5] != 0xFB)
        {
            throw new NotSupportedException(
                $"Resource {identifier} does not use the supported SimCity 4 RefPack header 10 FB.");
        }

        var headerSize = (uint)(input[6] << 16 | input[7] << 8 | input[8]);
        if (headerSize != expectedSize)
            throw Error(identifier, $"the RefPack header declares {headerSize} output bytes, but the compression directory declares {expectedSize}");
        if (expectedSize > int.MaxValue)
            throw new NotSupportedException($"Resource {identifier} is too large to decompress into a managed byte array.");

        var output = new byte[(int)expectedSize];
        var inputPosition = HeaderSize;
        var outputPosition = 0;

        while (true)
        {
            if (inputPosition >= input.Length)
                throw Error(identifier, "the command stream ended without a final command");

            var commandOffset = inputPosition;
            var b0 = input[inputPosition++];
            if (b0 >= 0xFC)
            {
                CopyLiterals(input, ref inputPosition, output, ref outputPosition, b0 & 3, identifier, commandOffset);
                if (outputPosition != output.Length)
                    throw Error(identifier, $"the final command produced {outputPosition} bytes, but {output.Length} were declared");
                if (inputPosition != input.Length)
                    throw Error(identifier, $"{input.Length - inputPosition} trailing bytes follow the final command");

                return output;
            }

            int literalCount;
            int matchLength;
            int distance;
            if (b0 <= 0x7F)
            {
                EnsureInput(input, inputPosition, 1, identifier, commandOffset);
                var b1 = input[inputPosition++];
                literalCount = b0 & 3;
                matchLength = ((b0 >> 2) & 7) + 3;
                distance = ((b0 & 0x60) << 3) + b1 + 1;
            }
            else if (b0 <= 0xBF)
            {
                EnsureInput(input, inputPosition, 2, identifier, commandOffset);
                var b1 = input[inputPosition++];
                var b2 = input[inputPosition++];
                literalCount = (b1 >> 6) & 3;
                matchLength = (b0 & 0x3F) + 4;
                distance = ((b1 & 0x3F) << 8) + b2 + 1;
            }
            else if (b0 <= 0xDF)
            {
                EnsureInput(input, inputPosition, 3, identifier, commandOffset);
                var b1 = input[inputPosition++];
                var b2 = input[inputPosition++];
                var b3 = input[inputPosition++];
                literalCount = b0 & 3;
                matchLength = ((b0 & 0x0C) << 6) + b3 + 5;
                distance = ((b0 & 0x10) << 12) + (b1 << 8) + b2 + 1;
            }
            else
            {
                literalCount = ((b0 & 0x1F) << 2) + 4;
                CopyLiterals(input, ref inputPosition, output, ref outputPosition, literalCount, identifier, commandOffset);
                continue;
            }

            CopyLiterals(input, ref inputPosition, output, ref outputPosition, literalCount, identifier, commandOffset);
            CopyMatch(output, ref outputPosition, matchLength, distance, identifier, commandOffset);
        }
    }

    private static void CopyLiterals(
        byte[] input,
        ref int inputPosition,
        byte[] output,
        ref int outputPosition,
        int count,
        Tgi identifier,
        int commandOffset)
    {
        EnsureInput(input, inputPosition, count, identifier, commandOffset);
        EnsureOutput(output, outputPosition, count, identifier, commandOffset);
        input.AsSpan(inputPosition, count).CopyTo(output.AsSpan(outputPosition));
        inputPosition += count;
        outputPosition += count;
    }

    private static void CopyMatch(
        byte[] output,
        ref int outputPosition,
        int count,
        int distance,
        Tgi identifier,
        int commandOffset)
    {
        if (distance > outputPosition)
        {
            throw Error(
                identifier,
                $"the command at input offset {commandOffset} refers {distance} bytes back with only {outputPosition} bytes available");
        }

        EnsureOutput(output, outputPosition, count, identifier, commandOffset);
        for (var index = 0; index < count; index++, outputPosition++)
            output[outputPosition] = output[outputPosition - distance];
    }

    private static void EnsureInput(byte[] input, int position, int count, Tgi identifier, int commandOffset)
    {
        if (count > input.Length - position)
            throw Error(identifier, $"the command at input offset {commandOffset} has truncated command or literal bytes");
    }

    private static void EnsureOutput(byte[] output, int position, int count, Tgi identifier, int commandOffset)
    {
        if (count > output.Length - position)
            throw Error(identifier, $"the command at input offset {commandOffset} exceeds the declared output size");
    }

    private static InvalidDataException Error(Tgi identifier, string message) =>
        new($"Cannot decompress resource {identifier}: {message}.");
}
