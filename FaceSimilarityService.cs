using SkiaSharp;

namespace FACE;

public sealed class FaceSimilarityService
{
    private const int SampleSize = 32;
    private const int PixelTolerance = 45;

    public double Compare(Stream candidateFaceStream, string registeredFacePath)
    {
        if (!File.Exists(registeredFacePath))
        {
            throw new FileNotFoundException("Foto wajah terdaftar tidak ditemukan.", registeredFacePath);
        }

        using var candidateBitmap = DecodeBitmap(candidateFaceStream);
        using var registeredStream = File.OpenRead(registeredFacePath);
        using var registeredBitmap = DecodeBitmap(registeredStream);

        using var resizedCandidate = ResizeForComparison(candidateBitmap);
        using var resizedRegistered = ResizeForComparison(registeredBitmap);

        var matchedPixels = 0;
        var totalPixels = SampleSize * SampleSize;

        for (var y = 0; y < SampleSize; y++)
        {
            for (var x = 0; x < SampleSize; x++)
            {
                var candidateGray = ToGray(resizedCandidate.GetPixel(x, y));
                var registeredGray = ToGray(resizedRegistered.GetPixel(x, y));

                if (Math.Abs(candidateGray - registeredGray) < PixelTolerance)
                {
                    matchedPixels++;
                }
            }
        }

        return matchedPixels / (double)totalPixels * 100;
    }

    private static SKBitmap DecodeBitmap(Stream imageStream)
    {
        if (imageStream.CanSeek)
        {
            imageStream.Position = 0;
        }

        using var skStream = new SKManagedStream(imageStream);
        return SKBitmap.Decode(skStream) ?? throw new InvalidOperationException("Gambar wajah tidak dapat dibaca.");
    }

    private static SKBitmap ResizeForComparison(SKBitmap bitmap)
    {
        var info = new SKImageInfo(SampleSize, SampleSize);
        return bitmap.Resize(info, SKFilterQuality.Medium) ?? throw new InvalidOperationException("Gambar wajah gagal diproses.");
    }

    private static int ToGray(SKColor color)
    {
        return (int)(0.3 * color.Red + 0.59 * color.Green + 0.11 * color.Blue);
    }
}
