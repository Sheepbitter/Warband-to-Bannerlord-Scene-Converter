using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Adds the pfms for the terrain generated from code with the elevation layer of the SCO, resulting in the final heightmap that can be used in warband.


namespace WarbandToBannerlordConverter;

// C# port of pfm_combiner.py, extended with PNG output.
// PfmCombiner is the sole owner of heightmap output:
//   - Reads base_terrain.pfm (written by WarbandTerrainGen)
//   - Reads layer_ground_elevation.pfm (located by TerrainProcessor)
//   - Adds them element-wise
//   - Writes heightmap.pfm
//   - Writes heightmap.png  (16-bit greyscale, same format as original TerrainProcessor PNGs)
//   - Returns ZScale and ZOffset computed from the combined heightmap.pfm
public static class PfmCombiner
{
    // ── Main entry point ─────────────────────────────────────────────────────

    public static PfmResults CombineToHeightmap(
        string basePfmPath,
        string layerPfmPath,
        string outputPfmPath,
        string outputPngPath,
        Action<string> log)
    {
        log($"Reading base terrain PFM: {Path.GetFileName(basePfmPath)}...");
        (float[] baseData, int bw, int bh, float baseScale) = ReadPfm(basePfmPath);

        log($"Reading layer PFM: {Path.GetFileName(layerPfmPath)}...");
        (float[] layerData, int lw, int lh, float _) = ReadPfm(layerPfmPath);

        if (bw != lw || bh != lh)
            throw new InvalidOperationException(
                $"Dimension mismatch: base terrain is {bw}x{bh} but layer_ground_elevation is {lw}x{lh}.\n" +
                "Make sure the terrain code matches this scene.");

        float[] combined = new float[baseData.Length];
        for (int i = 0; i < combined.Length; i++)
            combined[i] = baseData[i] + layerData[i];

        log("Writing heightmap.pfm...");
        WritePfm(outputPfmPath, combined, bw, bh, baseScale);

        log("Writing heightmap.png...");
        WritePng(outputPngPath, combined, bw, bh);

        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in combined)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
        }

        var result = new PfmResults { ZScale = max - min, ZOffset = min };
        log($"Heightmap complete! Z range: {result.ZOffset:F3} – {result.ZOffset + result.ZScale:F3} m");
        return result;
    }



    private static (float[] data, int width, int height, float scale) ReadPfm(string path)
    {
        using var fs = File.OpenRead(path);
        using var br = new BinaryReader(fs, Encoding.Latin1);

        string header = ReadPfmLine(br);
        if (header != "Pf")
            throw new NotSupportedException(
                $"Only single-channel PFM ('Pf') is supported; got '{header}' in {path}.");

        string[] dims = ReadPfmLine(br).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        int width = int.Parse(dims[0]);
        int height = int.Parse(dims[1]);

        float scale = float.Parse(ReadPfmLine(br), CultureInfo.InvariantCulture);
        bool littleEnd = scale < 0;

        float[] px = new float[width * height];
        for (int i = 0; i < px.Length; i++)
        {
            byte[] bytes = br.ReadBytes(4);
            if (!littleEnd) Array.Reverse(bytes);
            px[i] = BitConverter.ToSingle(bytes, 0);
        }

        return (px, width, height, scale);
    }


    private static void WritePfm(string path, float[] data, int width, int height, float scale)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.Latin1);

        bw.Write(Encoding.Latin1.GetBytes(
            $"Pf\n{width} {height}\n{scale.ToString("G", CultureInfo.InvariantCulture)}\n"));

        bool littleEnd = scale < 0;
        foreach (float v in data)
        {
            byte[] bytes = BitConverter.GetBytes(v);
            if (!littleEnd) Array.Reverse(bytes);
            bw.Write(bytes);
        }
    }


    private static void WritePng(string path, float[] pixels, int fullW, int fullH)
    {
        float min = float.MaxValue, max = float.MinValue;

        // Global min/max scan (replaces the old 3/5ths playable area scan)
        for (int i = 0; i < pixels.Length; i++)
        {
            float v = pixels[i];
            if (float.IsNaN(v) || float.IsInfinity(v)) continue;
            if (v < min) min = v;
            if (v > max) max = v;
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
            bmp.Save(path, ImageFormat.Png);
        }
    }


    private static string ReadPfmLine(BinaryReader br)
    {
        var sb = new StringBuilder();
        while (br.BaseStream.Position < br.BaseStream.Length)
        {
            char c = (char)br.ReadByte();
            if (c == '\n') break;
            if (c != '\r') sb.Append(c);
        }
        return sb.ToString().Trim();
    }
}