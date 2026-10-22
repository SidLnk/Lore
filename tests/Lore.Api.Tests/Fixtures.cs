using System.Security.Cryptography;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

namespace Lore.Api.Tests;

public static class Fixtures
{
    /// <summary>Small unique JPEG with EXIF date, camera and (optionally) GPS.</summary>
    public static byte[] Jpeg(int seed, DateTime taken, (double Lat, double Lon)? gps = null, ushort orientation = 1, int width = 64, int height = 48)
    {
        using var img = new Image<Rgb24>(width, height, new Rgb24((byte)seed, (byte)(seed >> 8), 128));
        img[0, 0] = new Rgb24((byte)(seed * 7), 1, 2);

        var exif = new ExifProfile();
        exif.SetValue(ExifTag.DateTimeOriginal, taken.ToString("yyyy:MM:dd HH:mm:ss"));
        exif.SetValue(ExifTag.Make, "TestMake");
        exif.SetValue(ExifTag.Model, "TestModel");
        exif.SetValue(ExifTag.Orientation, orientation);
        if (gps is var (lat, lon))
        {
            exif.SetValue(ExifTag.GPSLatitudeRef, lat >= 0 ? "N" : "S");
            exif.SetValue(ExifTag.GPSLatitude, Dms(lat));
            exif.SetValue(ExifTag.GPSLongitudeRef, lon >= 0 ? "E" : "W");
            exif.SetValue(ExifTag.GPSLongitude, Dms(lon));
        }
        img.Metadata.ExifProfile = exif;

        using var ms = new MemoryStream();
        img.SaveAsJpeg(ms);
        return ms.ToArray();
    }

    static Rational[] Dms(double value)
    {
        value = Math.Abs(value);
        var deg = Math.Floor(value);
        var min = Math.Floor((value - deg) * 60);
        var sec = (value - deg - min / 60) * 3600;
        return [new Rational((uint)deg, 1), new Rational((uint)min, 1), new Rational((uint)Math.Round(sec * 10000), 10000)];
    }

    public static byte[] RandomBytes(int size) => RandomNumberGenerator.GetBytes(size);

    public static string Sha256(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
}
