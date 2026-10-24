using GameFlow.Infrastructure.Runtime.Web;
using Xunit;

namespace GameFlow.Infrastructure.Tests.Runtime.Web;

public sealed class QrCodeTests
{
    /// <summary>
    /// "hello", medium correction, mask 3, as produced by an independent
    /// encoder (python-qrcode). Module-for-module agreement with a
    /// reference is the only check that catches a wrong table entry or a
    /// misplaced bit: a structurally plausible code can still be unreadable.
    /// </summary>
    private static readonly string[] HelloMediumMask3 =
    [
        "111111101000001111111",
        "100000101000001000001",
        "101110100101001011101",
        "101110101010101011101",
        "101110100111001011101",
        "100000100111101000001",
        "111111101010101111111",
        "000000001111100000000",
        "101101110101101001011",
        "111010010011111001101",
        "110111101101000000011",
        "110101000111001111010",
        "010100111100100100001",
        "000000001011001000100",
        "111111101011100100000",
        "100000101010000111110",
        "101110100000111111111",
        "101110101011001011110",
        "101110101000101100100",
        "100000100000010110001",
        "111111101110010100100",
    ];

    [Fact]
    public void Encode_MatchesReferenceEncoderModuleForModule()
    {
        var code = QrCode.Encode("hello", QrErrorCorrection.Medium, forcedMask: 3);

        Assert.Equal(1, code.Version);
        Assert.Equal(21, code.Size);
        for (var y = 0; y < code.Size; y++)
        {
            var row = string.Concat(Enumerable.Range(0, code.Size).Select(x => code[x, y] ? '1' : '0'));
            Assert.Equal(HelloMediumMask3[y], row);
        }
    }

    [Fact]
    public void Encode_PhoneLink_FitsAndPicksSmallestVersion()
    {
        var link = "http://192.168.100.200:8080/?token=" + new string('f', 64) + "&seat=15";

        var code = QrCode.Encode(link);

        // 107 bytes: version 6 holds 106 at medium correction, version 7 holds 122.
        Assert.Equal(7, code.Version);
        Assert.Equal(45, code.Size);
        Assert.InRange(code.Mask, 0, 7);
    }

    [Fact]
    public void Encode_DrawsFinderPatternsInThreeCorners()
    {
        var code = QrCode.Encode("GameFlow");
        var last = code.Size - 1;

        foreach (var (x, y) in new[] { (0, 0), (last - 6, 0), (0, last - 6) })
        {
            Assert.True(code[x, y]);           // outer ring corner
            Assert.False(code[x + 1, y + 1]);  // light ring
            Assert.True(code[x + 3, y + 3]);   // centre
        }
    }

    [Fact]
    public void Indexer_OutsideTheSymbol_ReadsLight()
    {
        var code = QrCode.Encode("x");

        Assert.False(code[-1, 0]);
        Assert.False(code[0, code.Size]);
    }

    [Fact]
    public void Encode_TextBeyondVersion10_Throws()
    {
        Assert.Throws<ArgumentException>(() => QrCode.Encode(new string('a', 400)));
    }
}
