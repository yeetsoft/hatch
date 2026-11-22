namespace Hatch.Api.Common;

/// <summary>
/// Decides whether an attached file is a video, and which kind.
///
/// Like <see cref="PersonPhoto"/>, the answer comes from the bytes and only
/// from the bytes: a request's Content-Type is the uploader's claim. It is a
/// separate rule from the photo one because the photo rule has a size limit of
/// its own and answers a different question (is this an avatar).
///
/// Three containers, the ones a browser records and a phone saves: MP4,
/// QuickTime and WebM. Each starts with a signature the others do not share.
/// An MP4-family file starts with an <c>ftyp</c> box naming a brand, and the
/// same box is shared by HEIC and AVIF images and by M4A audio, so the brand
/// is checked against a list of video brands rather than trusting the box
/// alone. WebM is a Matroska file whose document type is <c>webm</c>; plain
/// Matroska has the same first four bytes and is left to its declared type.
/// </summary>
public static class AttachmentVideo
{
    /// <summary>How much of a file this needs to see: the EBML header's document type sits near the front.</summary>
    public const int SniffBytes = 64;

    private const string Mp4 = "video/mp4";

    private const string QuickTime = "video/quicktime";

    private const string WebM = "video/webm";

    private static readonly byte[] Ftyp = "ftyp"u8.ToArray();

    private static readonly byte[] EbmlMagic = [0x1A, 0x45, 0xDF, 0xA3];

    private static readonly byte[] WebmDocType = "webm"u8.ToArray();

    private static readonly byte[] QuickTimeBrand = "qt  "u8.ToArray();

    /// <summary>The <c>ftyp</c> brands that mean an MP4 video. Not HEIC, AVIF or M4A.</summary>
    private static readonly byte[][] Mp4Brands =
    [
        "isom"u8.ToArray(),
        "iso2"u8.ToArray(),
        "iso4"u8.ToArray(),
        "iso5"u8.ToArray(),
        "iso6"u8.ToArray(),
        "mp41"u8.ToArray(),
        "mp42"u8.ToArray(),
        "avc1"u8.ToArray(),
        "dash"u8.ToArray(),
        "M4V "u8.ToArray(),
        "mmp4"u8.ToArray(),
    ];

    /// <summary>
    /// The video type the first bytes of a file name, or false. Fewer bytes than
    /// a signature needs is false and never a throw.
    /// </summary>
    public static bool TryDetectContentType(ReadOnlySpan<byte> bytes, out string contentType)
    {
        contentType = string.Empty;

        if (bytes.Length >= 12 && bytes.Slice(4, 4).SequenceEqual(Ftyp))
        {
            var brand = bytes.Slice(8, 4);
            if (brand.SequenceEqual(QuickTimeBrand))
            {
                contentType = QuickTime;
                return true;
            }

            foreach (var known in Mp4Brands)
            {
                if (!brand.SequenceEqual(known)) continue;
                contentType = Mp4;
                return true;
            }

            return false;
        }

        if (bytes.Length >= EbmlMagic.Length + WebmDocType.Length
            && bytes[..EbmlMagic.Length].SequenceEqual(EbmlMagic)
            && bytes[..Math.Min(bytes.Length, SniffBytes)][EbmlMagic.Length..].IndexOf(WebmDocType) >= 0)
        {
            contentType = WebM;
            return true;
        }

        return false;
    }
}
