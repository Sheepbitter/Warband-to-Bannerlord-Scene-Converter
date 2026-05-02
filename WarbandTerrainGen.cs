using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WarbandToBannerlordConverter;

// C# port of cmpxchg8b's TerrainGenerator, which can be found here, preserved by Swyter: https://github.com/Swyter/warband-terraingen/

// Warband's terrain info stored in the scene.sco file is not what one would expect. Typically, if a scene contains the vertices for a heightmap, you'd expect it to be just that, the terrain's heightmap.
// That is not how it works in Warband. The terrain layer information in the sco file is actually an offset from the generated terrain that the map was built on.
// So, for example, a scene with a terrain code of 0x00000001300389800003a4ea000058340000637a0000399b would generate that terrain in Warband, then offset the terrain by the heightmap information defined in the SCO.
// I don't know why TW decided to do it this way, perhaps they had already built their terrain generator and only added functionality for manual adjustments much later, so it may have been easier to just adjust from the terrain directly.
// Or perhaps it resulted in a smaller filesize. Regardless, in order to reconstruct a heightmap from Warband, you need to generate terrain from the terrain code exactly as warband does. Extract the terrain elevation from the SCO.
// Then finally add the two and export it as a greyscale heightmap for import into Bannerlord.

// So converting Warband's terrain to something compatible with Bannerlord would not have been possible without the prior work of cmpxchg8b and Swyter.
// The original code was made by cmpxchg8b, who reverse engineered the TaleWorlds terrain gen code and posted the tool the TaleWorlds forum in April of 2012.
// The original tool allows modders to generate accurate terrain vectors for converting into heightmaps or 3d objects to edit in 3rd party programs. This allows modders to diff the results of their final terrain from the terrain gen code and import the result back into warband.
// At some point it was completely lost, but in April of 2021, Swyter managed to recover the files from a dead hard drive and hosted it to the GitHub repo linked above.


// The C# below ports cmpxchg8b's TerrainGenerator precisely, which in turn was a precise re-engineering of Warbands Terrain Generator.
// Any deviations would result in the incorrect terrain and thus final incorrect heightmap to be reproduced when Porting from Warband to Bannerlord.
// It took a lot of trial and error to get this dialed in as even parts I thought were irrelevant, like some rock roughening, had a huge affect and had to be converted as well.
// So no touchy.

public sealed class TerrainParams
{
    public int    TerrainSeed   { get; set; }
    public int    RiverSeed     { get; set; }
    public int    FloraSeed     { get; set; }
    public int    DeepWater     { get; set; }
    public int    SizeX         { get; set; }
    public int    SizeY         { get; set; }
    public bool   ShadeOccluded { get; set; }
    public bool   PlaceRiver    { get; set; }
    public double Valley        { get; set; }
    public int    HillHeight    { get; set; }
    public int    Ruggedness    { get; set; }
    public double Vegetation    { get; set; }
    public int    RegionType    { get; set; }
    public string RegionName    { get; set; } = "";
    public int    RegionDetail  { get; set; }
    public int    DisableGrass  { get; set; }
    public int    PolygonSize   { get; set; }  
}


public static class WarbandTerrainGen
{

    public static TerrainParams ParseCode(string code)
    {
        code = code.Trim().ToLower();
        if (code.StartsWith("0x")) code = code[2..];
        code = code.PadLeft(48, '0');
        if (code.Length != 48)
            throw new ArgumentException(
                $"Expected 48 hex chars after prefix, got {code.Length}.");

        uint[] keys = new uint[6];
        for (int i = 0; i < 6; i++)
        {
            string chunk = code.Substring(48 - 8 * (i + 1), 8);
            keys[i] = Convert.ToUInt32(chunk, 16);
        }
        return DecodeKeys(keys);
    }

    private static TerrainParams DecodeKeys(uint[] k)
    {
        var p = new TerrainParams
        {
            TerrainSeed   = (int)(k[0] & 0x7FFFFFFF),
            RiverSeed     = (int)(k[1] & 0x7FFFFFFF),
            DeepWater     = (int)((k[1] >> 31) & 0x1),
            FloraSeed     = (int)(k[2] & 0x7FFFFFFF),
            SizeX         = (int)((k[3] >>  0) & 0x3FF),
            SizeY         = (int)((k[3] >> 10) & 0x3FF),
            ShadeOccluded = ((k[3] >> 30) & 0x1) != 0,
            PlaceRiver    = ((k[3] >> 31) & 0x1) != 0,
            Valley        = ((k[4] >>  0) & 0x7F) / 100.0,
            HillHeight    = (int)((k[4] >>  7) & 0x7F),
            Ruggedness    = (int)((k[4] >> 14) & 0x7F),
            Vegetation    = ((k[4] >> 21) & 0x7F) / 100.0,
            RegionType    = (int)((k[4] >> 28) & 0xF),
            RegionDetail  = (int)((k[5] >>  0) & 0x3),
            DisableGrass  = (int)((k[5] >>  2) & 0x3),
        };
        p.PolygonSize = p.RegionDetail + 2;
        p.RegionName  = RegionName(p.RegionType);
        return p;
    }

    private static string RegionName(int t) => t switch
    {
         0 => "ocean",
         1 => "mountain",
         2 => "steppe",
         3 => "plain",
         4 => "snow",
         5 => "desert",
         7 => "bridge",
         8 => "river",
         9 => "mountain_forest",
        10 => "steppe_forest",
        11 => "forest",
        12 => "snow_forest",
        13 => "desert_forest",
        21 => "shore",
        22 => "foam",
        23 => "waves",
        _  => $"unknown({t})"
    };

    public static (float minZ, float maxZ) WritePng(float[][] z, int width, int height, string path)
    {
        float minZ = float.MaxValue, maxZ = float.MinValue;
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
            {
                if (z[x][y] < minZ) minZ = z[x][y];
                if (z[x][y] > maxZ) maxZ = z[x][y];
            }
        float zRange = (maxZ - minZ == 0f) ? 1f : maxZ - minZ;

        int stride = width * 2;
        byte[] raw = new byte[height * (1 + stride)]; 
        for (int py = 0; py < height; py++)
        {
            int srcY = height - 1 - py; // reversed to flip axis
            int rowBase = py * (1 + stride);
            raw[rowBase] = 0;
            for (int px = 0; px < width; px++)
            {
                float norm = (z[px][srcY] - minZ) / zRange;
                ushort val = (ushort)(Math.Clamp(norm, 0f, 1f) * 65535f);
                raw[rowBase + 1 + px * 2]     = (byte)(val >> 8);
                raw[rowBase + 1 + px * 2 + 1] = (byte)(val & 0xFF);
            }
        }

        WritePngFile(path, width, height, raw);
        return (minZ, maxZ);
    }

    public static void WritePfm(float[][] z, int width, int height, string path)
    {
        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs, Encoding.Latin1);
        byte[] hdr = Encoding.Latin1.GetBytes($"Pf\n{width} {height}\n-1.0\n");
        bw.Write(hdr);
        for (int y = height - 1; y >= 0; y--)
            for (int x = width - 1; x >= 0; x--)
                bw.Write(z[x][y]);
    }

    private static void WritePngFile(string path, int width, int height, byte[] rawRows)
    {
        byte[] compressed = ZlibDeflate(rawRows);

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);

        bw.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });

        WriteChunk(bw, "IHDR", new byte[]
        {
            (byte)(width  >> 24), (byte)(width  >> 16), (byte)(width  >> 8), (byte)width,
            (byte)(height >> 24), (byte)(height >> 16), (byte)(height >> 8), (byte)height,
            16,
            0,
            0,
            0,
            0
        });

        WriteChunk(bw, "IDAT", compressed);
        WriteChunk(bw, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(BinaryWriter bw, string type, byte[] data)
    {
        uint len = (uint)data.Length;
        bw.Write(new byte[] {
            (byte)(len >> 24), (byte)(len >> 16), (byte)(len >> 8), (byte)len
        });
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        bw.Write(typeBytes);
        bw.Write(data);
        uint crc = Crc32(typeBytes, data);
        bw.Write(new byte[] {
            (byte)(crc >> 24), (byte)(crc >> 16), (byte)(crc >> 8), (byte)crc
        });
    }

    private static byte[] ZlibDeflate(byte[] data)
    {
        using var ms = new MemoryStream();
        ms.WriteByte(0x78);
        ms.WriteByte(0x9C);
        using (var ds = new System.IO.Compression.DeflateStream(ms,
                            System.IO.Compression.CompressionLevel.Optimal, leaveOpen: true))
        {
            ds.Write(data, 0, data.Length);
        }
        uint adler = Adler32(data);
        ms.WriteByte((byte)(adler >> 24));
        ms.WriteByte((byte)(adler >> 16));
        ms.WriteByte((byte)(adler >>  8));
        ms.WriteByte((byte)(adler));
        return ms.ToArray();
    }

    private static uint Adler32(byte[] data)
    {
        uint s1 = 1, s2 = 0;
        foreach (byte b in data) { s1 = (s1 + b) % 65521; s2 = (s2 + s1) % 65521; }
        return (s2 << 16) | s1;
    }

    private static readonly uint[] _crcTable = BuildCrcTable();
    private static uint[] BuildCrcTable()
    {
        var t = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            t[i] = c;
        }
        return t;
    }
    private static uint Crc32(byte[] a, byte[] b)
    {
        uint c = 0xFFFFFFFF;
        foreach (byte x in a) c = _crcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (byte x in b) c = _crcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFF;
    }
}

//MSVC rand() - must be identical or we get completely wrong generation.
internal static class MsvcRng
{
    private static uint _seed;

    public static void Srand(int seed) => _seed = (uint)(seed & 0x7FFFFFFF);

    // Returns value in [0, 32767] matching MSVC rand().
    public static int Rand()
    {
        _seed = (214013u * _seed + 2531011u) & 0xFFFFFFFF;
        return (int)((_seed >> 16) & 0x7FFF);
    }

    // Raw [0, 1) using same modulo as the original C++.
    public static double RandfRaw() => (Rand() % 15817) / 15817.0;

    public static double Randf() => RandfRaw();
    public static double Randf(double max) => RandfRaw() * max;
    public static double Randf(double min, double max) => RandfRaw() * (max - min) + min;

    public static int RandInt(int mod) => mod > 1 ? Rand() % mod : 0;
}


// Tables are verbatim from the C++ source
internal static class PerlinNoise
{
    private static readonly int[] PERM_TABLE =
    {
        198, 12, 146, 95, 44, 18, 240, 28, 151, 32, 45, 20, 30, 23, 141, 248,
        168, 254, 178, 85, 92, 216, 236, 175, 47, 88, 67, 136, 234, 1, 72, 106,
        79, 220, 24, 171, 26, 224, 128, 137, 223, 16, 105, 195, 231, 183, 29, 132,
        241, 122, 252, 135, 5, 181, 130, 213, 49, 204, 70, 176, 144, 35, 64, 104,
        19, 39, 46, 13, 246, 233, 9, 177, 31, 61, 154, 117, 129, 75, 139, 118,
        68, 42, 207, 86, 112, 27, 158, 8, 247, 250, 109, 163, 242, 52, 160, 164,
        43, 83, 62, 190, 57, 155, 169, 98, 6, 51, 38, 150, 80, 84, 114, 14,
        253, 194, 4, 138, 101, 25, 60, 97, 167, 188, 148, 209, 77, 192, 131, 227,
        36, 191, 237, 76, 214, 17, 251, 81, 71, 123, 119, 232, 0, 244, 87, 53,
        66, 126, 187, 110, 107, 2, 212, 93, 74, 211, 249, 63, 102, 113, 255, 11,
        173, 197, 166, 157, 226, 124, 90, 199, 59, 10, 100, 140, 89, 208, 221, 37,
        33, 230, 121, 174, 189, 165, 149, 34, 179, 15, 108, 40, 55, 91, 7, 200,
        147, 120, 134, 56, 184, 133, 115, 202, 245, 94, 82, 152, 116, 143, 210, 206,
        145, 203, 193, 127, 180, 125, 22, 159, 142, 156, 219, 111, 99, 217, 96, 41,
        162, 228, 3, 161, 58, 229, 153, 235, 215, 54, 205, 50, 218, 103, 69, 21,
        48, 186, 170, 73, 65, 222, 238, 225, 78, 172, 185, 196, 182, 239, 201, 243,
    };

    private static readonly double[] GRAD_TABLE =
    {
        -0.382332, 0.045987, 0.412422, 0.322819, -0.523555, 0.155692, -0.599637, 0.610314, 0.204491, -0.326772, 0.183249, 0.170122, 0.137437, 0.159912, 0.346638, 0.012269,
        -0.149943, -0.247321, 0.421695, -0.031413, 0.22226, 0.269062, 0.312575, -0.166291, -0.74937, -0.184944, 0.331976, 0.029522, 0.484716, 0.374884, 0.651325, 0.0,
        0.325664, 0.237639, -0.077213, 0.0, -0.599967, -0.599965, 0.0, -0.106802, 0.12898, 0.262476, 0.539146, -0.54082, -0.294115, -0.40226, -0.235177, 0.513393,
        -0.047496, -0.238059, -0.11023, -0.04151, 0.047293, 0.08302, 0.031514, -0.065262, -0.051344, 0.036846, 0.514855, -0.309666, 0.312743, -0.15927, -0.225656, 0.473331,
        -0.343894, 0.361597, -0.352909, 0.079702, -0.477326, -0.180476, 0.212372, 0.185283, -0.080332, 0.006545, 0.049649, -0.337633, 0.364485, -0.607989, -0.179383, 0.380025,
        -0.126611, 0.229949, -0.167068, 0.789544, -0.879327, 0.0, -0.104328, 0.061701, 0.558347, 0.643778, -0.016567, -0.00871, -0.026806, 0.036742, -0.082902, -0.210578,
        -0.686211, 0.498557, 0.205545, 0.05053, -0.184377, -0.10106, -0.396777, 0.114284, 0.142954, -0.39971, -0.370211, 0.799427, 0.010625, -0.360047, -0.285405, -0.57573,
        0.45687, 0.222734, 0.564252, 0.774509, -0.124628, -0.175417, -0.05755, 0.049107, 0.126588, 0.389595, -0.119535, -0.006558, -0.190696, 0.038741, 0.090039, -0.048335,
        -0.180079, -0.043774, 0.029059, -0.011331, 0.073884, -0.145004, 0.0, -0.488167, -0.489682, 0.266305, -0.048211, -0.254391, -0.078009, 0.301849, 0.074496, -0.133721,
        -0.378882, 0.449358, -0.252802, -0.026044, 0.080153, 0.069205, -0.05561, -0.107745, 0.073953, 0.022285, 0.0, 0.321538, -0.000819, 0.286642, -0.076246, 0.042583,
        0.058611, 0.0, -0.454821, 0.260599, 0.665373, 0.08596, -0.01242, -0.292721, -0.08383, -0.35935, 0.107285, -0.163418, 0.11873, -0.878918, -0.215777, -0.389136,
        -0.211051, -0.307529, 0.0, -0.322809, -0.623562, 0.172665, -0.57749, -0.424905, 0.082658, -0.205316, 0.083868, 0.258113, -0.753892, -0.319969, 0.0, -0.725878,
        -0.006615, 0.004806, -0.072567, 0.464495, -0.077344, 0.35127, -0.016793, -0.093071, -0.166656, 0.060751, 0.840006, -0.362233, -0.198387, 0.225023, 0.936188, -0.444758,
        -0.11514, -0.354659, 0.014497, -0.029373, -0.046912, 0.526206, -0.445624, -0.362435, -0.231729, -0.289542, 0.182363, 0.12552, -0.057606, -0.044101, 0.055588, -0.049504,
        -0.082013, -0.845759, -0.470834, -0.068169, 0.082669, 0.041471, 0.013118, 0.259258, 0.157987, 0.511734, 0.276396, -0.134263, -0.265539, 0.500715, 0.231015, 0.445317,
        -0.311734, 0.103178, 0.137202, 0.011978, -0.152683, -0.413649, -0.013775, 0.217392, -0.341425, 0.287605, -0.131299, 0.022249, -0.023086, 0.0, -0.081268, 0.059899,
        0.004714, 0.0, -0.284805, -0.603364, -0.20102, 0.140391, 0.316763, -0.804606, 0.426147, -0.103548, 0.111007, -0.276499, -0.264919, -0.271293, 0.821277, 0.124308,
        0.4558, -0.632242, -0.694367, 0.167233, 0.013052, 0.025603, 0.018478, 0.070566, -0.104252, -0.063714, 0.028317, 0.023133, 0.034385, 0.501435, -0.430703, -0.475005,
        -0.17456, -0.766047, 0.349119, -0.164142, 0.056665, -0.38817, -0.763245, 0.388889, 0.0, 0.444891, -0.079174, 0.390493, 0.15998, -0.057249, 0.13463, 0.163417,
        -0.229607, -0.192898, 0.288537, -0.390402, -0.284583, -0.075388, -0.805658, 0.565244, -0.193679, -0.078991, 0.024717, -0.045931, 0.111235, -0.074319, -0.665672, -0.344944,
        0.411413, -0.096546, -0.583653, -0.324629, 0.40793, 0.10454, 0.660048, -0.820568, 0.267792, 0.112261, 0.66301, -0.575374, -0.320677, -0.573242, 0.416483, -0.437923,
        0.029031, 0.562818, -0.514251, -0.688641, 0.3112, 0.134575, -0.113951, -0.185842, 0.588785, 0.07939, -0.03214, 0.023769, 0.221999, -0.259927, 0.0, 0.816506,
        0.454549, 0.065812, 0.003058, 0.003097, 0.000617, 0.149211, 0.008463, -0.140542, 0.353697, -0.301407, -0.286977, -0.444848, 0.388932, -0.262564, 0.10957, -0.237067,
        0.383215, -0.843997, -0.164186, -0.407824, 0.748646, -0.176676, 0.310143, 0.017268, 0.448852, 0.085881, 0.001312, 0.013876, 0.043496, -0.031838, -0.277795, 0.019677,
        0.037472, -0.974004, 0.186361, -0.596817, 0.433611, -0.329135, -0.1038, 0.016461, -0.067573, 0.602512, 0.60251, 0.0, 0.055517, 0.517075, -0.111035, -0.193866,
        0.04815, 0.028228, -0.073225, -0.07015, 0.178692, 0.231196, -0.405436, 0.534878, -0.008113, -0.024969, -0.092419, -0.51487, -0.415516, -0.472159, 0.483225, 0.254046,
        0.781881, -0.016453, 0.123169, -0.312721, -0.182725, -0.214054, -0.760135, 0.574751, -0.381369, -0.209033, -0.370622, 0.39484, 0.54037, -0.221661, -0.523415, -0.207639,
        0.165177, -0.508353, -0.330354, 0.292668, 0.900738, 0.0, 0.093909, 0.023324, -0.013674, -0.413931, -0.048747, -0.189258, -0.045809, -0.386021, 0.313923, 0.457278,
        0.591863, 0.592619, -0.361923, 0.08765, 0.390406, 0.09321, 0.250343, -0.057607, 0.256967, 0.037128, -0.875059, -0.258041, 0.382945, 0.640064, 0.004503, -0.005276,
        0.018734, -0.124908, -0.20383, 0.0, -0.165268, 0.240234, -0.299769, 0.72198, 0.396683, 0.17599, 0.528949, -0.214708, 0.342476, 0.47955, -0.122894, 0.775932,
        -0.030996, 0.0, 0.134868, -0.014594, 0.044915, -0.017316, 0.58016, -0.498274, 0.471095, 0.160296, 0.142754, -0.236497, 0.074791, -0.142261, -0.046224, 0.412773,
        -0.479527, -0.25511, -0.228631, 0.399995, 0.164699, 0.299747, -0.53561, -0.495665, -0.039875, -0.02897, 0.05948, -0.259657, 0.18865, 0.336903, -0.13525, -0.416248,
        0.472116, -0.127893, 0.074129, -0.343279, -0.005685, 0.009164, -0.001274, -0.691049, -0.350751, 0.298473, -0.031537, 0.217033, 0.063074, -0.186958, 0.404504, -0.653873,
        0.041271, -0.576689, -0.346857, -0.132702, -0.041688, 0.095206, 0.249581, 0.181331, 0.332778, 0.046968, -0.034124, 0.515215, -0.576599, -0.07244, 0.304896, -0.494652,
        -0.673044, 0.349003, 0.763427, 0.277402, 0.164917, -0.131499, 0.064191, -0.288233, 0.34798, 0.096356, 0.32227, -0.131006, 0.137525, 0.477995, 0.194294, 0.112615,
        0.521505, 0.173785, 0.070542, 0.11252, 0.143387, 0.129961, -0.245641, 0.545331, -0.273569, 0.086535, -0.067099, 0.04875, -0.120032, 0.309317, 0.224729, -0.532433,
        0.217197, 0.157801, -0.609051, 0.032277, -0.002693, -0.004582, 0.016443, -0.005291, 0.004595, 0.06551, -0.534217, 0.255287, 0.385232, -0.61746, -0.201973, 0.049956,
        0.036295, -0.217366, -0.071771, -0.155923, -0.077943, 0.396951, -0.05768, -0.574478, -0.188498, 0.053955, 0.27177, 0.49011, 0.581275, 0.327016, -0.002596, -0.003844,
        0.000942, -0.276486, 0.352783, 0.026408, -0.067877, 0.614233, -0.708215, -0.014551, -0.095078, 0.058666, -0.552212, 0.696821, 0.296471, 0.029568, -0.091002, 0.265439,
        0.740368, -0.154013, -0.270329, -0.115748, 0.135523, 0.0, 0.423776, 0.370509, 0.250127, -0.248043, -0.484951, -0.209789, -0.000314, -0.05746, -0.024009, 0.273355,
        -0.259453, 0.137294, -0.037477, -0.115346, -0.575826, 0.274724, 0.142359, -0.169791, 0.037665, -0.027365, -0.031991, -0.35933, 0.636097, 0.051412, 0.101262, 0.209703,
        -0.164983, -0.200361, -0.278973, -0.143513, -0.133635, 0.166975, 0.105166, -0.272142, -0.19772, 0.353101, 0.520612, 0.708366, -0.367319, 0.273803, -0.126571, 0.369126,
        -0.034668, -0.165782, -0.214985, 0.167786, -0.905754, -0.335573, 0.741254, 0.239782, 0.101324, 0.673682, 0.276876, -0.184366, -0.079911, 0.058058, 0.468966, -0.09032,
        -0.10712, -0.060264, -0.476929, -0.48766, -0.632836, 0.020207, 0.574117, -0.040414, -0.503039, 0.365478, -0.510581, 0.115311, 0.130793, -0.544152, 0.056282, 0.794572,
        -0.112563, 0.488661, 0.497362, -0.166646, -0.215566, 0.385861, 0.540876, 0.795336, -0.403683, -0.343516, -0.234981, 0.723209, -0.380219, -0.045123, 0.306946, 0.287834,
        0.031085, 0.083707, -0.00629, 0.311298, -0.22617, 0.171675, -0.137457, 0.224179, 0.710243, -0.279656, 0.499711, 0.462443, 0.831447, 0.131686, 0.0, -0.309519,
        -0.109618, 0.619045, 0.159594, 0.046698, -0.131755, -0.17193, 0.147663, -0.139609, -0.261018, -0.127415, -0.572127, -0.181165, -0.557579, -0.707514, -0.709104, 0.170238,
        0.0, 0.01268, 0.039026, 0.077985, -0.316095, -0.03802, 0.340972, 0.167454, 0.51538, 0.495246, 0.126888, 0.136979, 0.228492, 0.214834, 0.456259, -0.107739,
    };

    private static int Perm(int x) => PERM_TABLE[x & 0xFF];

    private static (double gx, double gy, double gz) Gradient(int x)
    {
        int idx = 3 * Perm(x & 0xFF);
        return (GRAD_TABLE[idx], GRAD_TABLE[idx + 1], GRAD_TABLE[idx + 2]);
    }

    /// <summary>Exact port of Perlin() from Perlin.cpp.</summary>
    public static (double ox, double oy, double oz) Perlin3d(double factor, double px, double py, double pz)
    {
        if (factor == 0.0) return (0.0, 0.0, 0.0);

        double xd  = (px + 1.0) / factor;
        double yd  = (py + 1.0) / factor;
        double zd  = (pz + 1.0) / factor;
        int    xdi = (int)Math.Floor(xd);
        int    ydi = (int)Math.Floor(yd);
        int    zdi = (int)Math.Floor(zd);
        double xm  = xd - xdi;
        double ym  = yd - ydi;
        double zm  = zd - zdi;
        double xm1 = xm - 1.0;
        double ym1 = ym - 1.0;
        double zm1 = zm - 1.0;
        double xmm   = xm  * xm;
        double ymm   = ym  * ym;
        double zmm   = zm  * zm;
        double xm1m  = xm1 * xm1;
        double ym1m  = ym1 * ym1;
        double zm1m  = zm1 * zm1;

        int xp    = Perm(xdi);
        int xp1   = Perm(xdi + 1);
        int xpy   = Perm(xp  + ydi);
        int xpy1  = Perm(xp  + ydi + 1);
        int xp1y  = Perm(xp1 + ydi);
        int xp1y1 = Perm(xp1 + ydi + 1);
        int zp    = zdi & 0xFF;
        int zp1   = (zdi + 1) & 0xFF;

        double xmm3m  = 1.0 - 3.0 * xmm  + (xmm  * 2) * xm;
        double ymm3m  = 1.0 - 3.0 * ymm  + (ymm  * 2) * ym;
        double zmm3m  = 1.0 - 3.0 * zmm  + (zmm  * 2) * zm;
        double xm1m3m = 1.0 - 3.0 * xm1m - (xm1m * 2) * xm1;
        double ym1m3m = 1.0 - 3.0 * ym1m - (ym1m * 2) * ym1;
        double zm1m3m = 1.0 - 3.0 * zm1m - (zm1m * 2) * zm1;

        double pxyz    = xmm3m  * ymm3m  * zmm3m;
        double pxyz1   = xmm3m  * ymm3m  * zm1m3m;
        double pxy1z   = xmm3m  * ym1m3m * zmm3m;
        double pxy1z1  = xmm3m  * ym1m3m * zm1m3m;
        double px1yz   = xm1m3m * ymm3m  * zmm3m;
        double px1yz1  = xm1m3m * ymm3m  * zm1m3m;
        double px1y1z  = xm1m3m * ym1m3m * zmm3m;
        double px1y1z1 = xm1m3m * ym1m3m * zm1m3m;

        double ox = 0, oy = 0, oz = 0;

        void Accum(int tableIdx, double wx, double wy, double wz, double pw)
        {
            var (gx, gy, gz) = Gradient(tableIdx);
            ox += wx * pw * gx;
            oy += wy * pw * gy;
            oz += wz * pw * gz;
        }

        Accum(xpy   + zp,  xm,  ym,  zm,  pxyz);
        Accum(xpy   + zp1, xm,  ym,  zm1, pxyz1);
        Accum(xpy1  + zp,  xm,  ym1, zm,  pxy1z);
        Accum(xpy1  + zp1, xm,  ym1, zm1, pxy1z1);
        Accum(xp1y  + zp,  xm1, ym,  zm,  px1yz);
        Accum(xp1y  + zp1, xm1, ym,  zm1, px1yz1);
        Accum(xp1y1 + zp,  xm1, ym1, zm,  px1y1z);
        Accum(xp1y1 + zp1, xm1, ym1, zm1, px1y1z1);

        return (ox, oy, oz);
    }

    public static (double sx, double sy, double sz) PerlinOctave(
        double frequency, double gain, int numOctaves,
        double px, double py, double pz)
    {
        double sx = 0, sy = 0, sz = 0;
        double amplitude = 1.0;
        double norm = 0.0;

        for (int i = 0; i <= numOctaves; i++)
        {
            var (nx, ny, nz) = Perlin3d(amplitude * frequency, px, py, pz);
            sx   += nx * amplitude;
            sy   += ny * amplitude;
            sz   += nz * amplitude;
            norm += amplitude;
            amplitude *= gain;
            gain = 0.61;
        }
        return (sx / norm, sy / norm, sz / norm);
    }
}


public sealed class TerrainGenerator
{

    private const int    MIN_FACES   = 40;
    private const int    MAX_FACES   = 250;
    private const double PI          = 3.14159265;
    private const double LOG2_E      = 1.44269504;
    private const int    RLT_BASE    = 2;
    private const int    RLT_EXPANDED= 4;

    private static readonly (int dx, int dy)[] POSSIBLE_MOVES =
    {
        ( 0,  1), ( 1,  1), ( 1,  0), ( 1, -1),
        ( 0, -1), (-1, -1), (-1,  0), (-1,  1),
        ( 0,  0),
    };
    private static readonly int[] POSSIBLE_MOVE_DISTANCES = { 2, 1, 2, 1, 2, 1, 2, 1 };

    private readonly TerrainParams _p;
    private readonly int    _detail;
    private readonly int    _hillHeight;
    private readonly double _valley;
    private readonly int    _ruggedness;
    private readonly bool   _placeRiver;
    private readonly int    _deepWater;

    public int Nx { get; }
    public int Ny { get; }
    private readonly int _sizeX;
    private readonly int _sizeY;

    private readonly float[][] _z;
    private readonly double[][] _slope;
    private readonly int[][]    _terrainFlags;
    private readonly double[][] _vnormZ;
    private readonly double[][] _earthIntensity;
    private readonly double[][] _greenIntensity;

    private double _barrenness    = 0.19;
    private bool   _hasGreenLayer = true;

    private double _noiseFreq;
    private (double x, double y, double z) _posRand;

    public TerrainGenerator(TerrainParams p)
    {
        _p          = p;
        _detail     = p.PolygonSize;
        _hillHeight = p.HillHeight;
        _valley     = p.Valley;
        _ruggedness = p.Ruggedness;
        _placeRiver = p.PlaceRiver;
        _deepWater  = p.DeepWater;

        Nx = Clamp(p.SizeX / _detail, MIN_FACES, MAX_FACES);
        Ny = Clamp(p.SizeY / _detail, MIN_FACES, MAX_FACES);
        _sizeX = Nx * _detail;
        _sizeY = Ny * _detail;

        int W = Nx + 1, H = Ny + 1;

        _z              = Alloc2D<float> (W, H, 0f);
        _slope          = Alloc2D<double>(W, H, 1.0);
        _terrainFlags   = Alloc2D<int>   (W, H, 0);
        _vnormZ         = Alloc2D<double>(W, H, 1.0);
        _earthIntensity = Alloc2D<double>(W, H, 0.0);
        _greenIntensity = Alloc2D<double>(W, H, 0.0);
    }

    public void Generate()
    {
        MsvcRng.Srand(_p.TerrainSeed);
        GenerateLayers();
        GenerateTerrain();
        MsvcRng.Srand(_p.RiverSeed);
        if (_placeRiver) GenerateRiver();
        SmoothHeight();
        ComputeNormals();
        ComputeVertexLayerIntensities();
        RoughenRockVertices();
    }

    public (float[][] z, int width, int height) GetHeightmap()
        => (_z, Nx + 1, Ny + 1);

    // Layer setup - mirrors C++ generateLayers
    private void GenerateLayers()
    {
        MsvcRng.Rand();
        MsvcRng.Rand();

        _barrenness    = 0.19;
        _hasGreenLayer = true;

        switch (_p.RegionType)
        {
            case 2:
                MsvcRng.Rand();
                break;
            case 4:
            case 12:
                _hasGreenLayer = false;
                break;
            case 5:
            case 13:
                _barrenness    = 0.26;
                _hasGreenLayer = false;
                break;
        }
    }

    private void ComputeNormals()
    {
        int d = _detail;

        double[][] vnx = Alloc2D<double>(Nx + 1, Ny + 1, 0.0);
        double[][] vny = Alloc2D<double>(Nx + 1, Ny + 1, 0.0);
        double[][] vnz = Alloc2D<double>(Nx + 1, Ny + 1, 0.0);

        for (int fy = 0; fy < Ny; fy++)
        {
            for (int fx = 0; fx < Nx; fx++)
            {
                (double, double, double)[] p = new (double, double, double)[4];
                p[0] = (fx * d,       fy * d,       _z[fx  ][fy  ]);
                p[1] = ((fx+1) * d,   fy * d,       _z[fx+1][fy  ]);
                p[2] = ((fx+1) * d,   (fy+1) * d,   _z[fx+1][fy+1]);
                p[3] = (fx * d,       (fy+1) * d,   _z[fx  ][fy+1]);

                foreach (int[] tri in new[] { new[]{0,1,2}, new[]{0,2,3} })
                {
                    var (x0, y0, z0) = p[tri[0]];
                    var (x1, y1, z1) = p[tri[1]];
                    var (x2, y2, z2) = p[tri[2]];

                    double ax = x1-x0, ay = y1-y0, az = z1-z0;
                    double bx = x2-x0, by = y2-y0, bz = z2-z0;
                    double cx = ay*bz - az*by;
                    double cy = az*bx - ax*bz;
                    double cz = ax*by - ay*bx;
                    double len = Math.Sqrt(cx*cx + cy*cy + cz*cz);
                    if (len > 1e-10) { cx /= len; cy /= len; cz /= len; }

                    foreach (int vi in tri)
                    {
                        int vxi = fx + (vi == 1 || vi == 2 ? 1 : 0);
                        int vyi = fy + (vi == 2 || vi == 3 ? 1 : 0);
                        vnx[vxi][vyi] += cx;
                        vny[vxi][vyi] += cy;
                        vnz[vxi][vyi] += cz;
                    }
                }
            }
        }

        for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
            {
                double len = Math.Sqrt(vnx[x][y]*vnx[x][y] + vny[x][y]*vny[x][y] + vnz[x][y]*vnz[x][y]);
                _vnormZ[x][y] = len > 1e-10 ? vnz[x][y] / len : 1.0;
            }
    }

    private void ComputeVertexLayerIntensities()
    {
        for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
            {
                if (_z[x][y] <= 10000f)
                {
                    double slope = 1.0 - _vnormZ[x][y];
                    _earthIntensity[x][y] = 1.0 - Clamp01((slope - _barrenness) * 12.0);
                }

                if (_hasGreenLayer)
                {
                    var (nx2, ny2, nz2) = PerlinNoise.PerlinOctave(15.0, 0.6, 3, x, y, 0.0);
                    double intensity = Clamp01((nx2 + ny2 + nz2) * 4.5 + 0.7);
                    _greenIntensity[x][y] = _earthIntensity[x][y] * intensity;
                    if (intensity > 0.99) _earthIntensity[x][y] = 0.0;
                }
            }
    }

    // Rock roughening - Smaller steps like this did actually matter in a big way.
    private void RoughenRockVertices()
    {
        for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
                if (_earthIntensity[x][y] + _greenIntensity[x][y] < 0.5)
                {
                    double increase = (MsvcRng.RandfRaw() - 0.5)
                                      * (1.0 - _earthIntensity[x][y])
                                      * _detail * 0.7;
                    _z[x][y] += (float)increase;
                }
    }
    private void GenerateTerrain()
    {
        double sizeAvg    = (_sizeX + _sizeY) * 0.5;
        double sizeFactor = sizeAvg * MsvcRng.Randf(1.0, 1.5);
        double noiseFreq  = (_sizeX + _sizeY) * 0.2 * MsvcRng.Randf(1.0, 1.5);
        _noiseFreq = noiseFreq;

        double valley = _valley;
        double prx    = MsvcRng.Randf(10000.0);
        double pry    = MsvcRng.Randf(10000.0);
        double prz    = MsvcRng.Randf(10000.0);
        _posRand = (prx, pry, prz);

        for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
            {
                double px = x * _detail + prx;
                double py = y * _detail + pry;
                double pz =               prz;
                var (nx2, ny2, nz2) = PerlinNoise.PerlinOctave(noiseFreq, 0.6, 1, px, py, pz);
                double sf = Clamp01((nx2 + ny2 + nz2) * valley * 9.0);
                _slope[x][y] = (Math.Cos(sf * PI) + 1.0) * 0.45 + 0.1;
            }

        double altRand = MsvcRng.Randf(7.0, 10.0);
        for (int i = 0; i < 2; i++)
        {
            for (int j = 0; j < 13; j++)
            {
                double altFactor = Math.Sin((1.0 - j / 13.0) * 3.2) * altRand;
                int bound = (i == 0) ? Ny : Nx;
                for (int kk = 0; kk <= bound; kk++)
                {
                    int x1 = (i == 0) ? j         : kk;
                    int y1 = (i == 0) ? kk         : j;
                    int x2 = (i == 0) ? Nx - j     : kk;
                    int y2 = (i == 0) ? kk         : Ny - j;

                    foreach ((int vx, int vy) in new[] { (x1, y1), (x2, y2) })
                    {
                        if (vx < 0 || vx > Nx || vy < 0 || vy > Ny) continue;
                        double px = vx * _detail + prx;
                        double py = vy * _detail + pry;
                        double pz =                prz;
                        var (_, _, nz2) = PerlinNoise.PerlinOctave(40.0, 0.6, 4, px, py, pz);
                        double alt = (nz2 + nz2 + 0.4) * altFactor;
                        if (alt > _z[vx][vy]) _z[vx][vy] = (float)alt;
                    }
                }
            }
        }

        int    rugClamped  = ClampExcl(_ruggedness, 0, 100);
        double hillFactor  = Math.Max(sizeFactor / (Math.Pow(2.0, (100 - rugClamped) * 0.045) * 4.0), 2.0);
        int    numPasses   = (int)(Math.Log(hillFactor) * LOG2_E);
        double heightFactor = 1.0;

        for (int i = 0; i <= numPasses; i++)
        {
            double density = (i == numPasses)
                ? (hillFactor - (1 << numPasses)) / (double)(1 << numPasses)
                : 1.0;
            GenerateHills(sizeFactor / (1 << i), density, heightFactor);
            heightFactor -= 0.05;
        }

        float minZ = float.MaxValue;
        for (int x = 0; x <= Nx; x++)
            for (int y = 0; y <= Ny; y++)
                if (_z[x][y] < minZ) minZ = _z[x][y];
        for (int x = 0; x <= Nx; x++)
            for (int y = 0; y <= Ny; y++)
                _z[x][y] -= minZ;
    }

    private void GenerateHills(double widthFactor, double densityFactor, double heightFactor)
    {
        int numHills = (int)(
            RoundVal(_sizeX * densityFactor * _sizeY / (widthFactor * widthFactor * 0.25))
            * MsvcRng.Randf(0.4, 0.6) * 30.0
        );

        for (int h = 0; h < numHills; h++)
        {
            double r4 = (MsvcRng.RandfRaw() + MsvcRng.RandfRaw()
                       + MsvcRng.RandfRaw() + MsvcRng.RandfRaw()) / 4.0;
            double radius  = (r4 * 0.5 + 0.05) * widthFactor;
            double py2     = (_sizeY + widthFactor * 2) * MsvcRng.RandfRaw() - widthFactor;
            double px2     = (_sizeX + widthFactor * 2) * MsvcRng.RandfRaw() - widthFactor;
            double height  = MsvcRng.Randf(0.2, 0.3) * radius * heightFactor;
            GenerateHill(px2, py2, radius, height);
        }
    }

    private void GenerateHill(double hx, double hy, double radius, double height)
    {
        int d = _detail;
        int minX = Math.Max(RoundVal(Math.Ceiling( (hx - radius) / d)), 0);
        int maxX = Math.Min(RoundVal(Math.Floor(   (hx + radius) / d)), Nx);
        int minY = Math.Max(RoundVal(Math.Ceiling( (hy - radius) / d)), 0);
        int maxY = Math.Min(RoundVal(Math.Floor(   (hy + radius) / d)), Ny);

        for (int y = minY; y <= maxY; y++)
            for (int x = minX; x <= maxX; x++)
            {
                double vx   = x * d;
                double vy   = y * d;
                double dx   = vx - hx;
                double dy   = vy - hy;
                double dist = Math.Sqrt(dx*dx + dy*dy) / radius;
                if (dist < 1.0)
                    _z[x][y] += (float)(
                        (Math.Cos(dist * PI) + 1.0) * 0.5
                        * height * _slope[x][y]
                        * _hillHeight * 0.01
                    );
            }
    }

    private void GenerateRiver()
    {
        int[] start = { 0, 0 };
        int[] end   = { 0, 0 };
        bool riverPlaced = false;

        for (int attempt = 0; attempt < 4 && !riverPlaced; attempt++)
        {
            int tries = 20;
            double bestNoise = -10000.0;
            int x = 0, y = 0;

            while (tries > 0)
            {
                if (MsvcRng.RandfRaw() >= 0.5)
                {
                    x = MsvcRng.RandInt(Nx - 3) + 3;
                    y = MsvcRng.Rand() % 4 + 3;
                    if (MsvcRng.RandfRaw() < 0.5) y = Ny - y;
                }
                else
                {
                    x = MsvcRng.Rand() % 4 + 3;
                    if (MsvcRng.RandfRaw() < 0.5) x = Nx - x;
                    y = MsvcRng.RandInt(Ny - 3) + 3;
                }
                double noise = GetVertexNoise(start[0], start[1]);
                if (noise >= bestNoise) { bestNoise = noise; start[0] = x; start[1] = y; }
                tries--;
            }

            bool endFound = false;
            tries = 50;
            while (tries > 0 && !endFound)
            {
                if (MsvcRng.RandfRaw() >= 0.5)
                {
                    end[1] = 0;
                    if (MsvcRng.RandfRaw() < 0.5) end[1] = Ny + 1;
                    end[0] = MsvcRng.RandInt(Nx + 1);
                }
                else
                {
                    end[0] = 0;
                    if (MsvcRng.RandfRaw() < 0.5) end[0] = Nx + 1;
                    end[1] = MsvcRng.RandInt(Ny + 1);
                }
                int ddx = Math.Abs(end[0] - start[0]);
                int ddy = Math.Abs(end[1] - start[1]);
                if ((Nx + Ny) * 0.7 < ddx + ddy) endFound = true;
                tries--;
            }

            if (endFound)
            {
                double depth = PlaceRiver(start, end, RLT_BASE, 1.5, 0.9);
                if (depth >= 3.5)
                    RemoveFlags((1 << RLT_BASE) | (1 << RLT_EXPANDED));
                else
                    riverPlaced = true;
            }
        }

        if (riverPlaced)
        {
            double riverDepth = _deepWater != 0 ? -2.3 : -1.3;
            SetTypeDepth(RLT_BASE, riverDepth);
            SmoothRiverHeight(RLT_BASE, 0.5);
        }
    }

    private double PlaceRiver(int[] start, int[] end, int terrainType, double globalDir, double globalHeight)
    {
        double depth = 10000.0;
        double ddx   = end[0] - start[0];
        double ddy   = end[1] - start[1];
        double dlen  = Math.Sqrt(ddx*ddx + ddy*ddy);
        if (dlen > 0) { ddx /= dlen; ddy /= dlen; }

        int curX = start[0], curY = start[1];
        int numCurves = 0;
        int terrainFlag = 1 << RLT_BASE;
        _terrainFlags[curX][curY] |= terrainFlag;

        (double dx, double dy)[] pd = new (double, double)[8];
        for (int i = 0; i < 8; i++)
        {
            var (mx, my) = POSSIBLE_MOVES[i];
            double plen = Math.Sqrt(mx*mx + my*my);
            pd[i] = plen > 0 ? (mx / plen, my / plen) : (0.0, 0.0);
        }

        bool atEnd = false;

        while (true)
        {
            double cdx = end[0] - curX;
            double cdy = end[1] - curY;
            double clen = Math.Sqrt(cdx*cdx + cdy*cdy);
            if (clen > 0) { cdx /= clen; cdy /= clen; }

            double[] likeliness = new double[8];
            for (int i = 0; i < 8; i++)
            {
                var (mx, my) = POSSIBLE_MOVES[i];
                int nx2 = curX + mx;
                int ny2 = curY + my;

                int distBounds = Math.Max(20 - nx2, 0) + Math.Max(nx2 + 20 - Nx, 0)
                               + Math.Max(20 - ny2, 0) + Math.Max(ny2 + 20 - Ny, 0);
                double placement = 0.0;
                if (distBounds > 0)
                {
                    if ((nx2 <= 20 && pd[i].dx <= 0) ||
                        (ny2 <= 20 && pd[i].dy <= 0) ||
                        (nx2 >= Nx - 20 && pd[i].dx >= 0) ||
                        (ny2 >= Ny - 20 && pd[i].dy >= 0))
                    {
                        placement = Math.Clamp((double)distBounds, 0, 24) * -0.04;
                    }
                }

                if (nx2 < 0 || nx2 > Nx || ny2 < 0 || ny2 > Ny)
                {
                    likeliness[i] = (pd[i].dx * ddx + pd[i].dy * ddy + 1.0) / 2.0;
                }
                else if ((_terrainFlags[nx2][ny2] & terrainFlag) == 0)
                {
                    double heightDiff = (_z[curX][curY] - _z[nx2][ny2]) * globalHeight * 3.0;
                    if (heightDiff < 0) heightDiff *= 0.25 - _hillHeight * 0.0022;
                    double dirDot  = pd[i].dx * ddx  + pd[i].dy * ddy;
                    double cdirDot = pd[i].dx * cdx  + pd[i].dy * cdy;
                    likeliness[i] = heightDiff
                                  + (dirDot  + 0.7) * 0.5 * globalDir
                                  + (cdirDot + 0.7) * 0.5 * 0.65
                                  + placement;
                }
                else
                {
                    if (_terrainFlags[nx2][ny2] != 0 && numCurves - 30 > 0) atEnd = true;
                    likeliness[i] = -1.0;
                }
            }

            for (int i = 0; i < 8; i++)
                if (likeliness[i] > 0) likeliness[i] = likeliness[i] * likeliness[i] * likeliness[i];

            double total = 0;
            for (int i = 0; i < 8; i++) if (likeliness[i] > 0) total += likeliness[i];

            double threshold = total * MsvcRng.RandfRaw();
            double running   = 0;
            int    selected  = -1;
            for (int i = 0; i < 8; i++)
            {
                if (likeliness[i] > 0)
                {
                    if (running <= threshold && threshold < likeliness[i] + running)
                        selected = i;
                    running += likeliness[i];
                }
            }

            if (selected != -1)
            {
                var (mx, my) = POSSIBLE_MOVES[selected];
                int newX = curX + mx, newY = curY + my;
                if (newX >= 3 && newX <= Nx - 3 && newY >= 3 && newY <= Ny - 3
                    && likeliness[selected] > 0)
                {
                    curX = newX; curY = newY;
                    ddx = ddx * 0.9 + pd[selected].dx * 0.1;
                    ddy = ddy * 0.9 + pd[selected].dy * 0.1;
                    dlen = Math.Sqrt(ddx*ddx + ddy*ddy);
                    if (dlen > 0) { ddx /= dlen; ddy /= dlen; }
                    numCurves++;
                    _terrainFlags[curX][curY] |= terrainFlag;
                    depth = Math.Min(depth, _z[curX][curY]);
                    if (numCurves < 100000 && !atEnd) continue;
                }
            }
            break;
        }

        return depth;
    }

    private void RemoveFlags(int flags)
    {
        for (int x = 0; x <= Nx; x++)
            for (int y = 0; y <= Ny; y++)
                _terrainFlags[x][y] &= ~flags;
    }

    private void SetTypeDepth(int typeId, double depth)
    {
        int flag = 1 << typeId;
        for (int x = 0; x <= Nx; x++)
            for (int y = 0; y <= Ny; y++)
                if ((_terrainFlags[x][y] & flag) != 0)
                    _z[x][y] = (float)depth;
    }

    private void SmoothRiverHeight(int typeId, double smoothFactor)
    {
        int flag = 1 << typeId;
        for (int x = 0; x <= Nx; x++)
            for (int y = 0; y <= Ny; y++)
            {
                if ((_terrainFlags[x][y] & flag) != 0) continue;
                int    smoothType = 0;
                double height     = 0.0;
                for (int i = 0; i < 8; i++)
                {
                    var (mx, my) = POSSIBLE_MOVES[i];
                    int nx2 = x + mx, ny2 = y + my;
                    if (nx2 < 0 || nx2 > Nx || ny2 < 0 || ny2 > Ny) continue;
                    if ((_terrainFlags[nx2][ny2] & flag) == 0) continue;
                    int nt = POSSIBLE_MOVE_DISTANCES[i];
                    if (nt == 2 || (nt == 1 && smoothType == 0)) smoothType = nt;
                    height = _z[nx2][ny2];
                }
                if (smoothType != 0)
                {
                    double f = smoothFactor * (smoothType == 1 ? 0.7 : 1.0);
                    _z[x][y] += (float)((height - _z[x][y]) * f);
                }
            }
    }

    private void SmoothHeight()
    {
        for (int y = 0; y <= Ny; y++)
            for (int x = 0; x <= Nx; x++)
            {
                double hSum = _z[x][y];
                double count = 1.0;
                for (int i = 0; i < 8; i++)
                {
                    var (mx, my) = POSSIBLE_MOVES[i];
                    int nx2 = x + mx, ny2 = y + my;
                    if (nx2 < 0 || nx2 > Nx || ny2 < 0 || ny2 > Ny) continue;
                    hSum += _z[nx2][ny2];
                    count++;
                }
                double diff = Clamp(hSum / count - _z[x][y], -10.0, 2.0);
                _z[x][y] += (float)(MsvcRng.RandfRaw() * (0.6 - 0.2) + 0.2) * (float)diff;
            }
    }


    private double GetVertexNoise(int x, int y)
    {
        var (rx, ry, rz) = _posRand;
        double px = x * _detail + rx;
        double py = y * _detail + ry;
        var (nx, ny, nz) = PerlinNoise.PerlinOctave(_noiseFreq, 0.5, 1, px, py, rz);
        return nx + ny + nz;
    }


    private static T[][] Alloc2D<T>(int w, int h, T fill)
    {
        var arr = new T[w][];
        for (int i = 0; i < w; i++) { arr[i] = new T[h]; if (!fill!.Equals(default(T))) Array.Fill(arr[i], fill); }
        return arr;
    }

    private static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
    private static double Clamp01(double v) => v < 0.0 ? 0.0 : v > 1.0 ? 1.0 : v;

    private static int ClampExcl(int v, int lo, int hi)
    {
        if (v < lo)  return lo;
        if (v >= hi) return hi - 1;
        return v;
    }

    private static int RoundVal(double v)
    {
        double f = Math.Floor(v);
        return (int)f + (v - f > 0.5 ? 1 : 0);
    }
}
