using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace WarbandToBannerlordConverter;
// The terrain processor takes the pfm and pgm files and converts them into a greyscale png so that people don't have to do so manually with another program like gimp or photoshop. These become our heightmap and materialmaps.
// At the time of writing this, the code still contains a 3/5ths playable area fix. This is not actually needed. My first test map had some sort of corruption in the terrain data that caused another "ghost" terrain to extend 5/3rds beyond the actual map bounds.
// I had incorrectly assumed that this was the case for all maps and that it was some sort of outer terrain information that got mixed in. Once I'm sure that this was a one-off case, I will remove the cropped 3/5ths "fix".

public class PfmResults
{
    public float ZScale { get; set; }
    public float ZOffset { get; set; }
}

public static class TerrainProcessor
{
    public static PfmResults ProcessFolder(string folderPath, Action<string> log)
    {
        PfmResults results = null;

        var pfmFiles = Directory.GetFiles(folderPath, "*.pfm");
        var pgmFiles = Directory.GetFiles(folderPath, "*.pgm");

        foreach (var file in pfmFiles)
        {
            log($"Processing PFM: {Path.GetFileName(file)}...");
            results = ConvertPfm(file);
        }

        foreach (var file in pgmFiles)
        {
            log($"Processing PGM: {Path.GetFileName(file)}...");
            ConvertPgm(file);
        }
        log("Terrain conversion complete!");

        return results;
    }

    private static PfmResults ConvertPfm(string path)
    {
        string fullPngPath = Path.ChangeExtension(path, ".png");
        float min = float.MaxValue, max = float.MinValue;

        using (var fs = File.OpenRead(path))
        using (var br = new BinaryReader(fs))
        {
            ReadToNewline(br);
            string dimLine = ReadToNewline(br);
            string[] dims = dimLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int fullW = int.Parse(dims[0]);
            int fullH = int.Parse(dims[1]);
            ReadToNewline(br);

            float[] pixels = new float[fullW * fullH];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = br.ReadSingle();

            // Calculate 3/5ths Playable Area, likely not needed, see top comments.
            int playableW = (fullW * 3) / 5;
            int playableH = (fullH * 3) / 5;
            int offsetX = fullW - playableW;

            for (int y = 0; y < playableH; y++)
            {
                for (int x = 0; x < playableW; x++)
                {
                    float v = pixels[y * fullW + (x + offsetX)];
                    if (float.IsNaN(v) || float.IsInfinity(v)) continue;
                    if (v < min) min = v;
                    if (v > max) max = v;
                }
            }

            float range = (max - min == 0) ? 1.0f : max - min;

            using (var bmp = new Bitmap(fullW, fullH, PixelFormat.Format48bppRgb))
            {
                var data = bmp.LockBits(new Rectangle(0, 0, fullW, fullH), ImageLockMode.WriteOnly, PixelFormat.Format48bppRgb);
                byte[] buffer = new byte[data.Stride * fullH];

                for (int i = 0; i < pixels.Length; i++)
                {
                    float norm = (pixels[i] - min) / range;
                    ushort val = (ushort)(Math.Clamp(norm, 0, 1) * 65535f + 0.5f);
                    int py = i / fullW;
                    int px = i % fullW;
                    int idx = py * data.Stride + px * 6;
                    buffer[idx] = buffer[idx + 2] = buffer[idx + 4] = (byte)(val & 0xFF);
                    buffer[idx + 1] = buffer[idx + 3] = buffer[idx + 5] = (byte)((val >> 8) & 0xFF);
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                bmp.UnlockBits(data);

                bmp.RotateFlip(RotateFlipType.RotateNoneFlipX);
                bmp.Save(fullPngPath, ImageFormat.Png);
            }



            using (var fullImage = (Bitmap)Image.FromFile(fullPngPath))
            using (var croppedBmp = new Bitmap(playableW, playableH, fullImage.PixelFormat))
            {
                using (Graphics g = Graphics.FromImage(croppedBmp))
                {
                    Rectangle srcRect = new Rectangle(0, fullH - playableH, playableW, playableH);
                    Rectangle destRect = new Rectangle(0, 0, playableW, playableH);
                    g.DrawImage(fullImage, destRect, srcRect, GraphicsUnit.Pixel);
                }
                string cropPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_cropped.png");
                croppedBmp.Save(cropPath, ImageFormat.Png);
            }
        }
        
        return new PfmResults { ZScale = max - min, ZOffset = min };
    }


    private static void ConvertPgm(string path)
    {
        string fullPngPath = Path.ChangeExtension(path, ".png");
        int w, h;

        using (var fs = File.OpenRead(path))
        using (var br = new BinaryReader(fs))
        {
            ReadToSpace(br);
            w = int.Parse(ReadToSpace(br));
            h = int.Parse(ReadToSpace(br));
            ReadToSpace(br);

            byte[] raw = br.ReadBytes(w * h);
            using (var bmp = new Bitmap(w, h, PixelFormat.Format8bppIndexed))
            {
                ColorPalette pal = bmp.Palette;
                for (int i = 0; i < 256; i++) pal.Entries[i] = Color.FromArgb(i, i, i);
                bmp.Palette = pal;

                var data = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format8bppIndexed);
                byte[] buffer = new byte[data.Stride * h];
                for (int i = 0; i < raw.Length; i++)
                {
                    int py = (h - 1) - (i / w);
                    int px = (w - 1) - (i % w);
                    buffer[py * data.Stride + px] = raw[i];
                }
                Marshal.Copy(buffer, 0, data.Scan0, buffer.Length);
                bmp.UnlockBits(data);
                bmp.Save(fullPngPath, ImageFormat.Png);
            }
        }

        // Load Saved PNG to do crop. It keeps screwing up when I try to do the cropping from the pfm data itself. This much simpler. - Note, now likely not needed. See top comments.
        int cropW = (w * 3) / 5;
        int cropH = (h * 3) / 5;
        using (var fullImage = (Bitmap)Image.FromFile(fullPngPath))
        {
            Rectangle cropRect = new Rectangle(0, h - cropH, cropW, cropH);
            using (var croppedBmp = fullImage.Clone(cropRect, fullImage.PixelFormat))
            {
                string cropPath = Path.Combine(Path.GetDirectoryName(path), Path.GetFileNameWithoutExtension(path) + "_cropped.png");
                croppedBmp.Save(cropPath, ImageFormat.Png);
            }
        }
    }

    private static string ReadToNewline(BinaryReader br)
    {
        StringBuilder sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            char c = (char)br.ReadByte();
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    private static string ReadToSpace(BinaryReader br)
    {
        StringBuilder sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            char c = (char)br.ReadByte();
            if (char.IsWhiteSpace(c)) { if (sb.Length > 0) break; continue; }
            sb.Append(c);
        }
        return sb.ToString();
    }
}