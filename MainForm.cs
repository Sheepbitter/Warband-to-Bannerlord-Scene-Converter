using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

namespace WarbandToBannerlordConverter;

// The main GUI for the Converter Tool to streamline use of all the programs needed to convert Warband Scenes to Bannerlord. Was manually typing values in a console window before this and that was a pain in the ass.
public partial class MainForm : Form
{
    private TextBox txtTerrainCode;
    private TextBox txtTerrainFolder;
    private TextBox txtBLPrefab;
    private TextBox txtJsonPath, txtXmlPath;
    private TextBox txtZScale, txtZOffset;
    private TextBox txtMapDimensions;
    private ListBox lbProps;
    private CheckBox chkUseOrigin;
    private NumericUpDown[] numPos = new NumericUpDown[3];
    private NumericUpDown[] numRot = new NumericUpDown[3];
    private NumericUpDown[] numScale = new NumericUpDown[3];
    private NumericUpDown[] numOrigin = new NumericUpDown[3];

    private Label lblStatus;

    private Button btnProcessTerrain;
    private MappingManager mapper;
    private AppSettings _settings;
    private bool _isLoadingMapping = false;

    private System.Windows.Forms.Timer _autoSaveTimer;

    public MainForm(string jPath, string xPath)
    {
        InitializeManualComponents();

        string mPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asset_mappings.xml");
        mapper = new MappingManager(mPath);
        _settings = AppSettings.Load();

        RestoreSavedPaths(jPath, xPath);
    }

    private void InitializeManualComponents()
    {
        this.Text = "WB to BL Live Converter v12";
        this.Size = new Size(850, 780);
        this.MinimumSize = new Size(700, 680);

        // --- Auto-save timer (fires 0.5s after last change) ---
        _autoSaveTimer = new System.Windows.Forms.Timer { Interval = 500 };
        _autoSaveTimer.Tick += (s, e) =>
        {
            _autoSaveTimer.Stop();
            if (lbProps.SelectedItem != null)
            {
                ApplyCurrentFormToMapping();
                mapper.Save();
                SetStatus($"Auto-saved changes to \"{lbProps.SelectedItem}\"", Color.DarkGreen);
            }
        };

        lblStatus = new Label
        {
            Dock = DockStyle.Bottom,
            Height = 22,
            TextAlign = ContentAlignment.MiddleLeft,
            BackColor = Color.FromArgb(220, 220, 220),
            ForeColor = Color.DimGray,
            Padding = new Padding(6, 0, 0, 0),
            Text = "Ready."
        };
        this.Controls.Add(lblStatus);

        Panel pnlFooter = new Panel { Dock = DockStyle.Bottom, Height = 50, Padding = new Padding(5) };

        Button btnSaveMapping = new Button
        {
            Text = "SAVE CURRENT MAPPING",
            Width = 200,
            Dock = DockStyle.Left,
            BackColor = Color.LightSkyBlue,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        btnSaveMapping.Click += (s, e) =>
        {
            _autoSaveTimer.Stop();
            ApplyCurrentFormToMapping();
            mapper.Save();
            SetStatus("Mapping database saved manually.", Color.Navy);
        };

        Button btnInject = new Button
        {
            Text = "INJECT ALL INTO SCENE",
            Dock = DockStyle.Fill,
            BackColor = Color.LightGreen,
            Font = new Font(this.Font, FontStyle.Bold)
        };
        btnInject.Click += (s, e) => DoInjection();

        pnlFooter.Controls.Add(btnInject);
        pnlFooter.Controls.Add(btnSaveMapping);

        Panel pnlTerrain = new Panel
        {
            Dock = DockStyle.Top,
            Height = 185,
            Padding = new Padding(8),
            BackColor = Color.FromArgb(240, 240, 245)
        };

        GroupBox grpTerrain = new GroupBox
        {
            Text = "",
            Top = 5,
            Left = 5,
            Width = 820,
            Height = 170
        };
        grpTerrain.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        var lblFieldFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        grpTerrain.Controls.Add(new Label
        {
            Text = "Terrain Code",
            Top = 17,
            Left = 10,
            Width = 200,
            Font = lblFieldFont
        });

        txtTerrainCode = new TextBox
        {
            Top = 40,
            Left = 10,
            Width = 530,
            PlaceholderText = "Paste 0x… terrain code here"
        };
        txtTerrainCode.TextChanged += OnTerrainCodeChanged;
        grpTerrain.Controls.Add(txtTerrainCode);

        grpTerrain.Controls.Add(new Label
        {
            Text = "Found with the map download in  or in scenes.txt of the module.",
            Top = 65,
            Left = 10,
            Width = 440,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5f)
        });

        grpTerrain.Controls.Add(new Label
        {
            Text = "Unpacked Files Processor",
            Top = 87,
            Left = 10,
            Width = 250,
            Font = lblFieldFont
        });

        txtTerrainFolder = new TextBox { Top = 110, Left = 10, Width = 330 };
        Button btnBrowseTerrain = new Button { Text = "Browse", Top = 108, Left = 348, Width = 80 };
        btnProcessTerrain = new Button { Text = "Convert files", Top = 108, Left = 436, Width = 100, BackColor = Color.LightGreen };

        grpTerrain.Controls.Add(new Label
        {
            Text = "Folder containing files unpacked by mab tools (PFM, PGM, and JSON)",
            Top = 135,
            Left = 10,
            Width = 440,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 7.5f)
        });

        grpTerrain.Controls.Add(new Label
        {
            Text = "Map Size:",
            Top = 43,
            Left = 560,
            Width = 60,
            Font = lblFieldFont
        });
        txtMapDimensions = new TextBox
        {
            Top = 43,
            Left = 622,
            Width = 155,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = grpTerrain.BackColor,
            Text = "—"
        };
        grpTerrain.Controls.Add(txtMapDimensions);

        grpTerrain.Controls.Add(new Label
        {
            Text = "Z Scale:",
            Top = 93,
            Left = 560,
            Width = 60,
            Font = lblFieldFont
        });
        txtZScale = new TextBox
        {
            Top = 93,
            Left = 622,
            Width = 155,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = grpTerrain.BackColor,
            Text = "0.000000"
        };

        grpTerrain.Controls.Add(new Label
        {
            Text = "Z Offset:",
            Top = 113,
            Left = 560,
            Width = 60,
            Font = lblFieldFont
        });
        txtZOffset = new TextBox
        {
            Top = 113,
            Left = 622,
            Width = 155,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = grpTerrain.BackColor,
            Text = "0.000000"
        };

        grpTerrain.Controls.AddRange(new Control[]
        {
            txtTerrainFolder, btnBrowseTerrain, btnProcessTerrain,
            txtZScale, txtZOffset
        });
        pnlTerrain.Controls.Add(grpTerrain);

        btnBrowseTerrain.Click += (s, e) =>
        {
            using var fbd = new FolderBrowserDialog();
            if (Directory.Exists(txtTerrainFolder.Text)) fbd.SelectedPath = txtTerrainFolder.Text;
            if (fbd.ShowDialog() == DialogResult.OK) SetTerrainFolder(fbd.SelectedPath, true);
        };

        btnProcessTerrain.Click += (s, e) => DoConvertFiles();

        Panel pnlFiles = new Panel
        {
            Dock = DockStyle.Top,
            Height = 80,
            Padding = new Padding(5),
            BackColor = Color.FromArgb(230, 230, 235)
        };
        txtJsonPath = AddFileSelector(pnlFiles, "Mission JSON:", 10, "JSON Files|*.json", SetMissionJsonPath);
        txtXmlPath = AddFileSelector(pnlFiles, "Scene XSCENE:", 40, "XSCENE Files|*.xscene", SetSceneXscenePath);

        lbProps = new ListBox { Dock = DockStyle.Left, Width = 250, Font = new Font("Segoe UI", 9) };
        lbProps.SelectedIndexChanged += (s, e) => SelectedPropChanged();

        Panel pnlEdit = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15), AutoScroll = true };

        txtBLPrefab = AddLabeledInput(pnlEdit, "Bannerlord Prefab ID:", 20);
        txtBLPrefab.TextChanged += (s, e) => ScheduleAutoSave();

        AddHeader(pnlEdit, "Global Position Offset (X, Y, Z):", 70);
        numPos = AddXYZRow(pnlEdit, 95, -1000, 1000);
        foreach (var n in numPos) WireNumericAutoSave(n);

        AddHeader(pnlEdit, "Global Rotation Offset (Degrees):", 150);
        numRot = AddXYZRow(pnlEdit, 175, -360, 360);
        foreach (var n in numRot) WireNumericAutoSave(n);

        AddHeader(pnlEdit, "Global Scale Multiplier:", 230);
        numScale = AddXYZRow(pnlEdit, 255, -100, 100, 1.0m);
        foreach (var n in numScale) WireNumericAutoSave(n);

        AddHeader(pnlEdit, "Origin Point (Parent Empty Entity):", 315);

        chkUseOrigin = new CheckBox
        {
            Text = "Enable origin point",
            Top = 338,
            Left = 10,
            Width = 160,
            Font = new Font(this.Font, FontStyle.Regular)
        };
        chkUseOrigin.CheckedChanged += (s, e) =>
        {
            bool on = chkUseOrigin.Checked;
            foreach (var n in numOrigin) n.Enabled = on;
            ScheduleAutoSave();
        };
        pnlEdit.Controls.Add(chkUseOrigin);

        numOrigin = AddXYZRow(pnlEdit, 365, -10000, 10000);
        foreach (var n in numOrigin)
        {
            n.Enabled = false;
            WireNumericAutoSave(n);
        }

        pnlEdit.Controls.Add(new Label
        {
            Text = "Position and rotation offsets apply to the parent. Scale applies to the child prefab.",
            Top = 395,
            Left = 10,
            Width = 580,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8f)
        });

        this.Controls.Add(pnlEdit);
        this.Controls.Add(lbProps);
        this.Controls.Add(pnlFiles);
        this.Controls.Add(pnlTerrain);
        this.Controls.Add(pnlFooter);
    }

    private void OnTerrainCodeChanged(object sender, EventArgs e)
    {
        string code = txtTerrainCode.Text.Trim();
        if (string.IsNullOrWhiteSpace(code))
        {
            txtMapDimensions.Text = "—";
            return;
        }
        try
        {
            TerrainParams p = WarbandTerrainGen.ParseCode(code);
            txtMapDimensions.Text = $"{p.InGameSizeX} x {p.InGameSizeY} m";
        }
        catch
        {
            txtMapDimensions.Text = "Invalid code";
        }
    }

    private void DoConvertFiles()
    {
        if (!Directory.Exists(txtTerrainFolder.Text)) return;

        string code = txtTerrainCode.Text.Trim();
        bool hasCode = !string.IsNullOrWhiteSpace(code);
        TerrainParams terrainParams = null;

        if (!hasCode)
        {
            var answer = MessageBox.Show(
                "No terrain code has been entered.\n\n" +
                "The heightmap will not be generated without it.\n\n" +
                "Proceed anyway? Only material maps and the navmesh will be converted.",
                "Terrain Code Missing",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);

            if (answer == DialogResult.No) return;
        }
        else
        {
            try { terrainParams = WarbandTerrainGen.ParseCode(code); }
            catch (Exception ex)
            {
                MessageBox.Show($"Invalid terrain code:\n{ex.Message}", "Parse Error",
                                MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }
        }

        btnProcessTerrain.Enabled = false;
        txtZScale.Text = "Working...";
        txtZOffset.Text = "Working...";

        void Log(string msg) { lblStatus.Text = msg; Application.DoEvents(); }

        string layerPfmPath = TerrainProcessor.ProcessFolder(txtTerrainFolder.Text, Log);

        if (terrainParams != null)
        {
            if (layerPfmPath == null)
            {
                MessageBox.Show(
                    "layer_ground_elevation.pfm was not found in the selected folder.\n\n" +
                    "The heightmap cannot be generated without it.",
                    "Missing PFM", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                txtZScale.Text = "N/A";
                txtZOffset.Text = "N/A";
            }
            else
            {
                try
                {
                    Log("Generating base terrain from terrain code...");
                    var gen = new TerrainGenerator(terrainParams);
                    gen.Generate();
                    var (zGrid, gridW, gridH) = gen.GetHeightmap();

                    string basePfmPath = Path.Combine(txtTerrainFolder.Text, "base_terrain.pfm");
                    Log("Writing base_terrain.pfm...");
                    WarbandTerrainGen.WritePfm(zGrid, gridW, gridH, basePfmPath);

                    string heightmapPfm = Path.Combine(txtTerrainFolder.Text, "heightmap.pfm");
                    string heightmapPng = Path.Combine(txtTerrainFolder.Text, "heightmap.png");

                    PfmResults hmResult = PfmCombiner.CombineToHeightmap(
                        basePfmPath, layerPfmPath, heightmapPfm, heightmapPng, Log);

                    txtZScale.Text = hmResult.ZScale.ToString("F6");
                    txtZOffset.Text = hmResult.ZOffset.ToString("F6");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Heightmap generation failed:\n{ex.Message}",
                                    "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    txtZScale.Text = "Error";
                    txtZOffset.Text = "Error";
                }
            }
        }
        else
        {
            txtZScale.Text = "N/A";
            txtZOffset.Text = "N/A";
        }

        var navResult = NavMeshProcessor.ProcessFolder(txtTerrainFolder.Text, Log);

        if (navResult != null)
        {
            SetStatus(
                $"Done — heightmap generated, navmesh: {navResult.VertexCount}v " +
                $"{navResult.EdgeCount}e {navResult.FaceCount}f → {Path.GetFileName(navResult.OutputPath)}",
                Color.DarkGreen);
        }
        else
        {
            SetStatus(
                terrainParams != null
                    ? "Done — heightmap generated. (no ai_mesh.obj found for navmesh)"
                    : "Done — material maps converted. (no terrain code, no navmesh obj)",
                Color.DarkGreen);
        }

        btnProcessTerrain.Enabled = true;
    }

    private void ScheduleAutoSave()
    {
        if (_isLoadingMapping) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
        SetStatus($"Unsaved changes for \"{lbProps.SelectedItem}\"...", Color.DarkOrange);
    }

    private void RestoreSavedPaths(string jsonArg, string xsceneArg)
    {
        if (Directory.Exists(_settings.LastTerrainFolder))
            txtTerrainFolder.Text = _settings.LastTerrainFolder;

        string jsonPath = !string.IsNullOrWhiteSpace(jsonArg) ? jsonArg : _settings.LastMissionJsonPath;
        string xscenePath = !string.IsNullOrWhiteSpace(xsceneArg) ? xsceneArg : _settings.LastSceneXscenePath;

        if (File.Exists(jsonPath))
        {
            txtJsonPath.Text = jsonPath;
            _settings.LastMissionJsonPath = jsonPath;
        }

        if (File.Exists(xscenePath))
        {
            txtXmlPath.Text = xscenePath;
            _settings.LastSceneXscenePath = xscenePath;
        }

        _settings.Save();

        if (File.Exists(txtJsonPath.Text)) LoadProps();
    }

    private void SetTerrainFolder(string folderPath, bool autoDetectMissionJson)
    {
        txtTerrainFolder.Text = folderPath;
        _settings.LastTerrainFolder = folderPath;
        _settings.Save();

        if (!autoDetectMissionJson) return;

        string missionJsonPath = Path.Combine(folderPath, "mission_objects.json");
        if (File.Exists(missionJsonPath))
        {
            SetMissionJsonPath(missionJsonPath);
            SetStatus("Found mission_objects.json in the unpacked folder.", Color.DarkGreen);
        }
    }

    private void SetMissionJsonPath(string path)
    {
        txtJsonPath.Text = path;
        _settings.LastMissionJsonPath = path;
        _settings.Save();
        LoadProps();
    }

    private void SetSceneXscenePath(string path)
    {
        txtXmlPath.Text = path;
        _settings.LastSceneXscenePath = path;
        _settings.Save();
    }

    private string GetInitialDirectory(string path)
    {
        if (File.Exists(path)) return Path.GetDirectoryName(path) ?? "";
        if (Directory.Exists(path)) return path;
        return "";
    }

    private void SetStatus(string msg, Color color)
    {
        lblStatus.Text = msg;
        lblStatus.ForeColor = color;
    }

    private void LoadProps()
    {
        if (!File.Exists(txtJsonPath.Text)) return;
        try
        {
            lbProps.Items.Clear();
            var objects = JsonSerializer.Deserialize<List<MissionObject>>(File.ReadAllText(txtJsonPath.Text));
            var uniqueProps = objects.Select(o => o.str).Distinct().OrderBy(s => s);
            foreach (var p in uniqueProps)
            {
                lbProps.Items.Add(p);
                if (!mapper.Mappings.ContainsKey(p)) mapper.Mappings[p] = new AssetMapping { WB = p };
            }
        }
        catch (Exception ex) { MessageBox.Show("JSON Error: " + ex.Message); }
    }

    private void SelectedPropChanged()
    {
        if (lbProps.SelectedItem == null) return;

        _isLoadingMapping = true;
        var m = mapper.Mappings[lbProps.SelectedItem.ToString()];

        txtBLPrefab.Text = m.BL;
        numPos[0].Value = (decimal)m.OffX; numPos[1].Value = (decimal)m.OffY; numPos[2].Value = (decimal)m.OffZ;
        numRot[0].Value = (decimal)m.RotX; numRot[1].Value = (decimal)m.RotY; numRot[2].Value = (decimal)m.RotZ;
        numScale[0].Value = (decimal)m.ScX; numScale[1].Value = (decimal)m.ScY; numScale[2].Value = (decimal)m.ScZ;

        chkUseOrigin.Checked = m.UseOrigin;
        numOrigin[0].Value = (decimal)m.OriginX;
        numOrigin[1].Value = (decimal)m.OriginY;
        numOrigin[2].Value = (decimal)m.OriginZ;
        foreach (var n in numOrigin) n.Enabled = m.UseOrigin;

        _isLoadingMapping = false;
        SetStatus($"Loaded \"{lbProps.SelectedItem}\".", Color.DimGray);
    }

    private void ApplyCurrentFormToMapping()
    {
        if (lbProps.SelectedItem == null) return;
        CommitNumericEditors();

        var m = mapper.Mappings[lbProps.SelectedItem.ToString()];

        m.BL = txtBLPrefab.Text;
        m.OffX = (double)numPos[0].Value; m.OffY = (double)numPos[1].Value; m.OffZ = (double)numPos[2].Value;
        m.RotX = (double)numRot[0].Value; m.RotY = (double)numRot[1].Value; m.RotZ = (double)numRot[2].Value;
        m.ScX = (double)numScale[0].Value; m.ScY = (double)numScale[1].Value; m.ScZ = (double)numScale[2].Value;
        m.UseOrigin = chkUseOrigin.Checked;
        m.OriginX = (double)numOrigin[0].Value; m.OriginY = (double)numOrigin[1].Value; m.OriginZ = (double)numOrigin[2].Value;
    }

    private void DoInjection()
    {
        if (string.IsNullOrEmpty(txtJsonPath.Text) || string.IsNullOrEmpty(txtXmlPath.Text)) return;

        _autoSaveTimer.Stop();
        ApplyCurrentFormToMapping();
        mapper.Save();
        SetStatus("Saving before injection...", Color.DimGray);

        try
        {
            var injector = new SceneInjector(mapper);
            var result = injector.Inject(txtJsonPath.Text, txtXmlPath.Text);
            SetStatus($"Injected {result.InjectedCount} props ({result.RemovedCount} old removed).", Color.DarkGreen);
            MessageBox.Show($"Removed {result.RemovedCount} old props.\nInjected {result.InjectedCount} updated props!");
        }
        catch (Exception ex) { MessageBox.Show("Injection Error: " + ex.Message); }
    }

    private TextBox AddFileSelector(Panel p, string label, int y, string filter, Action<string> selectFile)
    {
        p.Controls.Add(new Label { Text = label, Top = y, Left = 10, Width = 100 });
        TextBox tb = new TextBox { Top = y, Left = 110, Width = 550, ReadOnly = true };
        Button btn = new Button { Text = "Browse...", Top = y - 2, Left = 670, Width = 80 };
        btn.Click += (s, e) =>
        {
            using var ofd = new OpenFileDialog { Filter = filter };
            string init = GetInitialDirectory(tb.Text);
            if (!string.IsNullOrEmpty(init)) ofd.InitialDirectory = init;
            if (ofd.ShowDialog() == DialogResult.OK) selectFile(ofd.FileName);
        };
        p.Controls.Add(tb); p.Controls.Add(btn); return tb;
    }

    private TextBox AddLabeledInput(Panel p, string text, int y)
    {
        p.Controls.Add(new Label { Text = text, Top = y, Left = 10, Width = 150 });
        TextBox tb = new TextBox { Top = y, Left = 160, Width = 250 };
        p.Controls.Add(tb); return tb;
    }

    private void AddHeader(Panel p, string text, int y) =>
        p.Controls.Add(new Label { Text = text, Top = y, Left = 10, Width = 300, Font = new Font(this.Font, FontStyle.Bold) });

    private NumericUpDown[] AddXYZRow(Panel p, int y, int min, int max, decimal def = 0)
    {
        NumericUpDown[] nums = new NumericUpDown[3];
        string[] labels = { "X", "Y", "Z" };
        for (int i = 0; i < 3; i++)
        {
            p.Controls.Add(new Label { Text = labels[i] + ":", Top = y, Left = 10 + (i * 90), Width = 20 });
            var num = new NumericUpDown
            {
                Top = y,
                Left = 30 + (i * 90),
                Width = 60,
                Minimum = min,
                Maximum = max,
                DecimalPlaces = 3,
                Value = def
            };
            AttachScrollWheel(num);
            nums[i] = num;
            p.Controls.Add(num);
        }
        return nums;
    }

    private void AttachScrollWheel(NumericUpDown num)
    {
        num.MouseWheel += (s, e) =>
        {
            ((HandledMouseEventArgs)e).Handled = true;

            decimal step;
            if ((ModifierKeys & Keys.Control) != 0) step = 0.01m;
            else if ((ModifierKeys & Keys.Shift) != 0) step = 1.00m;
            else step = 0.1m;

            decimal delta = e.Delta > 0 ? step : -step;
            decimal newVal = num.Value + delta;
            num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, newVal));
        };
    }

    private void WireNumericAutoSave(NumericUpDown num)
    {
        num.ValueChanged += (s, e) => ScheduleAutoSave();
        num.TextChanged += (s, e) => ScheduleAutoSave();
        num.Validated += (s, e) => CommitNumericEditor(num);
    }

    private void CommitNumericEditors()
    {
        foreach (var num in numPos.Concat(numRot).Concat(numScale).Concat(numOrigin))
            CommitNumericEditor(num);
    }

    private void CommitNumericEditor(NumericUpDown num)
    {
        if (string.IsNullOrWhiteSpace(num.Text)) return;

        if (!decimal.TryParse(num.Text, NumberStyles.Number, CultureInfo.CurrentCulture, out decimal value) &&
            !decimal.TryParse(num.Text, NumberStyles.Number, CultureInfo.InvariantCulture, out value))
            return;

        value = Math.Max(num.Minimum, Math.Min(num.Maximum, value));
        if (num.Value != value) num.Value = value;
    }
}