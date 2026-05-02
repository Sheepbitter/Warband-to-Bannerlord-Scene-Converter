using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// The terrain processor takes the pfm and pgm files and converts them into a greyscale png so that people don't have to do so manually with another program like gimp or photoshop. These become our heightmap and materialmaps.

namespace WarbandToBannerlordConverter;
public class PfmResults
{
    public float ZScale { get; set; }
    public float ZOffset { get; set; }
}

public static class TerrainProcessor
{
    private const string LayerGroundElevation = "layer_ground_elevation";

    public static string ProcessFolder(string folderPath, Action<string> log)
    {
        string layerPfmPath = null;

        foreach (var file in Directory.GetFiles(folderPath, "*.pfm"))
        {
            if (Path.GetFileNameWithoutExtension(file)
                    .Equals(LayerGroundElevation, StringComparison.OrdinalIgnoreCase))
            {
                log($"Found: {Path.GetFileName(file)}");
                layerPfmPath = file;
            }
        }

        foreach (var file in Directory.GetFiles(folderPath, "*.pgm"))
        {
            log($"Processing PGM: {Path.GetFileName(file)}...");
            ConvertPgm(file);
        }

        return layerPfmPath;
    }


    private static void ConvertPgm(string path)
    {
        string fullPngPath = Path.ChangeExtension(path, ".png");
        int w, h;

        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs);

        ReadToSpace(br); // P5
        w = int.Parse(ReadToSpace(br));
        h = int.Parse(ReadToSpace(br));
        ReadToSpace(br); // MaxVal

        byte[] raw = br.ReadBytes(w * h);
        using var bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed);
        ColorPalette pal = bmp.Palette;
        for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
        bmp.Palette = pal;

        var bmpData = bmp.LockBits(new Rectangle(0, 0, w, h),
                                   ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
        byte[] buffer = new byte[bmpData.Stride * h];
        for (int i = 0; i < raw.Length; i++)
        {
            int py = (h - 1) - (i / w);
            int px = (w - 1) - (i % w);
            buffer[py * bmpData.Stride + px] = raw[i];
        }
        Marshal.Copy(buffer, 0, bmpData.Scan0, buffer.Length);
        bmp.UnlockBits(bmpData);
        bmp.Save(fullPngPath, ImageFormat.Png);
    }


    private static string ReadToSpace(BinaryReader br)
    {
        var sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            char c = (char)br.ReadByte();
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0) break; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}