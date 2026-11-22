using System.Text;
using Hatch.Api.Common;

namespace Hatch.Api.Tests.Common;

/// <summary>
/// The video sniff on its own: each container it knows, and the files that
/// share a box or a magic number with one and are not video.
/// </summary>
public class AttachmentVideoTests
{
    [Theory]
    [InlineData("isom", "video/mp4")]
    [InlineData("mp42", "video/mp4")]
    [InlineData("avc1", "video/mp4")]
    [InlineData("M4V ", "video/mp4")]
    [InlineData("qt  ", "video/quicktime")]
    public void AnFtypBrand_IsRecognisedAsItsVideoType(string brand, string expected)
    {
        Assert.True(AttachmentVideo.TryDetectContentType(Ftyp(brand), out var type));
        Assert.Equal(expected, type);
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("avif")]
    [InlineData("M4A ")]
    public void AnFtypBrandThatIsNotVideo_IsNotRecognised(string brand)
    {
        Assert.False(AttachmentVideo.TryDetectContentType(Ftyp(brand), out var type));
        Assert.Equal(string.Empty, type);
    }

    [Fact]
    public void AWebmHeader_IsRecognised()
    {
        Assert.True(AttachmentVideo.TryDetectContentType(Ebml("webm"), out var type));
        Assert.Equal("video/webm", type);
    }

    [Fact]
    public void AMatroskaHeader_IsNotRecognised()
    {
        Assert.False(AttachmentVideo.TryDetectContentType(Ebml("matroska"), out _));
    }

    [Fact]
    public void TheWordWebmWithoutTheMagic_IsNotRecognised()
    {
        Assert.False(AttachmentVideo.TryDetectContentType("....webm and some text after"u8, out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(8)]
    [InlineData(11)]
    public void TooFewBytes_AreNotRecognisedAndDoNotThrow(int length)
    {
        Assert.False(AttachmentVideo.TryDetectContentType(Ftyp("isom").AsSpan(0, length), out _));
    }

    [Fact]
    public void AFourByteEbmlMagicAlone_IsNotRecognised()
    {
        Assert.False(AttachmentVideo.TryDetectContentType([0x1A, 0x45, 0xDF, 0xA3], out _));
    }

    internal static byte[] Ftyp(string brand) =>
        [0x00, 0x00, 0x00, 0x18, .. Encoding.ASCII.GetBytes("ftyp" + brand), 0, 0, 0, 0, 0, 0, 0, 0];

    internal static byte[] Ebml(string docType) =>
        [0x1A, 0x45, 0xDF, 0xA3, 0x9F, 0x42, 0x86, 0x81, 0x01, 0x42, 0x82, 0x84, .. Encoding.ASCII.GetBytes(docType), 0x42, 0x87, 0x81, 0x04];
}
