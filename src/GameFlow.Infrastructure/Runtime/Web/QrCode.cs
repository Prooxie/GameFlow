using System.Text;

namespace GameFlow.Infrastructure.Runtime.Web;

/// <summary>Error-correction level of a <see cref="QrCode"/>.</summary>
public enum QrErrorCorrection
{
    /// <summary>Recovers about 7% damage. Largest modules for a given text.</summary>
    Low,

    /// <summary>Recovers about 15% damage.</summary>
    Medium,
}

/// <summary>
/// A QR code (ISO/IEC 18004) encoding text in byte mode, versions 1–10.
///
/// <para>
/// It exists so a phone can join by pointing its camera at the screen: a
/// phone's link carries a 64-character access token, which nobody is going
/// to type. Written in place rather than taken as a package because it is
/// one small, well-specified algorithm, and the app ships no network-facing
/// dependency it can avoid. Versions stop at 10 (up to 213 bytes at medium
/// correction), far beyond a LAN link.
/// </para>
///
/// <para>
/// The structure follows the standard's construction order: data
/// codewords, Reed–Solomon error correction per block, interleaving,
/// function patterns, data placement, then the mask with the lowest
/// penalty and the matching format and version information.
/// </para>
/// </summary>
public sealed class QrCode
{
    public const int MinVersion = 1;
    public const int MaxVersion = 10;

    // Indexed by version; element 0 unused. From the standard's table 9.
    private static readonly int[] EccPerBlockLow = [-1, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18];
    private static readonly int[] EccPerBlockMedium = [-1, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26];
    private static readonly int[] BlocksLow = [-1, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4];
    private static readonly int[] BlocksMedium = [-1, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5];

    private readonly bool[,] modules;
    private readonly bool[,] isFunction;

    private QrCode(int version, QrErrorCorrection level, byte[] dataCodewords, int? forcedMask)
    {
        Version = version;
        Level = level;
        Size = (version * 4) + 17;
        modules = new bool[Size, Size];
        isFunction = new bool[Size, Size];

        DrawFunctionPatterns();
        var allCodewords = AddEccAndInterleave(dataCodewords);
        DrawCodewords(allCodewords);

        var mask = forcedMask ?? ChooseMask();
        ApplyMask(mask);
        DrawFormatBits(mask);
        Mask = mask;
    }

    public int Version { get; }

    public QrErrorCorrection Level { get; }

    /// <summary>Modules per side, without the quiet zone.</summary>
    public int Size { get; }

    /// <summary>The mask pattern applied (0–7).</summary>
    public int Mask { get; }

    /// <summary>True for a dark module. Out-of-range coordinates read as light (the quiet zone).</summary>
    public bool this[int x, int y] =>
        x >= 0 && y >= 0 && x < Size && y < Size && modules[y, x];

    /// <summary>
    /// Encodes <paramref name="text"/> as UTF-8 in the smallest version that
    /// fits at <paramref name="level"/>.
    /// </summary>
    /// <param name="text">The text to encode.</param>
    /// <param name="level">Error correction; medium tolerates a glare spot or a scuffed screen.</param>
    /// <param name="forcedMask">Test seam: use this mask instead of the lowest-penalty one.</param>
    /// <exception cref="ArgumentException">The text does not fit in version 10.</exception>
    public static QrCode Encode(string text, QrErrorCorrection level = QrErrorCorrection.Medium, int? forcedMask = null)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (forcedMask is < 0 or > 7)
        {
            throw new ArgumentOutOfRangeException(nameof(forcedMask));
        }

        var data = Encoding.UTF8.GetBytes(text);
        for (var version = MinVersion; version <= MaxVersion; version++)
        {
            var countBits = version <= 9 ? 8 : 16;
            var capacityBits = DataCodewordCount(version, level) * 8;
            var usedBits = 4 + countBits + (data.Length * 8);
            if (usedBits <= capacityBits)
            {
                return new QrCode(version, level, BuildDataCodewords(data, countBits, capacityBits), forcedMask);
            }
        }

        throw new ArgumentException($"Text of {data.Length} bytes is too long for a version {MaxVersion} QR code.", nameof(text));
    }

    // ─── Data ────────────────────────────────────────────────────────

    private static byte[] BuildDataCodewords(byte[] data, int countBits, int capacityBits)
    {
        var bits = new BitWriter();
        bits.Append(0b0100, 4);              // byte mode
        bits.Append(data.Length, countBits);
        foreach (var b in data)
        {
            bits.Append(b, 8);
        }

        bits.Append(0, Math.Min(4, capacityBits - bits.Length)); // terminator
        bits.Append(0, (8 - (bits.Length % 8)) % 8);              // byte align
        for (var pad = 0xEC; bits.Length < capacityBits; pad ^= 0xEC ^ 0x11)
        {
            bits.Append(pad, 8);
        }

        return bits.ToBytes();
    }

    private static int RawDataModuleCount(int version)
    {
        var result = ((16 * version) + 128) * version + 64;
        if (version >= 2)
        {
            var alignCount = (version / 7) + 2;
            result -= ((25 * alignCount) - 10) * alignCount - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }
        return result;
    }

    private static int EccPerBlock(int version, QrErrorCorrection level) =>
        level == QrErrorCorrection.Low ? EccPerBlockLow[version] : EccPerBlockMedium[version];

    private static int BlockCount(int version, QrErrorCorrection level) =>
        level == QrErrorCorrection.Low ? BlocksLow[version] : BlocksMedium[version];

    private static int DataCodewordCount(int version, QrErrorCorrection level) =>
        (RawDataModuleCount(version) / 8) - (EccPerBlock(version, level) * BlockCount(version, level));

    private byte[] AddEccAndInterleave(byte[] data)
    {
        var blockCount = BlockCount(Version, Level);
        var eccLength = EccPerBlock(Version, Level);
        var rawCodewords = RawDataModuleCount(Version) / 8;
        var shortBlockCount = blockCount - (rawCodewords % blockCount);
        var shortBlockLength = rawCodewords / blockCount;

        var divisor = ReedSolomonDivisor(eccLength);
        var blocks = new List<byte[]>(blockCount);
        for (int i = 0, offset = 0; i < blockCount; i++)
        {
            var dataLength = shortBlockLength - eccLength + (i < shortBlockCount ? 0 : 1);
            var block = new byte[shortBlockLength + 1];
            Array.Copy(data, offset, block, 0, dataLength);
            offset += dataLength;

            var ecc = ReedSolomonRemainder(data.AsSpan(offset - dataLength, dataLength), divisor);
            // Short blocks carry a placeholder byte so all blocks interleave by
            // the same index; it is skipped below.
            Array.Copy(ecc, 0, block, shortBlockLength + 1 - eccLength, eccLength);
            blocks.Add(block);
        }

        var result = new byte[rawCodewords];
        var k = 0;
        for (var i = 0; i < shortBlockLength + 1; i++)
        {
            for (var j = 0; j < blocks.Count; j++)
            {
                if (i != shortBlockLength - eccLength || j >= shortBlockCount)
                {
                    result[k++] = blocks[j][i];
                }
            }
        }

        return result;
    }

    private static byte[] ReedSolomonDivisor(int degree)
    {
        var result = new byte[degree];
        result[degree - 1] = 1;
        byte root = 1;
        for (var i = 0; i < degree; i++)
        {
            for (var j = 0; j < result.Length; j++)
            {
                result[j] = GfMultiply(result[j], root);
                if (j + 1 < result.Length)
                {
                    result[j] ^= result[j + 1];
                }
            }
            root = GfMultiply(root, 0x02);
        }
        return result;
    }

    private static byte[] ReedSolomonRemainder(ReadOnlySpan<byte> data, byte[] divisor)
    {
        var result = new byte[divisor.Length];
        foreach (var b in data)
        {
            var factor = (byte)(b ^ result[0]);
            Array.Copy(result, 1, result, 0, result.Length - 1);
            result[^1] = 0;
            for (var i = 0; i < result.Length; i++)
            {
                result[i] ^= GfMultiply(divisor[i], factor);
            }
        }
        return result;
    }

    /// <summary>Multiplication in GF(2⁸) modulo x⁸ + x⁴ + x³ + x² + 1.</summary>
    private static byte GfMultiply(byte x, byte y)
    {
        var z = 0;
        for (var i = 7; i >= 0; i--)
        {
            z = (z << 1) ^ ((z >> 7) * 0x11D);
            z ^= ((y >> i) & 1) * x;
        }
        return (byte)z;
    }

    // ─── Function patterns ───────────────────────────────────────────

    private void DrawFunctionPatterns()
    {
        for (var i = 0; i < Size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(Size - 4, 3);
        DrawFinder(3, Size - 4);

        var positions = AlignmentPositions();
        var count = positions.Length;
        for (var i = 0; i < count; i++)
        {
            for (var j = 0; j < count; j++)
            {
                // The three corners already hold finder patterns.
                if ((i == 0 && j == 0) || (i == 0 && j == count - 1) || (i == count - 1 && j == 0))
                {
                    continue;
                }
                DrawAlignment(positions[i], positions[j]);
            }
        }

        // Reserve the format areas now (real bits drawn after masking).
        DrawFormatBits(0);
        DrawVersion();
    }

    private int[] AlignmentPositions()
    {
        if (Version == 1)
        {
            return [];
        }

        var count = (Version / 7) + 2;
        var step = ((Version * 8) + (count * 3) + 5) / ((count * 4) - 4) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = Size - 7; i >= 1; i--, position -= step)
        {
            result[i] = position;
        }
        return result;
    }

    private void DrawFinder(int x, int y)
    {
        for (var dy = -4; dy <= 4; dy++)
        {
            for (var dx = -4; dx <= 4; dx++)
            {
                var distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                int xx = x + dx, yy = y + dy;
                if (xx >= 0 && xx < Size && yy >= 0 && yy < Size)
                {
                    SetFunction(xx, yy, distance != 2 && distance != 4);
                }
            }
        }
    }

    private void DrawAlignment(int x, int y)
    {
        for (var dy = -2; dy <= 2; dy++)
        {
            for (var dx = -2; dx <= 2; dx++)
            {
                SetFunction(x + dx, y + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
            }
        }
    }

    private void DrawFormatBits(int mask)
    {
        // Error-correction indicator bits: L = 01, M = 00.
        var data = ((Level == QrErrorCorrection.Low ? 1 : 0) << 3) | mask;
        var remainder = data;
        for (var i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }
        var bits = ((data << 10) | remainder) ^ 0x5412;

        for (var i = 0; i <= 5; i++)
        {
            SetFunction(8, i, Bit(bits, i));
        }
        SetFunction(8, 7, Bit(bits, 6));
        SetFunction(8, 8, Bit(bits, 7));
        SetFunction(7, 8, Bit(bits, 8));
        for (var i = 9; i < 15; i++)
        {
            SetFunction(14 - i, 8, Bit(bits, i));
        }

        for (var i = 0; i < 8; i++)
        {
            SetFunction(Size - 1 - i, 8, Bit(bits, i));
        }
        for (var i = 8; i < 15; i++)
        {
            SetFunction(8, Size - 15 + i, Bit(bits, i));
        }
        SetFunction(8, Size - 8, true); // always-dark module
    }

    private void DrawVersion()
    {
        if (Version < 7)
        {
            return;
        }

        var remainder = Version;
        for (var i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }
        var bits = (Version << 12) | remainder;

        for (var i = 0; i < 18; i++)
        {
            var bit = Bit(bits, i);
            int a = Size - 11 + (i % 3), b = i / 3;
            SetFunction(a, b, bit);
            SetFunction(b, a, bit);
        }
    }

    private void SetFunction(int x, int y, bool dark)
    {
        modules[y, x] = dark;
        isFunction[y, x] = true;
    }

    private static bool Bit(int value, int index) => ((value >> index) & 1) != 0;

    // ─── Data placement and masking ─────────────────────────────────

    private void DrawCodewords(byte[] data)
    {
        var bitIndex = 0;
        var totalBits = data.Length * 8;
        for (var right = Size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5; // skip the vertical timing column
            }

            for (var vertical = 0; vertical < Size; vertical++)
            {
                for (var j = 0; j < 2; j++)
                {
                    var x = right - j;
                    var upward = ((right + 1) & 2) == 0;
                    var y = upward ? Size - 1 - vertical : vertical;
                    if (!isFunction[y, x] && bitIndex < totalBits)
                    {
                        modules[y, x] = ((data[bitIndex >> 3] >> (7 - (bitIndex & 7))) & 1) != 0;
                        bitIndex++;
                    }
                }
            }
        }
    }

    private void ApplyMask(int mask)
    {
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                if (!isFunction[y, x] && MaskBit(mask, x, y))
                {
                    modules[y, x] = !modules[y, x];
                }
            }
        }
    }

    private static bool MaskBit(int mask, int x, int y) => mask switch
    {
        0 => (x + y) % 2 == 0,
        1 => y % 2 == 0,
        2 => x % 3 == 0,
        3 => (x + y) % 3 == 0,
        4 => ((x / 3) + (y / 2)) % 2 == 0,
        5 => ((x * y) % 2) + ((x * y) % 3) == 0,
        6 => (((x * y) % 2) + ((x * y) % 3)) % 2 == 0,
        _ => (((x + y) % 2) + ((x * y) % 3)) % 2 == 0,
    };

    /// <summary>
    /// Picks the mask that scores lowest on the standard's penalty rules —
    /// any mask decodes, but the low-penalty one reads more reliably.
    /// </summary>
    private int ChooseMask()
    {
        var best = 0;
        var bestPenalty = int.MaxValue;
        for (var mask = 0; mask < 8; mask++)
        {
            ApplyMask(mask);
            DrawFormatBits(mask);
            var penalty = Penalty();
            if (penalty < bestPenalty)
            {
                best = mask;
                bestPenalty = penalty;
            }
            ApplyMask(mask); // masking is its own inverse
        }
        return best;
    }

    private int Penalty()
    {
        var result = 0;

        // Runs of five or more same-coloured modules, in rows then columns.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var a = 0; a < Size; a++)
            {
                var runColor = false;
                var runLength = 0;
                for (var b = 0; b < Size; b++)
                {
                    var color = pass == 0 ? modules[a, b] : modules[b, a];
                    if (b > 0 && color == runColor)
                    {
                        runLength++;
                        if (runLength == 5) { result += 3; }
                        else if (runLength > 5) { result++; }
                    }
                    else
                    {
                        runColor = color;
                        runLength = 1;
                    }
                }
            }
        }

        // 2×2 blocks of one colour.
        for (var y = 0; y < Size - 1; y++)
        {
            for (var x = 0; x < Size - 1; x++)
            {
                var color = modules[y, x];
                if (color == modules[y, x + 1] && color == modules[y + 1, x] && color == modules[y + 1, x + 1])
                {
                    result += 3;
                }
            }
        }

        // Finder-like 1:1:3:1:1 patterns with four light modules on a side.
        for (var pass = 0; pass < 2; pass++)
        {
            for (var a = 0; a < Size; a++)
            {
                for (var b = 0; b + 10 < Size + 4; b++)
                {
                    if (FinderLike(pass, a, b))
                    {
                        result += 40;
                    }
                }
            }
        }

        // Balance of dark and light.
        var dark = 0;
        foreach (var module in modules)
        {
            if (module) { dark++; }
        }
        var total = Size * Size;
        var k = ((Math.Abs((dark * 20) - (total * 10)) + total - 1) / total) - 1;
        result += k * 10;

        return result;
    }

    /// <summary>Dark-light-dark×3-light-dark followed or preceded by four light, at (a, b..b+10).</summary>
    private bool FinderLike(int pass, int a, int start)
    {
        bool At(int i) => pass == 0 ? this[start + i, a] : this[a, start + i];

        bool core = At(4) && !At(5) && At(6) && At(7) && At(8) && !At(9) && At(10);
        if (!core)
        {
            return false;
        }

        var lightBefore = !At(0) && !At(1) && !At(2) && !At(3);
        var lightAfter = !At(11) && !At(12) && !At(13) && !At(14);
        return lightBefore || lightAfter;
    }

    private sealed class BitWriter
    {
        private readonly List<bool> bits = [];

        public int Length => bits.Count;

        public void Append(int value, int length)
        {
            for (var i = length - 1; i >= 0; i--)
            {
                bits.Add(((value >> i) & 1) != 0);
            }
        }

        public byte[] ToBytes()
        {
            var result = new byte[bits.Count / 8];
            for (var i = 0; i < bits.Count; i++)
            {
                if (bits[i])
                {
                    result[i >> 3] |= (byte)(0x80 >> (i & 7));
                }
            }
            return result;
        }
    }
}
