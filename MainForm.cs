using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using ScottPlot.WinForms;

namespace HapticApp
{
    // Main window: Load button, Calculate button, three charts and a results box.
    public class MainForm : Form
    {
        private readonly Button _btnLoad = new Button { Text = "Load data...", AutoSize = true };
        private readonly Button _btnCalc = new Button { Text = "Calculate", AutoSize = true, Enabled = false };
        private readonly System.Windows.Forms.Label _lblFile = new System.Windows.Forms.Label { Text = "No file loaded", AutoSize = true };

        private readonly FormsPlot _plotData = new FormsPlot { Dock = DockStyle.Fill };    // travel and force against sample
        private readonly FormsPlot _plotSwitch = new FormsPlot { Dock = DockStyle.Fill };  // switching against sample
        private readonly FormsPlot _plotForce = new FormsPlot { Dock = DockStyle.Fill };   // force against travel
        private readonly TextBox _txtResults = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = new System.Drawing.Font("Consolas", 10)
        };

        private Recording _rec;
        private HapticResult _res;

        public MainForm()
        {
            Text = "Haptic Data Analyzer";
            Width = 1300;
            Height = 820;
            StartPosition = FormStartPosition.CenterScreen;

            var top = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 44,
                Padding = new Padding(8, 8, 8, 4),
                WrapContents = false
            };
            _lblFile.Margin = new Padding(16, 6, 0, 0);
            _btnLoad.Click += OnLoad;
            _btnCalc.Click += OnCalculate;
            top.Controls.Add(_btnLoad);
            top.Controls.Add(_btnCalc);
            top.Controls.Add(_lblFile);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
            grid.Controls.Add(_plotData, 0, 0);
            grid.Controls.Add(_plotForce, 1, 0);
            grid.Controls.Add(_plotSwitch, 0, 1);
            grid.Controls.Add(_txtResults, 1, 1);

            // Fill control first, then the docked top panel.
            Controls.Add(grid);
            Controls.Add(top);

            _txtResults.Text = "Press 'Load data...' and choose the CSV file.";
        }

        // ---------- Button: load ----------
        private void OnLoad(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = "Select acquisition data",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*"
            })
            {
                if (dlg.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    _rec = CsvLoader.Load(dlg.FileName);
                    if (_rec.Count == 0)
                        throw new InvalidDataException("The file contains no readable data rows.");

                    _res = null;
                    _lblFile.Text = Path.GetFileName(dlg.FileName) + "  (" + _rec.Count + " samples)";
                    _txtResults.Text = "Data loaded. Press 'Calculate' to get the haptic characteristics.";

                    DrawDataChart();
                    DrawSwitchChart();
                    DrawForceChart();
                    _btnCalc.Enabled = true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, ex.Message, "Could not load file", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        // ---------- Button: calculate ----------
        private void OnCalculate(object sender, EventArgs e)
        {
            if (_rec == null) return;

            try
            {
                _res = HapticCalculator.Calculate(_rec);
                DrawForceChart();                 // redraw with the markers
                _txtResults.Text = FormatResults(_res);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Calculation failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        // ---------- Charts ----------
        private double[] SampleNumbers()
        {
            var xs = new double[_rec.Count];
            for (int i = 0; i < xs.Length; i++) xs[i] = i;
            return xs;
        }

        // Plot 1: travel and force against sample number.
        private void DrawDataChart()
        {
            var plot = _plotData.Plot;
            plot.Clear();
            double[] xs = SampleNumbers();

            var travel = plot.Add.Scatter(xs, _rec.Travel);
            travel.MarkerSize = 0;
            travel.LegendText = "Travel";
            travel.Color = ScottPlot.Colors.Blue;

            var force = plot.Add.Scatter(xs, _rec.Force);
            force.MarkerSize = 0;
            force.LegendText = "Force";
            force.Color = ScottPlot.Colors.Red;

            plot.Title("Acquisition data");
            plot.XLabel("Sample number");
            plot.YLabel("Travel / Force");
            plot.ShowLegend();
            plot.Axes.AutoScale();
            _plotData.Refresh();
        }

        // Plot 2: the switching signal on its own chart.
        private void DrawSwitchChart()
        {
            var plot = _plotSwitch.Plot;
            plot.Clear();

            var sw = plot.Add.Scatter(SampleNumbers(), _rec.Switching);
            sw.MarkerSize = 0;
            sw.LegendText = "Switching";
            sw.Color = ScottPlot.Colors.Green;

            plot.Title("Switching signal");
            plot.XLabel("Sample number");
            plot.YLabel("Switching level");
            plot.ShowLegend();
            plot.Axes.AutoScale();
            _plotSwitch.Refresh();
        }

        // Plot 3: force against travel. Press and release are drawn separately, like the diagram in the task.
        private void DrawForceChart()
        {
            var plot = _plotForce.Plot;
            plot.Clear();

            int turn = Array.IndexOf(_rec.Travel, _rec.Travel.Max());

            double[] pressX = _rec.Travel.Take(turn + 1).ToArray();
            double[] pressY = _rec.Force.Take(turn + 1).ToArray();
            double[] relX = _rec.Travel.Skip(turn).ToArray();
            double[] relY = _rec.Force.Skip(turn).ToArray();

            var press = plot.Add.Scatter(pressX, pressY);
            press.MarkerSize = 0;
            press.LegendText = "Press";
            press.Color = ScottPlot.Colors.Blue;

            var release = plot.Add.Scatter(relX, relY);
            release.MarkerSize = 0;
            release.LegendText = "Release";
            release.Color = ScottPlot.Colors.Orange;

            if (_res != null)
            {
                AddPoint(plot, _res.FaTravel, _res.Fa, "Fa", ScottPlot.Colors.Red);
                AddPoint(plot, _res.FraTravel, _res.Fra, "Fra", ScottPlot.Colors.Green);
                AddPoint(plot, _res.FrrTravel, _res.Frr, "Frr", ScottPlot.Colors.Purple);
                AddPoint(plot, _res.SwitchOnTravel, _res.SwitchOnForce, "Switch ON", ScottPlot.Colors.Black);
                AddPoint(plot, _res.SwitchOffTravel, _res.SwitchOffForce, "Switch OFF", ScottPlot.Colors.Gray);
            }

            plot.Title("Force against travel");
            plot.XLabel("Travel");
            plot.YLabel("Force");
            plot.ShowLegend();
            plot.Axes.AutoScale();
            _plotForce.Refresh();
        }

        // A single highlighted point (drawn as a one point scatter so it shows in the legend).
        private static void AddPoint(ScottPlot.Plot plot, double x, double y, string label, ScottPlot.Color color)
        {
            var pt = plot.Add.Scatter(new[] { x }, new[] { y });
            pt.LineWidth = 0;
            pt.MarkerSize = 12;
            pt.MarkerShape = ScottPlot.MarkerShape.FilledCircle;
            pt.Color = color;
            pt.LegendText = label;
        }

        // ---------- Results text ----------
        private static string F(double v)
        {
            return v.ToString("F3", CultureInfo.InvariantCulture);
        }

        private static string FormatResults(HapticResult r)
        {
            var sb = new StringBuilder();
            sb.AppendLine("PRESS CURVE");
            sb.AppendLine("  Fa  (peak force)      : " + F(r.Fa) + "   at travel " + F(r.FaTravel));
            sb.AppendLine("  Fra (valley after Fa) : " + F(r.Fra) + "   at travel " + F(r.FraTravel));
            sb.AppendLine("  dF  (Fa - Fra)        : " + F(r.DeltaF));
            sb.AppendLine();
            sb.AppendLine("RELEASE CURVE");
            sb.AppendLine("  Frr (release valley)  : " + F(r.Frr) + "   at travel " + F(r.FrrTravel));
            sb.AppendLine();
            sb.AppendLine("SWITCHING");
            sb.AppendLine("  Tm  (travel at ON)    : " + F(r.Tm));
            sb.AppendLine("  dT  (Tm - Fa travel)  : " + F(r.DeltaT));
            sb.AppendLine("  ON  at travel " + F(r.SwitchOnTravel) + "  (force " + F(r.SwitchOnForce) + ")");
            sb.AppendLine("  OFF at travel " + F(r.SwitchOffTravel) + "  (force " + F(r.SwitchOffForce) + ")");
            sb.AppendLine("  Signal levels: off " + F(r.SwitchLevelOff) + ", on " + F(r.SwitchLevelOn));
            sb.AppendLine();
            sb.AppendLine("Units are those of the data file.");
            return sb.ToString();
        }
    }
}
