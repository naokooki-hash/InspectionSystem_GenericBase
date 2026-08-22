using System;
using System.Linq;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;

using UI_Label = System.Windows.Forms.Label;
using UI_Color = System.Drawing.Color;
using UI_Font = System.Drawing.Font;
using UI_FontStyle = System.Drawing.FontStyle;

namespace _20260224SolderInspec
{
    public class FormDashboard : Form
    {
        private ProductionAnalyzer _analyzer;
        private TabControl _tabControl = null!;
        private FormsPlot _plotTact = null!, _plotQualityTrend = null!, _plotQualityHist = null!, _plotEnv = null!;
        private UI_Label _lblTactSummary = null!, _lblQualitySummary = null!, _lblEnvSummary = null!;

        public FormDashboard(ProductionAnalyzer analyzer)
        {
            _analyzer = analyzer;
            InitializeUI();
            UpdateCharts();
        }

        private void InitializeUI()
        {
            this.Text = "📊 生産ダッシュボード (汎用モジュール)";
            this.Size = new System.Drawing.Size(1100, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            _tabControl = new TabControl { Dock = DockStyle.Fill };
            this.Controls.Add(_tabControl);

            TabPage t1 = new TabPage("1. タクト・稼働"); SetupTactTab(t1); _tabControl.TabPages.Add(t1);
            TabPage t2 = new TabPage($"2. 品質 ({_analyzer.TargetValueName})"); SetupQualityTab(t2); _tabControl.TabPages.Add(t2);
            TabPage t3 = new TabPage($"3. 環境 ({_analyzer.EnvValueName})"); SetupEnvTab(t3); _tabControl.TabPages.Add(t3);
        }

        private void SetupTactTab(TabPage tab)
        {
            _lblTactSummary = new UI_Label { Dock = DockStyle.Top, Height = 40, Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold), BackColor = UI_Color.LightSlateGray, ForeColor = UI_Color.White, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            tab.Controls.Add(_lblTactSummary);

            _plotTact = new FormsPlot { Dock = DockStyle.Fill };
            _plotTact.Plot.Axes.Title.Label.Text = "タクトタイム推移";
            _plotTact.Plot.Axes.Left.Label.Text = "サイクルタイム (秒)";
            tab.Controls.Add(_plotTact);
        }

        private void SetupQualityTab(TabPage tab)
        {
            _lblQualitySummary = new UI_Label { Dock = DockStyle.Top, Height = 40, Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold), BackColor = UI_Color.Teal, ForeColor = UI_Color.White, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            tab.Controls.Add(_lblQualitySummary);

            TableLayoutPanel tlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            _plotQualityTrend = new FormsPlot { Dock = DockStyle.Fill };
            _plotQualityTrend.Plot.Axes.Title.Label.Text = $"{_analyzer.TargetValueName} トレンド";
            _plotQualityTrend.Plot.Axes.Left.Label.Text = $"{_analyzer.TargetValueName} ({_analyzer.TargetValueUnit})";
            tlp.Controls.Add(_plotQualityTrend, 0, 0);

            _plotQualityHist = new FormsPlot { Dock = DockStyle.Fill };
            _plotQualityHist.Plot.Axes.Title.Label.Text = $"{_analyzer.TargetValueName} 分布";
            tlp.Controls.Add(_plotQualityHist, 1, 0);

            tab.Controls.Add(tlp);
        }

        private void SetupEnvTab(TabPage tab)
        {
            _lblEnvSummary = new UI_Label { Dock = DockStyle.Top, Height = 40, Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold), BackColor = UI_Color.DarkGoldenrod, ForeColor = UI_Color.White, TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
            tab.Controls.Add(_lblEnvSummary);

            _plotEnv = new FormsPlot { Dock = DockStyle.Fill };
            _plotEnv.Plot.Axes.Title.Label.Text = $"{_analyzer.EnvValueName} 分布";
            tab.Controls.Add(_plotEnv);
        }

        public void UpdateCharts()
        {
            var records = _analyzer.GetRecords();
            if (records == null || records.Count == 0) return;
            double[] xs = Enumerable.Range(1, records.Count).Select(i => (double)i).ToArray();

            // 1. タクト
            _lblTactSummary.Text = $"  総検査数: {records.Count} 件  |  チョコ停: {_analyzer.GetChocoTeiCount()} 回  |  ドカ停: {_analyzer.GetDokaTeiCount()} 回";
            _plotTact.Plot.Clear();
            var scatterTact = _plotTact.Plot.Add.Scatter(xs, records.Select(r => r.CycleTimeSec).ToArray());
            scatterTact.Color = Colors.DodgerBlue; scatterTact.MarkerSize = 6;
            _plotTact.Plot.Add.HorizontalLine(_analyzer.ChocoTeiThresholdSec).Color = Colors.Orange;
            _plotTact.Plot.Add.HorizontalLine(_analyzer.DokaTeiThresholdSec).Color = Colors.Red;
            _plotTact.Refresh();

            // 2. 品質
            var q = _analyzer.CalculateCpCpk();
            _lblQualitySummary.Text = $"  平均: {q.Average:F2}{_analyzer.TargetValueUnit}  |  σ: {q.StdDev:F3}  |  Cp: {q.Cp:F2} / Cpk: {q.Cpk:F2}";
            _plotQualityTrend.Plot.Clear();
            var scatterQ = _plotQualityTrend.Plot.Add.Scatter(xs, records.Select(r => r.TargetValue).ToArray());
            scatterQ.Color = Colors.MediumSeaGreen;
            _plotQualityTrend.Plot.Add.HorizontalLine(_analyzer.TargetUpperSpecification).Color = Colors.Red;
            _plotQualityTrend.Plot.Add.HorizontalLine(_analyzer.TargetLowerSpecification).Color = Colors.Red;
            _plotQualityTrend.Refresh();

            _plotQualityHist.Plot.Clear();
            var qGroups = records.GroupBy(r => Math.Round(r.TargetValue, 2)).OrderBy(g => g.Key).ToList();
            if (qGroups.Any())
            {
                var bars = _plotQualityHist.Plot.Add.Bars(qGroups.Select(g => (double)g.Count()).ToArray());
                for (int i = 0; i < bars.Bars.Count; i++) bars.Bars[i].Position = qGroups[i].Key;
                bars.Color = Colors.CadetBlue;
            }
            _plotQualityHist.Refresh();

            // 3. 環境
            var b = _analyzer.CalculateEnvStats();
            _lblEnvSummary.Text = $"  {_analyzer.EnvValueName} 平均: {b.Average:F1}{_analyzer.EnvValueUnit}  |  σ: {b.StdDev:F2}";
            _plotEnv.Plot.Clear();
            var bGroups = records.GroupBy(r => Math.Round(r.EnvValue, 1)).OrderBy(g => g.Key).ToList();
            if (bGroups.Any())
            {
                var barsB = _plotEnv.Plot.Add.Bars(bGroups.Select(g => (double)g.Count()).ToArray());
                for (int i = 0; i < barsB.Bars.Count; i++) barsB.Bars[i].Position = bGroups[i].Key;
                barsB.Color = ScottPlot.Color.FromHex("#DAA520");
            }
            _plotEnv.Refresh();
        }
    }
}