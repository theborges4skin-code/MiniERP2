using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.UI;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.2절 '마스터SKU 관리창'
/// </summary>
public class MasterSkuForm : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly ChannelSkuRepository _channelSkuRepository = new();
    private readonly SettingsService _settingsService = new();
    private ExcelLikeDataGridView _itemsGrid = new();
    private BindingList<ItemModel> _items = new();
    private Label _statusLabel = new();
    private Dictionary<string, string> _cskuSummaryCache = new();
    private TextBox _searchBox = new();

    public MasterSkuForm()
    {
        InitializeComponent();
        LoadData();
    }

    private void InitializeComponent()
    {
        Text = "마스터SKU 관리";
        Size = new Size(800, 600);
        
        // 메인 레이아웃
        // 드래그 앤 드롭 활성화
        AllowDrop = true;
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        var mainLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            RowStyles = { new RowStyle(SizeType.Absolute, 40), new RowStyle(SizeType.Percent, 100) }
        };

        // 툴바
        var toolStrip = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(5)
        };

        var btnRefresh = new Button { Text = "새로고침", Size = new Size(100, 30) };
        var btnAddNew = new Button { Text = "새 마스터SKU 추가", Size = new Size(130, 30) };
        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        var btnImport = new Button { Text = "엑셀 가져오기", Size = new Size(110, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };
        var btnViewCsku = new Button { Text = "해당 CSKU 보기", Size = new Size(110, 30) };
        var btnOverview = new Button { Text = "매입·납품 통합 조회", Size = new Size(140, 30) };

        btnRefresh.Click += OnRefreshClick;
        btnAddNew.Click += OnAddNewMasterSkuClick;
        btnSave.Click += OnSaveClick;
        btnImport.Click += OnImportClick;
        btnExport.Click += OnExportClick;
        btnViewCsku.Click += (s, e) => OpenCskuFormForSelectedRow();
        btnOverview.Click += (s, e) => OpenOverviewFormForSelectedRow();

        _searchBox = new TextBox { Width = 160, PlaceholderText = "SKU/상품명 검색" };
        _searchBox.TextChanged += (s, e) => ApplySearchFilter();

        toolStrip.Controls.Add(new Label { Text = "검색:", AutoSize = true, Padding = new Padding(0, 7, 2, 0) });
        toolStrip.Controls.Add(_searchBox);
        toolStrip.Controls.Add(btnRefresh);
        toolStrip.Controls.Add(btnAddNew);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnImport);
        toolStrip.Controls.Add(btnExport);
        toolStrip.Controls.Add(btnViewCsku);
        toolStrip.Controls.Add(btnOverview);
        _statusLabel = new Label { AutoSize = true, Padding = new Padding(15, 7, 0, 0), ForeColor = Color.DarkGreen };
        toolStrip.Controls.Add(_statusLabel);

        // 데이터 그리드
        _itemsGrid = new ExcelLikeDataGridView
        {
            Dock = DockStyle.Fill,
            // 기획서 2.3절: 열 순서/너비 기억을 위한 고유 키 설정
            PersistenceKey = "MasterSkuForm.ItemsGrid",
            AutoGenerateColumns = false,
            AllowUserToAddRows = true, // 새 행 추가 허용
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        };

        _itemsGrid.Columns.AddRange(
            new DataGridViewTextBoxColumn { Name = "Sku", HeaderText = "SKU", DataPropertyName = "Sku", Width = 150 },
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 250 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "원가", DataPropertyName = "CostPrice", Width = 100 },
            new DataGridViewTextBoxColumn { Name = "ProductGroup", HeaderText = "상품그룹", DataPropertyName = "ProductGroup", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Reserve1", HeaderText = "예비1", DataPropertyName = "Reserve1", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Reserve2", HeaderText = "예비2", DataPropertyName = "Reserve2", Width = 120 },
            new DataGridViewTextBoxColumn { Name = "Reserve3", HeaderText = "예비3", DataPropertyName = "Reserve3", Width = 120 },
            new DataGridViewTextBoxColumn
            {
                Name = "CskuInfo",
                HeaderText = "연결 CSKU",
                DataPropertyName = string.Empty,
                Width = 200,
                ReadOnly = true,
                Tag = "no-export",
                DefaultCellStyle = new DataGridViewCellStyle { ForeColor = Color.DimGray },
            }
        );

        _itemsGrid.CellFormatting += OnItemsGridCellFormatting;

        SetupContextMenu();

        _itemsGrid.CellDoubleClick += OnGridCellDoubleClick;

        _itemsGrid.UserDeletingRow += OnUserDeletingRow;

        mainLayout.Controls.Add(toolStrip, 0, 0);
        mainLayout.Controls.Add(_itemsGrid, 0, 1);

        Controls.Add(mainLayout);

        FormClosing += (s, e) => _itemsGrid.SaveLayout();
    }

    private void SetupContextMenu()
    {
        var historyMenuItem = new ToolStripMenuItem("원가 변경 이력 보기(&H)");
        historyMenuItem.Click += OnHistoryMenuItemClick;

        var viewCskuMenuItem = new ToolStripMenuItem("해당 CSKU 보기(&C)");
        viewCskuMenuItem.Click += (s, e) => OpenCskuFormForSelectedRow();

        var overviewMenuItem = new ToolStripMenuItem("매입·납품 통합 조회(&O)");
        overviewMenuItem.Click += (s, e) => OpenOverviewFormForSelectedRow();

        _itemsGrid.ContextMenuStrip!.Items.Add(new ToolStripSeparator());
        _itemsGrid.ContextMenuStrip.Items.Add(historyMenuItem);
        _itemsGrid.ContextMenuStrip.Items.Add(viewCskuMenuItem);
        _itemsGrid.ContextMenuStrip.Items.Add(overviewMenuItem);

        // 메뉴가 열릴 때, 선택된 행이 1개일 때만 '이력 보기'/'CSKU 보기'/'통합 조회' 메뉴 활성화
        _itemsGrid.ContextMenuStrip.Opening += (s, e) =>
        {
            historyMenuItem.Enabled = _itemsGrid.SelectedRows.Count == 1;
            viewCskuMenuItem.Enabled = _itemsGrid.SelectedRows.Count == 1;
            overviewMenuItem.Enabled = _itemsGrid.SelectedRows.Count == 1;
        };
    }

    private void OnHistoryMenuItemClick(object? sender, EventArgs e)
    {
        if (_itemsGrid.SelectedRows.Count != 1) return;

        var selectedRow = _itemsGrid.SelectedRows[0];
        if (selectedRow.IsNewRow) return;

        var item = selectedRow.DataBoundItem as ItemModel;
        if (item == null || string.IsNullOrWhiteSpace(item.Sku))
        {
            MessageBox.Show("SKU가 없는 품목은 이력을 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var historyForm = new CostHistoryForm(item.Sku);
        FormManager.ApplyBoundsTracking(historyForm);
        FormManager.ShowDialogSafe(historyForm, this);
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 헤더를 더블클릭했거나, 유효하지 않은 행/열 인덱스인 경우 무시
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        // 'Sku' 열을 더블클릭했는지 확인
        if (_itemsGrid.Columns[e.ColumnIndex].Name == "Sku")
        {
            OpenCskuFormForRow(e.RowIndex);
        }
    }

    /// <summary>현재 선택된 행의 SKU 기준으로 CSKU 관리창을 연다(툴바 버튼/우클릭 메뉴 공용).</summary>
    private void OpenCskuFormForSelectedRow()
    {
        if (_itemsGrid.SelectedRows.Count != 1) return;
        OpenCskuFormForRow(_itemsGrid.SelectedRows[0].Index);
    }

    private void OpenCskuFormForRow(int rowIndex)
    {
        if (rowIndex < 0 || rowIndex >= _itemsGrid.Rows.Count) return;
        if (_itemsGrid.Rows[rowIndex].IsNewRow) return;

        var sku = _itemsGrid.Rows[rowIndex].Cells["Sku"].Value as string;
        if (string.IsNullOrWhiteSpace(sku))
        {
            MessageBox.Show("SKU가 없는 품목은 CSKU를 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // CSKU 관리창을 모달 다이얼로그로 엽니다.
        using var cskuForm = new CSkuForm(sku);
        FormManager.ApplyBoundsTracking(cskuForm);
        FormManager.ShowDialogSafe(cskuForm, this);
        LoadData(); // CSKU 추가/삭제로 "연결 CSKU" 요약이 바뀌었을 수 있으므로 새로고침.
    }

    /// <summary>선택된 행의 SKU 기준으로 매입·납품 통합 조회창(§M4)을 연다.</summary>
    private void OpenOverviewFormForSelectedRow()
    {
        if (_itemsGrid.SelectedRows.Count != 1) return;
        var row = _itemsGrid.SelectedRows[0];
        if (row.IsNewRow) return;

        var sku = row.Cells["Sku"].Value as string;
        if (string.IsNullOrWhiteSpace(sku))
        {
            MessageBox.Show("SKU가 없는 품목은 조회할 수 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var overviewForm = new PurchaseSalesOverviewForm(sku);
        FormManager.ApplyBoundsTracking(overviewForm);
        FormManager.ShowDialogSafe(overviewForm, this);
    }

    private void LoadData()
    {
        var allItems = _itemRepository.GetAll();
        _items = new BindingList<ItemModel>(allItems);
        _itemsGrid.DataSource = _items;

        _cskuSummaryCache = _channelSkuRepository.GetAll()
            .GroupBy(c => c.Msku, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                g => g.Key,
                g => $"{g.Count()}건 ({string.Join(", ", g.Select(c => c.ChannelCode).Distinct())})",
                StringComparer.OrdinalIgnoreCase);

        ApplySearchFilter();
    }

    /// <summary>
    /// SKU/상품명 부분일치로 행을 숨긴다(§2 — 목록이 길어져 원하는 SKU를 찾기 힘들다는 요청).
    /// 새 BindingList로 바꿔치기하지 않고 Row.Visible만 토글하는 이유: AllowUserToAddRows로 추가한
    /// 새 행이나 미저장 편집이 필터링 중에 별도 리스트로 갈라져 저장 시 누락되는 걸 막기 위함이다.
    /// </summary>
    private void ApplySearchFilter()
    {
        var keyword = _searchBox.Text.Trim();

        // 데이터바인딩된 그리드에서 "현재 위치"(BindingManager.Position)에 있는 행을 숨기려 하면
        // InvalidOperationException이 난다(WinForms 알려진 제약) — 숨기기 전에 현재 셀을 먼저
        // 해제해 그 의존성을 끊어야 한다.
        _itemsGrid.CurrentCell = null;

        foreach (DataGridViewRow row in _itemsGrid.Rows)
        {
            if (row.IsNewRow || string.IsNullOrEmpty(keyword)) { row.Visible = true; continue; }
            if (row.DataBoundItem is not ItemModel item) { row.Visible = true; continue; }
            row.Visible = item.Sku.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                || item.ItemName.Contains(keyword, StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnItemsGridCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _itemsGrid.Columns[e.ColumnIndex].Name != "CskuInfo") return;
        if (_itemsGrid.Rows[e.RowIndex].DataBoundItem is not ItemModel item) return;
        e.Value = _cskuSummaryCache.TryGetValue(item.Sku, out var summary) ? summary : "-";
        e.FormattingApplied = true;
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        LoadData();
    }

    /// <summary>
    /// 그리드 맨 아래 빈 행에 직접 타이핑해도 등록은 되지만(AllowUserToAddRows), 필수값을 빠뜨리기
    /// 쉽고 화면을 스크롤해 빈 행을 찾아야 하는 불편이 있었다(사용자 요청 — 택배비처럼 실물이 없는
    /// 명목상 SKU를 등록할 때 특히). NewMasterSkuDialog(거래처별 CSKU 관리의 "마스터SKU 지정/변경"
    /// 에서 이미 쓰던 것과 동일)로 SKU/품명/원가/단위를 한 창에서 바로 받아 즉시 등록한다.
    /// </summary>
    private void OnAddNewMasterSkuClick(object? sender, EventArgs e)
    {
        using var dlg = new NewMasterSkuDialog();
        if (FormManager.ShowDialogSafe(dlg, this) != DialogResult.OK || dlg.ResultSku == null) return;

        _searchBox.Text = ""; // 검색어가 걸려 있으면 방금 등록한 항목이 안 보일 수 있으므로 해제
        LoadData();
        SelectRowBySku(dlg.ResultSku);
        _statusLabel.ForeColor = Color.DarkGreen;
        _statusLabel.Text = $"마스터SKU '{dlg.ResultSku}'을(를) 추가했습니다. ({DateTime.Now:HH:mm:ss})";
    }

    private void SelectRowBySku(string sku)
    {
        foreach (DataGridViewRow row in _itemsGrid.Rows)
        {
            if (row.DataBoundItem is not ItemModel item || item.Sku != sku) continue;
            _itemsGrid.ClearSelection();
            row.Selected = true;
            _itemsGrid.CurrentCell = row.Cells[0];
            _itemsGrid.FirstDisplayedScrollingRowIndex = row.Index;
            break;
        }
    }

    private void OnSaveClick(object? sender, EventArgs e)
    {
        try
        {
            // 그리드에 있는 모든 항목을 DB에 Upsert
            foreach (var item in _items)
            {
                // SKU가 비어있는 새 행은 저장하지 않음
                if (string.IsNullOrWhiteSpace(item.Sku)) continue;
                
                _itemRepository.Upsert(item);
            }
            LoadData(); // 저장 후 데이터를 다시 불러와 동기화
            // 2026-06-28 점검: 그리드를 다시 불러온 직후 모달을 띄우는 패턴이 다른 화면들에서
            // 반복 재현됐던 경쟁 상태와 같은 위험군이라 비모달 라벨로 대체.
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"성공적으로 저장되었습니다. ({DateTime.Now:HH:mm:ss})";
        }
        catch (Exception ex)
        {
            _statusLabel.ForeColor = Color.Red;
            _statusLabel.Text = $"저장 중 오류가 발생했습니다: {ex.Message}";
        }
    }

    private void OnUserDeletingRow(object? sender, DataGridViewRowCancelEventArgs e)
    {
        // 새 행(아직 저장되지 않은 행)을 삭제하는 경우는 DB 작업 없이 그냥 종료
        if (e.Row is null || e.Row.IsNewRow) return;

        // 삭제할 아이템 가져오기
        var itemToDelete = e.Row.DataBoundItem as ItemModel;
        if (itemToDelete == null || string.IsNullOrWhiteSpace(itemToDelete.Sku))
        {
            // SKU가 없는 데이터는 DB에 없으므로 그냥 UI에서만 삭제되도록 둠
            return;
        }

        // 사용자에게 삭제 확인
        var result = MessageBox.Show($"SKU '{itemToDelete.Sku}' 품목을 삭제하시겠습니까?\n관련된 모든 원가 변경 이력도 함께 삭제됩니다.", "삭제 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);

        if (result == DialogResult.Yes)
        {
            _itemRepository.Delete(itemToDelete.Sku);
            // UserDeletingRow 이벤트 처리 중 모달을 띄우는 것도 같은 위험군이라 비모달 라벨로 대체.
            _statusLabel.ForeColor = Color.DarkGreen;
            _statusLabel.Text = $"삭제되었습니다. ({DateTime.Now:HH:mm:ss})";
        }
        else
        {
            // 사용자가 '아니오'를 선택하면 그리드에서 행이 삭제되지 않도록 이벤트를 취소
            e.Cancel = true;
        }
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        // 드롭하려는 데이터가 파일인지 확인
        if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // 단일 파일만 허용
            var files = (string[])e.Data.GetData(DataFormats.FileDrop)!;
            if (files.Length == 1 && Path.GetExtension(files[0]).Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                e.Effect = DragDropEffects.Copy; // 복사 모양 커서 표시
            }
        }
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        var files = (string[])e.Data!.GetData(DataFormats.FileDrop)!;
        ImportFromFile(files[0]);
    }

    private void OnImportClick(object? sender, EventArgs e)
    {
        using var ofd = new OpenFileDialog
        {
            Filter = "Excel/CSV (*.xlsx;*.csv)|*.xlsx;*.csv|Excel (*.xlsx)|*.xlsx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
            Title = "가져올 마스터SKU 엑셀/CSV 파일을 선택하세요",
            InitialDirectory = _settingsService.GetLastFolder("MasterSkuImport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        };
        if (ofd.ShowDialog(this) != DialogResult.OK) return;

        _settingsService.SetLastFolder("MasterSkuImport", Path.GetDirectoryName(ofd.FileName)!);
        ImportFromFile(ofd.FileName);
    }

    /// <summary>
    /// 엑셀 파일의 시트/헤더 행을 먼저 보여주고, 사용자가 보면서 SKU/상품명/원가 및
    /// 예비필드 3개가 어느 열인지 직접 선택하게 한 뒤 가져옵니다.
    /// </summary>
    private void ImportFromFile(string filePath)
    {
        try
        {
            using var package = Path.GetExtension(filePath).Equals(".csv", StringComparison.OrdinalIgnoreCase)
                ? CsvWorkbookReader.LoadAsPackage(filePath)
                : ExcelFileOpener.OpenWithPasswordPrompt(filePath, this);
            if (package == null) return;

            using var mappingDialog = new MasterSkuImportMappingDialog(package);
            if (FormManager.ShowDialogSafe(mappingDialog, this) != DialogResult.OK) return;

            var worksheet = package.Workbook.Worksheets[mappingDialog.SheetName];
            var headerRow = mappingDialog.HeaderRow;

            var headerToIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (int col = 1; col <= worksheet.Dimension.End.Column; col++)
            {
                var header = worksheet.Cells[headerRow, col].Value?.ToString();
                if (!string.IsNullOrEmpty(header) && !headerToIndexMap.ContainsKey(header))
                {
                    headerToIndexMap[header] = col;
                }
            }

            var skuCol = headerToIndexMap[mappingDialog.SkuColumn];
            var itemNameCol = headerToIndexMap[mappingDialog.ItemNameColumn];
            var costPriceCol = headerToIndexMap[mappingDialog.CostPriceColumn];
            var productGroupCol = mappingDialog.ProductGroupColumn is { } pg ? headerToIndexMap.GetValueOrDefault(pg) : 0;
            var reserve1Col = mappingDialog.Reserve1Column is { } r1 ? headerToIndexMap.GetValueOrDefault(r1) : 0;
            var reserve2Col = mappingDialog.Reserve2Column is { } r2 ? headerToIndexMap.GetValueOrDefault(r2) : 0;
            var reserve3Col = mappingDialog.Reserve3Column is { } r3 ? headerToIndexMap.GetValueOrDefault(r3) : 0;

            var itemsToImport = new List<ItemModel>();
            for (int row = headerRow + 1; row <= worksheet.Dimension.End.Row; row++)
            {
                var sku = worksheet.Cells[row, skuCol].Value?.ToString();
                if (string.IsNullOrWhiteSpace(sku)) continue;

                var itemName = worksheet.Cells[row, itemNameCol].Value?.ToString() ?? string.Empty;
                if (!decimal.TryParse(worksheet.Cells[row, costPriceCol].Value?.ToString(), out var costPrice))
                {
                    costPrice = 0; // 파싱 실패 시 0으로 처리
                }

                itemsToImport.Add(new ItemModel
                {
                    Sku = sku,
                    ItemName = itemName,
                    CostPrice = costPrice,
                    ProductGroup = productGroupCol > 0 ? worksheet.Cells[row, productGroupCol].Value?.ToString() : null,
                    Reserve1 = reserve1Col > 0 ? worksheet.Cells[row, reserve1Col].Value?.ToString() : null,
                    Reserve2 = reserve2Col > 0 ? worksheet.Cells[row, reserve2Col].Value?.ToString() : null,
                    Reserve3 = reserve3Col > 0 ? worksheet.Cells[row, reserve3Col].Value?.ToString() : null,
                });
            }

            if (itemsToImport.Count == 0)
            {
                MessageBox.Show("가져올 유효한 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var result = MessageBox.Show($"{itemsToImport.Count}개의 품목을 읽었습니다. 데이터베이스에 반영하시겠습니까?", "가져오기 확인", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (result == DialogResult.Yes)
            {
                foreach (var item in itemsToImport)
                {
                    _itemRepository.Upsert(item);
                }
                LoadData(); // 그리드 새로고침
                _statusLabel.ForeColor = Color.DarkGreen;
                _statusLabel.Text = $"데이터를 성공적으로 반영했습니다. ({DateTime.Now:HH:mm:ss})";
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 파일을 읽는 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void OnExportClick(object? sender, EventArgs e)
    {
        if (_itemsGrid.Rows.Count == 0)
        {
            MessageBox.Show("내보낼 데이터가 없습니다.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // 기획서 2.4절: 기능별 마지막 폴더 위치 기억
        var filePath = ExportHelper.ShowSaveFileDialog(this, "Excel Files (*.xlsx)|*.xlsx",
            $"MasterSKU_{DateTime.Now:yyyyMMdd}.xlsx",
            _settingsService.GetLastFolder("MasterSkuExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments));

        if (filePath == null) return;

        _settingsService.SetLastFolder("MasterSkuExport", Path.GetDirectoryName(filePath)!);

        try
        {
            // EPPlus 라이선스 설정 (비상업적 용도)
            ExcelLicense.Ensure();

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("MasterSKU");

            // 그리드의 현재 보이는 열 순서대로 헤더를 만듭니다.
            var visibleColumns = _itemsGrid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible && c.Tag as string != "no-export")
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            for (var i = 0; i < visibleColumns.Count; i++)
            {
                worksheet.Cells[1, i + 1].Value = visibleColumns[i].HeaderText;
            }

            // 데이터 바인딩된 아이템들을 순회하며 셀에 값을 채웁니다.
            for (var rowIndex = 0; rowIndex < _items.Count; rowIndex++)
            {
                var item = _items[rowIndex];
                worksheet.Cells[rowIndex + 2, 1].Value = item.Sku;
                worksheet.Cells[rowIndex + 2, 2].Value = item.ItemName;
                worksheet.Cells[rowIndex + 2, 3].Value = item.CostPrice;
                worksheet.Cells[rowIndex + 2, 4].Value = item.ProductGroup;
                worksheet.Cells[rowIndex + 2, 5].Value = item.Reserve1;
                worksheet.Cells[rowIndex + 2, 6].Value = item.Reserve2;
                worksheet.Cells[rowIndex + 2, 7].Value = item.Reserve3;
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            ExportHelper.SaveExcel(package, filePath);

            // 기획서 2.2절: 엑셀 내보내기 후 처리 공통 다이얼로그 호출
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ExportHelper.DescribeSaveError(ex)}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}