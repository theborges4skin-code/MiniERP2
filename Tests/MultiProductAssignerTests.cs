using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;
using MiniERP2.Controls;
using MiniERP2.Models;
using MiniERP2.Utils;
using Slot = MiniERP2.Utils.MultiProductAssigner.ProductSlot;

namespace MiniERP2.Tests;

[TestClass]
public class MultiProductAssignerTests
{
    private static OfsOrderItem ManualRow() => new()
    {
        ChannelCode = "CH1",
        OrderNo = "M-1",
        Recipient = "홍길동",
        Phone = "010-1111-2222",
        Address = "서울시 어딘가",
        DeliveryMessage = "문앞",
        Quantity = 3,
        Status = "수동 추가",
        LineKind = LineKinds.Cs,
    };

    [TestMethod]
    public void Apply_SingleSlotWithoutQuantity_ReplacesSkuAndKeepsQuantity()
    {
        var row = ManualRow();

        var extras = MultiProductAssigner.Apply(row, [new Slot("A", "상품A", "송장A", null)], "수동 매핑");

        Assert.AreEqual(0, extras.Count);
        Assert.AreEqual("A", row.MappedSku);
        Assert.AreEqual("송장A", row.InvoiceDisplayName);
        Assert.AreEqual("상품A", row.ProductName);
        Assert.AreEqual(3, row.Quantity);
        Assert.AreEqual("수동 매핑", row.Status);
        Assert.IsNull(row.ShipmentGroupId, "품목이 하나면 묶음을 건드리지 않는다.");
    }

    [TestMethod]
    public void Apply_MultipleSlots_AddsRowsWithSameRecipientInOneShipment()
    {
        var row = ManualRow();

        var extras = MultiProductAssigner.Apply(row,
            [new Slot("A", "상품A", null, 1), new Slot("B", "상품B", null, 2), new Slot("C", "상품C", null, 5)],
            "수동 매핑");

        Assert.AreEqual(2, extras.Count);
        Assert.AreEqual("A", row.MappedSku);
        Assert.AreEqual(1, row.Quantity);
        CollectionAssert.AreEqual(new[] { "B", "C" }, extras.Select(e => e.MappedSku).ToArray());
        CollectionAssert.AreEqual(new[] { 2, 5 }, extras.Select(e => e.Quantity).ToArray());

        Assert.IsNotNull(row.ShipmentGroupId);
        Assert.IsTrue(extras.All(e => e.ShipmentGroupId == row.ShipmentGroupId), "모든 품목이 한 송장(묶음)이어야 한다.");
        foreach (var extra in extras)
        {
            Assert.AreEqual(row.Recipient, extra.Recipient);
            Assert.AreEqual(row.Phone, extra.Phone);
            Assert.AreEqual(row.Address, extra.Address);
            Assert.AreEqual(row.DeliveryMessage, extra.DeliveryMessage);
            Assert.AreEqual(row.OrderNo, extra.OrderNo);
            Assert.AreEqual(row.ChannelCode, extra.ChannelCode);
            Assert.AreEqual(LineKinds.Cs, extra.LineKind);
            Assert.IsNull(extra.SourceRowKey);
        }
    }

    [TestMethod]
    public void Apply_FileLoadedRow_KeepsOriginalProductName()
    {
        var row = ManualRow();
        row.SourceRowKey = "발주.xlsx#5";
        row.ProductName = "원본 상품명";

        MultiProductAssigner.Apply(row, [new Slot("A", "상품A", null, null)], "수동 매핑");

        Assert.AreEqual("원본 상품명", row.ProductName);
    }

    [TestMethod]
    public void Validate_RejectsEmptyTooManyAndDuplicateSlots()
    {
        Assert.IsNotNull(MultiProductAssigner.Validate([]));
        Assert.IsNotNull(MultiProductAssigner.Validate(
            Enumerable.Range(1, 5).Select(i => new Slot($"S{i}", $"상품{i}", null, 1)).ToList()));
        Assert.IsNotNull(MultiProductAssigner.Validate([new Slot("A", "a", null, 1), new Slot("a", "a", null, 2)]));
        Assert.IsNull(MultiProductAssigner.Validate(
            Enumerable.Range(1, 4).Select(i => new Slot($"S{i}", $"상품{i}", null, 1)).ToList()));
    }

    /// <summary>
    /// 2026-10-14 재현: 그리드 맨 아래 * 줄을 클릭한 상태(그리드가 AddNew로 확정 안 된 항목을 만들어 둠)에서
    /// 원본 BindingList에 줄을 추가하면 InvalidOperationException이 났다. SettlePendingNewRow 후에는
    /// 정상 추가되고, 입력하지 않은 * 줄은 목록에 남지 않아야 한다.
    /// </summary>
    [STATestMethod]
    public void SettlePendingNewRow_AllowsAddingWhileNewRowIsCurrent()
    {
        var list = new BindingList<OfsOrderItem> { new() { OrderNo = "기존" } };
        using var form = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-3000, -3000) };
        var grid = new ExcelLikeDataGridView { Dock = DockStyle.Fill, AllowUserToAddRows = true, AutoGenerateColumns = false };
        grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "OrderNo", DataPropertyName = "OrderNo" });
        form.Controls.Add(grid);
        grid.DataSource = list;
        form.Show();

        grid.CurrentCell = grid.Rows[grid.Rows.Count - 1].Cells[0];
        Assert.IsTrue(grid.CurrentRow!.IsNewRow);

        grid.SettlePendingNewRow();
        list.Add(new OfsOrderItem { OrderNo = "추가1" });
        list.Add(new OfsOrderItem { OrderNo = "추가2" });

        CollectionAssert.AreEqual(new[] { "기존", "추가1", "추가2" }, list.Select(o => o.OrderNo).ToArray());
        Assert.AreEqual(4, grid.Rows.Count, "데이터 3줄 + 입력용 * 줄");
        form.Close();
    }
}
