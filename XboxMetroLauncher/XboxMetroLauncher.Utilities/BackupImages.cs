using System;
using System.IO;
using System.Linq;
using System.Windows.Media.Imaging;

namespace XboxMetroLauncher.Utilities;

internal static class BackupImages
{
    public static byte[] Decode(string base64, string fileName)
    {
        SafePaths.FileName(fileName);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico" }.Contains(Path.GetExtension(fileName), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Backup assets must be supported image files.");
        if (base64.Length > 24 * 1024 * 1024) throw new InvalidDataException("A backup image exceeds the 18 MB limit.");
        try
        {
            var bytes = Convert.FromBase64String(base64);
            using var stream = new MemoryStream(bytes, writable: false);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.OnDemand);
            if (decoder.Frames.Count == 0 || decoder.Frames.Count > 128) throw new InvalidDataException("Invalid image frame count.");
            foreach (var frame in decoder.Frames)
                if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0 || frame.PixelWidth > 8192 || frame.PixelHeight > 8192 || (long)frame.PixelWidth * frame.PixelHeight > 32_000_000)
                    throw new InvalidDataException("A backup image exceeds the supported dimensions.");
            return bytes;
        }
        catch (Exception ex) when (ex is FormatException or NotSupportedException or System.IO.FileFormatException or ArgumentException)
        { throw new InvalidDataException("A backup image is invalid.", ex); }
    }
}
