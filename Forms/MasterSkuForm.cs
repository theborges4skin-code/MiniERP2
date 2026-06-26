using System.ComponentModel;
using MiniERP2.Config;
using MiniERP2.Controls;
using MiniERP2.Database;
using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Forms;

/// <summary>
/// 기획서 5.2절 '마스터SKU 관리창'
/// </summary>
public class MasterSkuForm : Form
{
    private readonly ItemRepository _itemRepository = new();
    private readonly SettingsService _settingsService = new();
    private ExcelLikeDataGridView _itemsGrid = new();
    private BindingList<ItemModel> _items = new();

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
        var btnSave = new Button { Text = "저장", Size = new Size(100, 30) };
        var btnExport = new Button { Text = "엑셀로 내보내기", Size = new Size(120, 30) };

        btnRefresh.Click += OnRefreshClick;
        btnSave.Click += OnSaveClick;
        btnExport.Click += OnExportClick;

        toolStrip.Controls.Add(btnRefresh);
        toolStrip.Controls.Add(btnSave);
        toolStrip.Controls.Add(btnExport);

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
            new DataGridViewTextBoxColumn { Name = "ItemName", HeaderText = "상품명", DataPropertyName = "ItemName", Width = 300 },
            new DataGridViewTextBoxColumn { Name = "CostPrice", HeaderText = "원가", DataPropertyName = "CostPrice", Width = 120 }
        );

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

        _itemsGrid.ContextMenuStrip!.Items.Add(new ToolStripSeparator());
        _itemsGrid.ContextMenuStrip.Items.Add(historyMenuItem);

        // 메뉴가 열릴 때, 선택된 행이 1개일 때만 '이력 보기' 메뉴 활성화
        _itemsGrid.ContextMenuStrip.Opening += (s, e) =>
        {
            historyMenuItem.Enabled = _itemsGrid.SelectedRows.Count == 1;
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

        new CostHistoryForm(item.Sku).ShowDialog(this);
    }

    private void OnGridCellDoubleClick(object? sender, DataGridViewCellEventArgs e)
    {
        // 헤더를 더블클릭했거나, 유효하지 않은 행/열 인덱스인 경우 무시
        if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

        // 'Sku' 열을 더블클릭했는지 확인
        if (_itemsGrid.Columns[e.ColumnIndex].Name == "Sku")
        {
            var sku = _itemsGrid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value as string;

            if (!string.IsNullOrWhiteSpace(sku))
            {
                // CSKU 관리창을 모달 다이얼로그로 엽니다.
                new CSkuForm(sku).ShowDialog(this);
            }
        }
    }

    private void LoadData()
    {
        var allItems = _itemRepository.GetAll();
        _items = new BindingList<ItemModel>(allItems);
        _itemsGrid.DataSource = _items;
    }

    private void OnRefreshClick(object? sender, EventArgs e)
    {
        LoadData();
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
            MessageBox.Show("성공적으로 저장되었습니다.", "저장 완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
            LoadData(); // 저장 후 데이터를 다시 불러와 동기화
        }
        catch (Exception ex)
        {
            MessageBox.Show($"저장 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            MessageBox.Show("삭제되었습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
        var filePath = files[0];

        try
        {
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;
            using var package = new ExcelPackage(new FileInfo(filePath));
            var worksheet = package.Workbook.Worksheets.FirstOrDefault();

            if (worksheet == null)
            {
                MessageBox.Show("엑셀 파일에 워크시트가 없습니다.", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            var itemsToImport = new List<ItemModel>();
            // 2번째 행부터 (헤더 제외)
            for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
            {
                var sku = worksheet.Cells[row, 1].Value?.ToString();
                if (string.IsNullOrWhiteSpace(sku)) continue;

                var itemName = worksheet.Cells[row, 2].Value?.ToString() ?? string.Empty;
                if (!decimal.TryParse(worksheet.Cells[row, 3].Value?.ToString(), out var costPrice))
                {
                    costPrice = 0; // 파싱 실패 시 0으로 처리
                }

                itemsToImport.Add(new ItemModel { Sku = sku, ItemName = itemName, CostPrice = costPrice });
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
                MessageBox.Show("데이터를 성공적으로 반영했습니다.", "완료", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadData(); // 그리드 새로고침
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

        using var sfd = new SaveFileDialog
        {
            Filter = "Excel Files (*.xlsx)|*.xlsx",
            FileName = $"MasterSKU_{DateTime.Now:yyyyMMdd}.xlsx",
            // 기획서 2.4절: 기능별 마지막 폴더 위치 기억
            InitialDirectory = _settingsService.GetLastFolder("MasterSkuExport") ?? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (sfd.ShowDialog() != DialogResult.OK) return;

        var filePath = sfd.FileName;
        _settingsService.SetLastFolder("MasterSkuExport", Path.GetDirectoryName(filePath)!);

        try
        {
            // EPPlus 라이선스 설정 (비상업적 용도)
            ExcelPackage.LicenseContext = OfficeOpenXml.LicenseContext.NonCommercial;

            using var package = new ExcelPackage();
            var worksheet = package.Workbook.Worksheets.Add("MasterSKU");

            // 그리드의 현재 보이는 열 순서대로 헤더를 만듭니다.
            var visibleColumns = _itemsGrid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible)
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
            }

            worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
            package.SaveAs(new FileInfo(filePath));

            // 기획서 2.2절: 엑셀 내보내기 후 처리 공통 다이얼로그 호출
            ExportHelper.ShowPostExportDialog(this, filePath);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"엑셀 내보내기 중 오류가 발생했습니다.\n{ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}