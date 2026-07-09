using MiniERP2.Config;
using MiniERP2.DataLoaders;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// 판매(트랙B) 파일을 마켓별로 불러오는 창. 마켓마다 컬럼 헤더가 달라 파일마다 마켓을 지정해야
/// 하는데, "추가 → 드롭다운에서 마켓 선택 → 파일 선택"을 마켓 수만큼 반복하면 번거로우므로
/// 대신 이 창 하나에 마켓별 "불러오기" 버튼을 나열해, 버튼 클릭 한 번으로 해당 마켓의 파일
/// 선택 창이 바로 뜨도록 한다. 창을 닫지 않고도 여러 마켓을 이어서 불러올 수 있다.
/// </summary>
public class SalesFileLoaderDialog : Form
{
    private readonly List<SalesMarketMapping> _salesMarkets;
    private readonly List<ExportSummaryMarket> _markets;
    private readonly SettingsService _settingsService;
    private readonly Action<ExportSummaryLoadedSource> _onSourceLoaded;

    public SalesFileLoaderDialog(List<SalesMarketMapping> salesMarkets, List<ExportSummaryMarket> markets, SettingsService settingsService, Action<ExportSummaryLoadedSource> onSourceLoaded)
    {
        _salesMarkets = salesMarkets;
        _markets = markets;
        _settingsService = settingsService;
        _onSourceLoaded = onSourceLoaded;
        InitializeComponent();
    }

    private void InitializeComponent()
    {
        Text = "판매 파일 불러오기 (마켓별)";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ShowInTaskbar = false;
        Size = new Size(380, Math.Min(600, 70 + _salesMarkets.Count * 42));

        var rowsPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoScroll = true,
            Padding = new Padding(12),
        };
        rowsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        rowsPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));

        foreach (var mapping in _salesMarkets)
        {
            var market = _markets.FirstOrDefault(m => m.MarketCode == mapping.MarketCode);
            var marketName = market?.MarketName ?? mapping.MarketCode;

            var label = new Label { Text = marketName, AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(0, 10, 0, 0) };
            var button = new Button { Text = "불러오기", Width = 90, Height = 32 };
            button.Click += (s, e) => LoadForMarket(mapping, marketName);

            rowsPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            rowsPanel.Controls.Add(label);
            rowsPanel.Controls.Add(button);
        }

        var bottomPanel = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(12), Height = 50 };
        var btnClose = new Button { Text = "닫기", Width = 90, Height = 32 };
        btnClose.Click += (s, e) => Close();
        bottomPanel.Controls.Add(btnClose);

        Controls.Add(rowsPanel);
        Controls.Add(bottomPanel);
        AcceptButton = btnClose;
        CancelButton = btnClose;
    }

    private void LoadForMarket(SalesMarketMapping mapping, string marketName)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.xls;*.csv)|*.xlsx;*.xls;*.csv|All files (*.*)|*.*",
            Title = $"{marketName} 판매 파일을 선택하세요 (여러 개 선택 가능)",
            Multiselect = true,
            InitialDirectory = _settingsService.GetLastFolder("ExportSummary.Sales") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;
        _settingsService.SetLastFolder("ExportSummary.Sales", Path.GetDirectoryName(ofd.FileNames[0])!);

        var errors = new List<string>();

        var salesCurrency = _markets.FirstOrDefault(m => m.MarketCode == mapping.MarketCode)?.Currency ?? "";

        foreach (var fileName in ofd.FileNames)
        {
            try
            {
                var result = ExportSummaryLoader.LoadSalesFile(fileName, mapping, salesCurrency);
                _onSourceLoaded(new ExportSummaryLoadedSource("B. 판매", marketName, Path.GetFileName(fileName), result.Entries.Count, result.UnmatchedRowCount, result.Entries));
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(fileName)}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            MessageBox.Show(
                $"다음 파일을 읽는 중 오류가 발생했습니다:\n{string.Join("\n", errors)}",
                "일부 파일 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
