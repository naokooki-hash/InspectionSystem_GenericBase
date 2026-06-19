using System;
using System.Linq;
using System.Windows.Forms;
using ScottPlot;
using ScottPlot.WinForms;

// --- 名前の衝突（パニック）を防ぐための明確なルール付け ---
using UI_Label = System.Windows.Forms.Label;
using UI_Color = System.Drawing.Color;
using UI_Font = System.Drawing.Font;
using UI_FontStyle = System.Drawing.FontStyle;

namespace _20260224SolderInspec
{
    public class FormDashboard : Form
    {
        private ProductionAnalyzer _analyzer;
        private TabControl _tabControl;
        private FormsPlot _plotTact, _plotQualityTrend, _plotQualityHist, _plotBrightness;
        private UI_Label _lblTactSummary, _lblQualitySummary, _lblBrightnessSummary;

        public FormDashboard(ProductionAnalyzer analyzer)
        {
            _analyzer = analyzer;
            InitializeUI();
            UpdateCharts();
        }

        private void InitializeUI()
        {
            this.Text = "📊 リアルタイム生産稼働・品質分析ダッシュボード (ScottPlot版)";
            this.Size = new System.Drawing.Size(1100, 750);
            this.StartPosition = FormStartPosition.CenterParent;

            _tabControl = new TabControl { Dock = DockStyle.Fill };
            this.Controls.Add(_tabControl);

            TabPage t1 = new TabPage("1. 稼働・タクトタイム"); SetupTactTab(t1); _tabControl.TabPages.Add(t1);
            TabPage t2 = new TabPage("2. 品質・工程能力 (Cp/Cpk)"); SetupQualityTab(t2); _tabControl.TabPages.Add(t2);
            TabPage t3 = new TabPage("3. 環境・輝度正規分布"); SetupBrightnessTab(t3); _tabControl.TabPages.Add(t3);
        }

        private void SetupTactTab(TabPage tab)
        {
            _lblTactSummary = new UI_Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold),
                BackColor = UI_Color.LightSlateGray,
                ForeColor = UI_Color.White,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            tab.Controls.Add(_lblTactSummary);

            _plotTact = new FormsPlot { Dock = DockStyle.Fill };
            _plotTact.Plot.Axes.Title.Label.Text = "タクトタイム推移";
            _plotTact.Plot.Axes.Bottom.Label.Text = "検査回数 (直近)";
            _plotTact.Plot.Axes.Left.Label.Text = "サイクルタイム (秒)";
            tab.Controls.Add(_plotTact);
        }

        private void SetupQualityTab(TabPage tab)
        {
            _lblQualitySummary = new UI_Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold),
                BackColor = UI_Color.Teal,
                ForeColor = UI_Color.White,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            tab.Controls.Add(_lblQualitySummary);

            TableLayoutPanel tlp = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tlp.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40F));

            _plotQualityTrend = new FormsPlot { Dock = DockStyle.Fill };
            _plotQualityTrend.Plot.Axes.Title.Label.Text = "角度ばらつきトレンド";
            tlp.Controls.Add(_plotQualityTrend, 0, 0);

            _plotQualityHist = new FormsPlot { Dock = DockStyle.Fill };
            _plotQualityHist.Plot.Axes.Title.Label.Text = "角度分布ヒストグラム";
            tlp.Controls.Add(_plotQualityHist, 1, 0);

            tab.Controls.Add(tlp);
        }

        private void SetupBrightnessTab(TabPage tab)
        {
            _lblBrightnessSummary = new UI_Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = new UI_Font("Meiryo", 11F, UI_FontStyle.Bold),
                BackColor = UI_Color.DarkGoldenrod,
                ForeColor = UI_Color.White,
                TextAlign = System.Drawing.ContentAlignment.MiddleLeft
            };
            tab.Controls.Add(_lblBrightnessSummary);

            _plotBrightness = new FormsPlot { Dock = DockStyle.Fill };
            _plotBrightness.Plot.Axes.Title.Label.Text = "トリガー時輝度分布";
            tab.Controls.Add(_plotBrightness);
        }

        public void UpdateCharts()
        {
            var records = _analyzer.GetRecords();
            if (records == null || records.Count == 0) return;

            double[] xs = Enumerable.Range(1, records.Count).Select(i => (double)i).ToArray();

            // --- 1. 稼働タクト ---
            _lblTactSummary.Text = $"  総検査数: {records.Count} 件  |  チョコ停(>{_analyzer.ChocoTeiThresholdSec}s): {_analyzer.GetChocoTeiCount()} 回  |  ドカ停(>{_analyzer.DokaTeiThresholdSec}s): {_analyzer.GetDokaTeiCount()} 回";
            _plotTact.Plot.Clear();
            double[] tacts = records.Select(r => r.CycleTimeSec).ToArray();
            var scatterTact = _plotTact.Plot.Add.Scatter(xs, tacts);
            scatterTact.Color = Colors.DodgerBlue;
            scatterTact.MarkerSize = 6;

            var hlChoco = _plotTact.Plot.Add.HorizontalLine(_analyzer.ChocoTeiThresholdSec);
            hlChoco.Color = Colors.Orange;
            hlChoco.LinePattern = LinePattern.Dashed;

            var hlDoka = _plotTact.Plot.Add.HorizontalLine(_analyzer.DokaTeiThresholdSec);
            hlDoka.Color = Colors.Red;
            hlDoka.LinePattern = LinePattern.Dashed;

            _plotTact.Refresh();

            // --- 2. 品質 (Cp/Cpk) ---
            var q = _analyzer.CalculateCpCpk();
            _lblQualitySummary.Text = $"  角度平均: {q.Average:F2}°  |  σ: {q.StdDev:F3}  |  Cp: {q.Cp:F2}  /  Cpk: {q.Cpk:F2}";

            // トレンド
            _plotQualityTrend.Plot.Clear();
            double[] angles = records.Select(r => r.Angle).ToArray();
            var scatterQ = _plotQualityTrend.Plot.Add.Scatter(xs, angles);
            scatterQ.Color = Colors.MediumSeaGreen;

            var hlUsl = _plotQualityTrend.Plot.Add.HorizontalLine(_analyzer.AngleUpperSpecification);
            hlUsl.Color = Colors.Red;
            var hlLsl = _plotQualityTrend.Plot.Add.HorizontalLine(_analyzer.AngleLowerSpecification);
            hlLsl.Color = Colors.Red;
            _plotQualityTrend.Refresh();

            // ヒストグラム
            _plotQualityHist.Plot.Clear();
            var qGroups = records.GroupBy(r => Math.Round(r.Angle, 1)).OrderBy(g => g.Key).ToList();
            if (qGroups.Any())
            {
                var bars = _plotQualityHist.Plot.Add.Bars(qGroups.Select(g => (double)g.Count()).ToArray());
                for (int i = 0; i < bars.Bars.Count; i++) { bars.Bars[i].Position = qGroups[i].Key; }
                bars.Color = Colors.CadetBlue;
            }
            _plotQualityHist.Refresh();

            // --- 3. 環境・輝度 ---
            var b = _analyzer.CalculateBrightnessStats();
            _lblBrightnessSummary.Text = $"  輝度平均: {b.Average:F1}  |  σ: {b.StdDev:F2}  (理想: 160 ～ 180)";

            _plotBrightness.Plot.Clear();
            var bGroups = records.GroupBy(r => (int)(Math.Round(r.Brightness / 5.0) * 5)).OrderBy(g => g.Key).ToList();
            if (bGroups.Any())
            {
                var barsB = _plotBrightness.Plot.Add.Bars(bGroups.Select(g => (double)g.Count()).ToArray());
                for (int i = 0; i < barsB.Bars.Count; i++) { barsB.Bars[i].Position = bGroups[i].Key; barsB.Bars[i].Size = 4; }

                // 色を16進数コードで直接指定（Goldenrodの代わり）
                barsB.Color = ScottPlot.Color.FromHex("#DAA520");
            }
            _plotBrightness.Refresh();
        }
    }
}