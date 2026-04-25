using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows.Forms;
using System.Xml.Linq;

namespace WarbandToBannerlordConverter;
// The main GUI for the Converter Tool to streamline use of all the programs needed to convert Warband Scenes to Bannerlord. Was manually typing values in a console window before this and that was a pain in the ass.

public partial class MainForm : Form
{
    private TextBox txtTerrainFolder;
    private TextBox txtBLPrefab;
    private TextBox txtJsonPath, txtXmlPath;
    private TextBox txtZScale, txtZOffset;
    private ListBox lbProps;
    private CheckBox chkUseOrigin;

    private NumericUpDown[] numPos = new NumericUpDown[3];
    private NumericUpDown[] numRot = new NumericUpDown[3];
    private NumericUpDown[] numScale = new NumericUpDown[3];
    private NumericUpDown[] numOrigin = new NumericUpDown[3];

    private Label lblStatus;
    private Label lblZResults;

    private Button btnProcessTerrain;
    private MappingManager mapper;
    private bool _isLoadingMapping = false;

    private System.Windows.Forms.Timer _autoSaveTimer;

    public MainForm(string jPath, string xPath)
    {
        InitializeManualComponents();
        txtJsonPath.Text = jPath;
        txtXmlPath.Text = xPath;

        string mPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "asset_mappings.xml");
        mapper = new MappingManager(mPath);

        if (!string.IsNullOrEmpty(jPath)) LoadProps();
    }

    private void InitializeManualComponents()
    {
        this.Text = "WB to BL Live Converter v11";
        this.Size = new Size(850, 760);
        this.MinimumSize = new Size(700, 650);

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

        Panel pnlTerrain = new Panel { Dock = DockStyle.Top, Height = 100, Padding = new Padding(8), BackColor = Color.FromArgb(240, 240, 245) };

        GroupBox grpTerrain = new GroupBox { Text = "Unpacked Files Processor", Top = 5, Left = 5, Width = 820, Height = 85 };
        grpTerrain.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

        txtTerrainFolder = new TextBox { Top = 25, Left = 10, Width = 350 };
        Button btnBrowseTerrain = new Button { Text = "Browse", Top = 23, Left = 370, Width = 80 };
        btnProcessTerrain = new Button { Text = "Convert files", Top = 23, Left = 460, Width = 100, BackColor = Color.LightGreen };

        Label lblTerrainHint = new Label
        {
            Text = "Select the folder containing your PFM, PGM, and ai_mesh.obj files from Mab Tools.",
            Top = 52,
            Left = 10,
            Width = 540,
            ForeColor = Color.Gray,
            Font = new Font("Segoe UI", 8f)
        };

        grpTerrain.Controls.Add(new Label { Text = "Z Scale:", Top = 20, Left = 575, Width = 60, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        txtZScale = new TextBox
        {
            Top = 18,
            Left = 640,
            Width = 120,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = grpTerrain.BackColor,
            Text = "0.000000"
        };

        grpTerrain.Controls.Add(new Label { Text = "Z Offset:", Top = 45, Left = 575, Width = 60, Font = new Font("Segoe UI", 9f, FontStyle.Bold) });
        txtZOffset = new TextBox
        {
            Top = 43,
            Left = 640,
            Width = 120,
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = grpTerrain.BackColor,
            Text = "0.000000"
        };

        grpTerrain.Controls.AddRange(new Control[] { txtTerrainFolder, btnBrowseTerrain, btnProcessTerrain, lblTerrainHint });
        pnlTerrain.Controls.Add(grpTerrain);
        grpTerrain.Controls.Add(txtZScale);
        grpTerrain.Controls.Add(txtZOffset);

        btnBrowseTerrain.Click += (s, e) => {
            using var fbd = new FolderBrowserDialog();
            if (fbd.ShowDialog() == DialogResult.OK) txtTerrainFolder.Text = fbd.SelectedPath;
        };

        btnProcessTerrain.Click += (s, e) =>
        {
            if (!Directory.Exists(txtTerrainFolder.Text)) return;

            btnProcessTerrain.Enabled = false;
            txtZScale.Text  = "Calculating...";
            txtZOffset.Text = "Calculating...";

            var terrainResults = TerrainProcessor.ProcessFolder(txtTerrainFolder.Text, (msg) =>
            {
                lblStatus.Text = msg;
                Application.DoEvents();
            });

            if (terrainResults != null)
            {
                txtZScale.Text  = terrainResults.ZScale.ToString("F6");
                txtZOffset.Text = terrainResults.ZOffset.ToString("F6");
            }

            var navResult = NavMeshProcessor.ProcessFolder(txtTerrainFolder.Text, (msg) =>
            {
                lblStatus.Text = msg;
                Application.DoEvents();
            });

            if (navResult != null)
            {
                SetStatus(
                    $"Done — terrain converted, navmesh: {navResult.VertexCount}v " +
                    $"{navResult.EdgeCount}e {navResult.FaceCount}f → {Path.GetFileName(navResult.OutputPath)}",
                    Color.DarkGreen);
            }
            else
            {
                SetStatus("Terrain conversion complete! (no ai_mesh.obj found for navmesh)", Color.DarkGreen);
            }

            btnProcessTerrain.Enabled = true;
        };

        Panel pnlFiles = new Panel { Dock = DockStyle.Top, Height = 80, Padding = new Padding(5), BackColor = Color.FromArgb(230, 230, 235) };
        txtJsonPath = AddFileSelector(pnlFiles, "Mission JSON:", 10, "JSON Files|*.json");
        txtXmlPath  = AddFileSelector(pnlFiles, "Scene XSCENE:", 40, "XSCENE Files|*.xscene");

        lbProps = new ListBox { Dock = DockStyle.Left, Width = 250, Font = new Font("Segoe UI", 9) };
        lbProps.SelectedIndexChanged += (s, e) => SelectedPropChanged();

        Panel pnlEdit = new Panel { Dock = DockStyle.Fill, Padding = new Padding(15), AutoScroll = true };

        txtBLPrefab = AddLabeledInput(pnlEdit, "Bannerlord Prefab ID:", 20);
        txtBLPrefab.TextChanged += (s, e) => ScheduleAutoSave();

        AddHeader(pnlEdit, "Global Position Offset (X, Y, Z):", 70);
        numPos = AddXYZRow(pnlEdit, 95, -1000, 1000);
        foreach (var n in numPos) n.ValueChanged += (s, e) => ScheduleAutoSave();

        AddHeader(pnlEdit, "Global Rotation Offset (Degrees):", 150);
        numRot = AddXYZRow(pnlEdit, 175, -360, 360);
        foreach (var n in numRot) n.ValueChanged += (s, e) => ScheduleAutoSave();

        AddHeader(pnlEdit, "Global Scale Multiplier:", 230);
        numScale = AddXYZRow(pnlEdit, 255, -100, 100, 1.0m);
        foreach (var n in numScale) n.ValueChanged += (s, e) => ScheduleAutoSave();

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
            n.ValueChanged += (s, e) => ScheduleAutoSave();
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

    private void ScheduleAutoSave()
    {
        if (_isLoadingMapping) return;
        _autoSaveTimer.Stop();
        _autoSaveTimer.Start();
        SetStatus($"Unsaved changes for \"{lbProps.SelectedItem}\"...", Color.DarkOrange);
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

    private TextBox AddFileSelector(Panel p, string label, int y, string filter)
    {
        p.Controls.Add(new Label { Text = label, Top = y, Left = 10, Width = 100 });
        TextBox tb = new TextBox { Top = y, Left = 110, Width = 550, ReadOnly = true };
        Button btn = new Button { Text = "Browse...", Top = y - 2, Left = 670, Width = 80 };
        btn.Click += (s, e) =>
        {
            using (OpenFileDialog ofd = new OpenFileDialog { Filter = filter })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    tb.Text = ofd.FileName;
                    if (filter.Contains("json")) LoadProps();
                }
            }
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
            var num = new NumericUpDown { Top = y, Left = 30 + (i * 90), Width = 60, Minimum = min, Maximum = max, DecimalPlaces = 3, Value = def };
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
            if ((ModifierKeys & Keys.Control) != 0)
                step = 0.01m;
            else if ((ModifierKeys & Keys.Shift) != 0)
                step = 1.00m;
            else
                step = 0.1m;

            decimal delta = e.Delta > 0 ? step : -step;
            decimal newVal = num.Value + delta;
            num.Value = Math.Max(num.Minimum, Math.Min(num.Maximum, newVal));
        };
    }
}
