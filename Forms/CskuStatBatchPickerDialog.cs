using MiniERP2.Database;
using MiniERP2.Models;

namespace MiniERP2.Forms;

/// <summary>
/// CSKU별 통계 "배치 불러오기" 목록(CSKU별통계_개발기획서.md §6, §11-3). 스냅샷 누적 방식이라
/// 잘못 저장한 배치가 계속 남을 수 있어 삭제 버튼도 함께 둔다.
/// </summary>
public class CskuStatBatchPickerDialog : Form
{
    private readonly CskuStatRepository _repo;
    private DataGridView _grid = new();

    public long? SelectedBatchId { get; private set; }

    public CskuStatBatchPickerDialog(CskuStatRepository repo)
    {
        _repo = repo;
        InitializeComponent();
        Reload();
    }

    private void InitializeComponent()
    {
        Text = "배치 불러오기";
        Size = new Size(640, 420);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.Sizable;
        MinimizeBox = false;

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            ReadOnly = true,
            AllowUserToAddRows = false,
        };
        _grid.Columns.AddRange(
            new DataGridViewTextBoxColumn { HeaderText = "Id", DataPropertyName = "Id", Width = 50 },
            new DataGridViewTextBoxColumn { HeaderText = "기간", DataPropertyName = "Period", Width = 90 },
            new DataGridViewTextBoxColumn { HeaderText = "메모", DataPropertyName = "Memo", Width = 160 },
            new DataGridViewTextBoxColumn { HeaderText = "파일수", DataPropertyName = "FileCount", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "행수", DataPropertyName = "RowCount", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "생성일시", DataPropertyName = "CreatedAt", Width = 140 }
        );
        _grid.CellDoubleClick += (s, e) => { if (e.RowIndex >= 0) Accept(); };

        var buttonPanel = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Bottom,
            Height = 40,
        };
        var btnCancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Size = new Size(72, 30) };
        var btnOpen = new Button { Text = "불러오기", Size = new Size(80, 30) };
        var btnDelete = new Button { Text = "삭제", Size = new Size(72, 30) };
        btnOpen.Click += (s, e) => Accept();
        btnDelete.Click += (s, e) => DeleteSelected();
        buttonPanel.Controls.Add(btnCancel);
        buttonPanel.Controls.Add(btnOpen);
        buttonPanel.Controls.Add(btnDelete);
        AcceptButton = btnOpen;
        CancelButton = btnCancel;

        Controls.Add(_grid);
        Controls.Add(buttonPanel);
    }

    private void Reload() => _grid.DataSource = _repo.GetBatches();

    private void Accept()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CskuStatBatch batch) return;
        SelectedBatchId = batch.Id;
        DialogResult = DialogResult.OK;
    }

    private void DeleteSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CskuStatBatch batch) return;
        var confirm = MessageBox.Show(this, $"배치 #{batch.Id} ({batch.Period}, {batch.Memo})를 삭제하시겠습니까?\n이 작업은 되돌릴 수 없습니다.",
            "배치 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
        if (confirm != DialogResult.Yes) return;

        _repo.DeleteBatch(batch.Id);
        Reload();
    }
}
