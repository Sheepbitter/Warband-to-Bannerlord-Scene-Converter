using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace WarbandToBannerlordConverter;

// Don't ask me how this works. It's 100% vibecoded. I opened the engine files in Ghidra, found stuff relevant to generating and reading navmesh.bin files, then had Claude reverse engineer the decompiled C++ code to create a tool that makes navmesh prefab bin that the bannerlord editor can use.
// The general overview however, is that we get the vertices from the 3d object that mab tools generates and regenerate face and edge data from there. That is then converted into a bin that the BL editor can read when importing navmeshes.
// We could get the information directly from the SCO file, which already includes face and edge data. But it also includes other information that we have to throw out anyways, so there wasn't much of a difference between doing it directly and using the 3d object as an intermediate step.
// And since we're already processing other unpacked information from Mab Tools, it didn't make sense to separately read the SCO ourselves when it could all be integrated into a single button this way.

public class NavMeshResult
{
    public int VertexCount  { get; set; }
    public int EdgeCount    { get; set; }
    public int FaceCount    { get; set; }
    public string OutputPath { get; set; }
}

public static class NavMeshProcessor
{
    // NMG8 format constants (reverse-engineered from rglNav_mesh::import_stream)
    private const uint   NMG8_VERSION   = 0x38474d4e;  // "NMG8" as LE uint32
    private const uint   LEVEL_MASK_ALL = 0xffffffff;
    private const float  DEFAULT_COST   = 0f;
    private const int    PAD_INT        = -1;           // legacy edge pad fields

    // ── Entry point ─────────────────────────────────────────────────────────

    /// <summary>
    /// Scans <paramref name="folderPath"/> for an ai_mesh.obj, converts it to
    /// an NMG8 navmesh prefab .bin and writes it alongside the OBJ.
    /// Returns null (and logs a message) if no OBJ is found.
    /// </summary>
    public static NavMeshResult ProcessFolder(string folderPath, Action<string> log)
    {
        string objPath = Path.Combine(folderPath, "ai_mesh.obj");
        if (!File.Exists(objPath))
        {
            log("NavMesh: no ai_mesh.obj found in folder, skipping.");
            return null;
        }

        log("NavMesh: parsing ai_mesh.obj...");
        ParseObj(objPath, out List<(float x, float y, float z)> verts,
                          out List<int[]> faces);
        log($"NavMesh: {verts.Count} raw vertices, {faces.Count} raw faces.");

        log("NavMesh: deduplicating vertices...");
        Deduplicate(ref verts, ref faces, tolerance: 1e-4f);
        log($"NavMesh: {verts.Count} verts after dedup, {faces.Count} faces.");

        log("NavMesh: building edge topology...");
        BuildTopology(faces, out List<(int a, int b)> edges,
                             out List<int[]> faceEdges);
        log($"NavMesh: {edges.Count} unique edges.");

        log("NavMesh: serialising NMG8 binary...");
        byte[] bin = BuildNmg8(verts, faces, edges, faceEdges);

        // Output sits next to the OBJ, named after the folder (the scene name)
        string prefabName = Path.GetFileName(folderPath.TrimEnd(Path.DirectorySeparatorChar,
                                                                  Path.AltDirectorySeparatorChar));
        string outPath = Path.Combine(folderPath, prefabName + "_navmesh.bin");
        File.WriteAllBytes(outPath, bin);

        log($"NavMesh: written {bin.Length} bytes → {Path.GetFileName(outPath)}");

        return new NavMeshResult
        {
            VertexCount  = verts.Count,
            EdgeCount    = edges.Count,
            FaceCount    = faces.Count,
            OutputPath   = outPath
        };
    }

    // ── OBJ Parser ──────────────────────────────────────────────────────────

    private static void ParseObj(string path,
                                 out List<(float x, float y, float z)> verts,
                                 out List<int[]> faces)
    {
        verts = new List<(float, float, float)>();
        faces = new List<int[]>();

        foreach (string rawLine in File.ReadLines(path, Encoding.UTF8))
        {
            string line = rawLine.Trim();
            if (line.Length == 0 || line[0] == '#') continue;

            // Split on any whitespace
            string[] parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) continue;

            if (parts[0] == "v" && parts.Length >= 4)
            {
                verts.Add((float.Parse(parts[1]), float.Parse(parts[2]), float.Parse(parts[3])));
            }
            else if (parts[0] == "f" && parts.Length >= 4)
            {
                // Indices are 1-based and may be v/vt/vn — take only the first component
                int[] idx = new int[parts.Length - 1];
                for (int i = 0; i < idx.Length; i++)
                {
                    string raw = parts[i + 1].Split('/')[0];
                    int v = int.Parse(raw);
                    idx[i] = v > 0 ? v - 1 : verts.Count + v;  // handle negative indices
                }

                if (idx.Length == 3 || idx.Length == 4)
                {
                    faces.Add(idx);
                }
                else if (idx.Length > 4)
                {
                    // Fan-triangulate n-gons
                    for (int i = 1; i < idx.Length - 1; i++)
                        faces.Add(new[] { idx[0], idx[i], idx[i + 1] });
                }
            }
        }
    }

    // ── Vertex deduplication ────────────────────────────────────────────────

    private static void Deduplicate(ref List<(float x, float y, float z)> verts,
                                    ref List<int[]> faces,
                                    float tolerance)
    {
        float cell = tolerance * 10f;
        var grid   = new Dictionary<(int, int, int), List<(int idx, float x, float y, float z)>>();
        var newVerts = new List<(float x, float y, float z)>();
        var remap    = new int[verts.Count];

        for (int i = 0; i < verts.Count; i++)
        {
            var (vx, vy, vz) = verts[i];
            var key = ((int)(vx / cell), (int)(vy / cell), (int)(vz / cell));

            int found = -1;
            if (grid.TryGetValue(key, out var bucket))
            {
                foreach (var (ni, nx, ny, nz) in bucket)
                {
                    float dx = vx - nx, dy = vy - ny, dz = vz - nz;
                    if (MathF.Sqrt(dx*dx + dy*dy + dz*dz) < tolerance) { found = ni; break; }
                }
            }

            if (found >= 0)
            {
                remap[i] = found;
            }
            else
            {
                int ni2 = newVerts.Count;
                newVerts.Add(verts[i]);
                if (!grid.ContainsKey(key)) grid[key] = new List<(int, float, float, float)>();
                grid[key].Add((ni2, vx, vy, vz));
                remap[i] = ni2;
            }
        }

        // Remap face indices and drop degenerate faces
        var newFaces = new List<int[]>();
        foreach (var face in faces)
        {
            int[] f = new int[face.Length];
            for (int i = 0; i < face.Length; i++) f[i] = remap[face[i]];

            // Drop degenerate (any duplicate vertex index)
            bool degen = false;
            for (int i = 0; i < f.Length && !degen; i++)
                for (int j = i + 1; j < f.Length && !degen; j++)
                    if (f[i] == f[j]) degen = true;

            if (!degen) newFaces.Add(f);
        }

        verts = newVerts;
        faces = newFaces;
    }

    // ── Edge topology ───────────────────────────────────────────────────────

    private static void BuildTopology(List<int[]> faces,
                                      out List<(int a, int b)> edges,
                                      out List<int[]> faceEdges)
    {
        var edgeMap = new Dictionary<(int, int), int>();
        edges     = new List<(int, int)>();
        faceEdges = new List<int[]>();

        foreach (int[] face in faces)
        {
            int n = face.Length;
            int[] fe = new int[n];
            for (int i = 0; i < n; i++)
            {
                int a = face[i], b = face[(i + 1) % n];
                var key = (Math.Min(a, b), Math.Max(a, b));
                if (!edgeMap.TryGetValue(key, out int ei))
                {
                    ei = edges.Count;
                    edgeMap[key] = ei;
                    edges.Add(key);
                }
                fe[i] = ei;
            }
            faceEdges.Add(fe);
        }
    }

    // ── NMG8 binary serialiser ──────────────────────────────────────────────

    private static byte[] BuildNmg8(List<(float x, float y, float z)> verts,
                                    List<int[]>      faces,
                                    List<(int a, int b)> edges,
                                    List<int[]>      faceEdges)
    {
        using var ms  = new MemoryStream();
        using var bw  = new BinaryWriter(ms);

        // ── Version ────────────────────────────────────────────────────
        bw.Write(NMG8_VERSION);

        // ── Vertices ───────────────────────────────────────────────────
        bw.Write(verts.Count);
        foreach (var (x, y, z) in verts)
        {
            bw.Write(x);
            bw.Write(y);
            bw.Write(z);
        }

        // ── Edges ──────────────────────────────────────────────────────
        // File format: [pad(-1), vertA, vertB, pad(-1), pad(-1)]
        // The 3 pad fields are legacy; import_stream discards all three.
        bw.Write(edges.Count);
        foreach (var (a, b) in edges)
        {
            bw.Write(PAD_INT);
            bw.Write(a);
            bw.Write(b);
            bw.Write(PAD_INT);
            bw.Write(PAD_INT);
        }

        // ── Faces ──────────────────────────────────────────────────────
        bw.Write(faces.Count);
        for (int i = 0; i < faces.Count; i++)
        {
            int[] face = faces[i];
            int   vc   = face.Length;

            bw.Write(vc);                    // vert_count (3 or 4)

            foreach (int vi in face)         // vertex indices
                bw.Write(vi);

            foreach (int ei in faceEdges[i]) // edge indices
                bw.Write(ei);

            // Per-face versioned fields (version_fields_count = 2):
            //   field 0 (>= v2): is_concave byte  → face+0x138
            //   field 1 (>= v1): level_cost_index → face+0x130
            bw.Write(2);                     // version_fields_count
            bw.Write(0);                     // is_concave = false
            bw.Write(0);                     // level_cost_index = 0

            bw.Write(DEFAULT_COST);          // cost float  → face+0x120
            bw.Write(LEVEL_MASK_ALL);        // level_mask  → face+0x134  (all agents)
            bw.Write((byte)0);               // inverted flag (NMG8 > MMG8 path)
        }

        // ── Level groups ───────────────────────────────────────────────
        // 32 empty groups; the editor can populate cost data later.
        bw.Write(32);
        for (int g = 0; g < 32; g++)
        {
            bw.Write(0);   // capacity
            bw.Write(0);   // used
        }

        bw.Flush();
        byte[] body = ms.ToArray();

        // Prepend the 4-byte size prefix (stripped by rglBuffer before import_stream)
        using var full = new MemoryStream(body.Length + 4);
        using var bw2  = new BinaryWriter(full);
        bw2.Write((uint)body.Length);
        bw2.Write(body);
        bw2.Flush();
        return full.ToArray();
    }
}
