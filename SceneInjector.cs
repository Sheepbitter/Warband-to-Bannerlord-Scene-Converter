using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace WarbandToBannerlordConverter;
// Does the actual injecting of information from the asset_mappings.xml into the Scene.xscene file. Is responsible for the matrix math to convert the Warband matrices to Euler angles that the editor requires.
// Bannerlord also uses a 3d matrix, but converts them from the scene when loading the map.
// If there is an issue with an objects rotation, do not touch the math here. It is copied directly from Bannerlord. It is correct. You likely need to flip an axis to negative and/or adjust your rotation in the mapping using the GUI.
public class InjectionResult
{
    public int InjectedCount { get; set; }
    public int RemovedCount { get; set; }
}

public class SceneInjector
{
    private readonly MappingManager _mapper;

    public SceneInjector(MappingManager mapper)
    {
        _mapper = mapper;
    }

    public InjectionResult Inject(string jsonPath, string xscenePath)
    {
        File.Copy(xscenePath, xscenePath + ".preInjection.bak", true);

        XDocument sceneXml = XDocument.Load(xscenePath);

        XElement entNode = sceneXml.Descendants("entities").FirstOrDefault()
            ?? throw new Exception("No <entities> node found in scene.");

        var oldInjections = entNode.Elements("game_entity")
            .Where(e => Regex.IsMatch(e.Attribute("name")?.Value ?? "", @"^\d+_WB_"))
            .ToList();

        foreach (var old in oldInjections) old.Remove();

        var objects = JsonSerializer.Deserialize<List<MissionObject>>(File.ReadAllText(jsonPath));

        for (int i = 0; i < objects.Count; i++)
        {
            var obj = objects[i];
            var m = _mapper.Mappings[obj.str];

            entNode.Add(BuildEntity(i, obj, m));
        }

        sceneXml.Save(xscenePath);

        return new InjectionResult
        {
            InjectedCount = objects.Count,
            RemovedCount = oldInjections.Count
        };
    }

    private XElement BuildEntity(int index, MissionObject obj, AssetMapping m)
    {
        // Position
        double nX = obj.pos[0] + m.OffX;
        double nY = obj.pos[1] + m.OffY;
        double nZ = obj.pos[2] + m.OffZ;

        //Rotation
        double rX = 0, rY = 0, rZ = 0;
        if (obj.rotation_matrix != null && obj.rotation_matrix.Count == 3)
        {
            var mat = obj.rotation_matrix;

            rX = Math.Asin(mat[1][2]);
            rY = Math.Atan2(-mat[0][2], mat[2][2]);
            rZ = Math.Atan2(-mat[1][0], mat[1][1]);
        }

        // Apply rotation GUI offsets (degrees -> radians)
        rX += m.RotX * Math.PI / 180.0;
        rY += m.RotY * Math.PI / 180.0;
        rZ += m.RotZ * Math.PI / 180.0;

        // Scale applied to the child entity
        string scaleStr = $"{obj.scale[0] * m.ScX:F3}, {obj.scale[1] * m.ScY:F3}, {obj.scale[2] * m.ScZ:F3}";

        // [index]_WB_[WB entity name]_BL_[BL entity name] - index tracks what in the scene maps to the warband scene
        string entryName = $"{index}_WB_{m.WB}_BL_{m.BL}";

        if (m.UseOrigin)
        {
            var childEntity = new XElement("game_entity",
                new XAttribute("prefab", m.BL),
                new XAttribute("_index_", index.ToString()),
                new XElement("transform",
                    new XAttribute("position", $"{m.OriginX:F3}, {m.OriginY:F3}, {m.OriginZ:F3}"),
                    new XAttribute("rotation_euler", "0.000, 0.000, 0.000"),
                    new XAttribute("scale", scaleStr)
                )
            );

            return new XElement("game_entity",
                new XAttribute("name", entryName),
                new XAttribute("old_prefab_name", ""),
                new XElement("transform",
                    new XAttribute("position", $"{nX:F3}, {nY:F3}, {nZ:F3}"),
                    new XAttribute("rotation_euler", $"{rX:F3}, {rY:F3}, {rZ:F3}")
                ),
                new XElement("children", childEntity)
            );
        }
        else
        {
            return new XElement("game_entity",
                new XAttribute("prefab", m.BL),
                new XAttribute("name", entryName),
                new XElement("transform",
                    new XAttribute("position", $"{nX:F3}, {nY:F3}, {nZ:F3}"),
                    new XAttribute("rotation_euler", $"{rX:F3}, {rY:F3}, {rZ:F3}"),
                    new XAttribute("scale", scaleStr)
                )
            );
        }
    }
}