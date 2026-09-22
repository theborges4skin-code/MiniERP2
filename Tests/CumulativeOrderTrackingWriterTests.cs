using MiniERP2.Models;
using MiniERP2.Utils;
using OfficeOpenXml;

namespace MiniERP2.Tests;

/// <summary>
/// 누적발주서 송장번호 역기입(이력 → 엑셀 파일) 검증. 거래처에 회신하는 파일을 프로그램이 다시
/// 쓰는 기능이라, "맞는 칸에 맞는 번호가 들어가는가"뿐 아니라 "확신 없을 땐 쓰지 않는가"와
/// "암호가 유지되는가"까지 확인한다.
/// </summary>
[TestClass]
public class CumulativeOrderTrackingWriterTests
{
    private string _folder = string.Empty;
    private string _filePath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "MiniERP2Tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_folder);
        _filePath = Path.Combine(_folder, "누적발주서.xlsx");
        ExcelLicense.Ensure();
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_folder, recursive: true);

    private static ChannelConfig NewConfig() => new()
    {
        ChannelCode = "CH002",
        ChannelName = "푸디",
        OrderFieldMappings = new Dictionary<StdField, FieldMapping>
        {
            [StdField.Recipient] = new() { HeaderRow = 1, Column = "수취인" },
            [StdField.Phone] = new() { HeaderRow = 1, Column = "수취인 연락처" },
            [StdField.Address] = new() { HeaderRow = 1, Column = "수취인 주소" },
            [StdField.ProductName] = new() { HeaderRow = 1, Column = "상품명" },
            [StdField.Quantity] = new() { HeaderRow = 1, Column = "수량" },
            [StdField.TrackingNo] = new() { HeaderRow = 1, Column = "송장번호" },
            [StdField.CourierName] = new() { HeaderRow = 1, Column = "택배사" },
        }
    };

    /// <summary>헤더 1행 + 주어진 데이터 행으로 파일을 만든다(각 행: 수취인, 전화, 주소, 상품명, 수량, 송장번호).</summary>
    private void CreateFile(string?[][] rows, string? password = null)
    {
        using var package = new ExcelPackage();
        var sheet = package.Workbook.Worksheets.Add("2026");
        var headers = new[] { "수취인", "수취인 연락처", "수취인 주소", "상품명", "수량", "송장번호", "택배사" };
        for (var i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];

        for (var r = 0; r < rows.Length; r++)
            for (var c = 0; c < rows[r].Length; c++)
                sheet.Cells[r + 2, c + 1].Value = rows[r][c];

        if (password != null)
        {
            package.Encryption.IsEncrypted = true;
            package.Encryption.Password = password;
        }
        package.SaveAs(new FileInfo(_filePath));
    }

    private static OutboundDetail Detail(string recipient, string address, string tracking,
        string? phone = null, string? productName = null, int qty = 0, string? courier = null) => new()
    {
        ChannelCode = "CH002",
        Recipient = recipient,
        Address = address,
        Phone = phone ?? "",
        ProductName = productName ?? "",
        Qty = qty,
        TrackingNo = tracking,
        CourierName = courier ?? ""
    };

    private string ReadCell(int row, int col, string? password = null)
    {
        using var package = ExcelFileOpener.Open(_filePath, password);
        return package.Workbook.Worksheets["2026"].Cells[row, col].Text;
    }

    [TestMethod]
    public void 수령인과_주소가_같으면_송장번호를_써넣는다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시 500ml", "12", null]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(1, result.WrittenRows);
        Assert.AreEqual("6003-9615-8696", ReadCell(2, 6));
    }

    [TestMethod]
    public void 공백과_대소문자_차이는_같은_값으로_본다()
    {
        CreateFile([["  수지동천지점 ", "01049127218", "경기도  용인시 수지구 수지로 419", "핸드워시", "1", null]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(1, result.WrittenRows);
    }

    [TestMethod]
    public void 이미_같은_번호가_있으면_그대로_두고_쓰지_않는다()
    {
        CreateFile([["압구정역지점", "01042961771", "서울 강남구 압구정로 208", "핸드워시 4L", "1", "6002-5271-2644"]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("압구정역지점", "서울 강남구 압구정로 208", "600252712644")]);

        Assert.AreEqual(0, result.WrittenRows);
        Assert.AreEqual(1, result.AlreadySameRows);
        Assert.AreEqual("6002-5271-2644", ReadCell(2, 6), "하이픈 유무만 다른 같은 번호로 칸을 덮어쓰면 안 된다.");
    }

    [TestMethod]
    public void 다른_번호가_적혀_있으면_기본적으로_덮어쓰지_않는다()
    {
        CreateFile([["압구정역지점", "01042961771", "서울 강남구 압구정로 208", "핸드워시 4L", "1", "1111-1111-1111"]]);
        var config = NewConfig();
        var history = new[] { Detail("압구정역지점", "서울 강남구 압구정로 208", "6002-5271-2644") };

        var kept = CumulativeOrderTrackingWriter.Write(_filePath, null, config, history);
        Assert.AreEqual(1, kept.ConflictRows);
        Assert.AreEqual("1111-1111-1111", ReadCell(2, 6));

        var overwritten = CumulativeOrderTrackingWriter.Write(_filePath, null, config, history, overwriteExisting: true);
        Assert.AreEqual(1, overwritten.WrittenRows);
        Assert.AreEqual("6002-5271-2644", ReadCell(2, 6));
    }

    [TestMethod]
    public void 같은_수령인에_송장이_여럿이면_품목명으로_좁혀_쓴다()
    {
        CreateFile([
            ["성남공단금융센터", "01082866381", "경기 성남시 갈마치로 245", "핸드워시 4L-머스캣", "1", null],
            ["성남공단금융센터", "01082866381", "경기 성남시 갈마치로 245", "핸드워시 300ml-머스캣", "1", null],
        ]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
        [
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "1111-1111-1111", productName: "핸드워시 4L-머스캣"),
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "2222-2222-2222", productName: "핸드워시 300ml-머스캣"),
        ]);

        Assert.AreEqual(2, result.WrittenRows);
        Assert.AreEqual("1111-1111-1111", ReadCell(2, 6));
        Assert.AreEqual("2222-2222-2222", ReadCell(3, 6));
    }

    [TestMethod]
    public void 하나로_좁히지_못하면_쓰지_않고_보고만_한다()
    {
        CreateFile([["성남공단금융센터", "01082866381", "경기 성남시 갈마치로 245", "핸드워시", "1", null]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
        [
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "1111-1111-1111"),
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "2222-2222-2222"),
        ]);

        Assert.AreEqual(0, result.WrittenRows);
        Assert.AreEqual(1, result.AmbiguousRows);
        Assert.AreEqual("", ReadCell(2, 6));
    }

    [TestMethod]
    public void 후보가_여럿이어도_적힌_번호가_그중_하나면_끝난_행으로_본다()
    {
        // 같은 지점이 여러 번 주문해 후보가 둘이어도, 파일에 이미 그중 하나가 적혀 있으면 확인할 게 없다.
        CreateFile([["성남공단금융센터", "01082866381", "경기 성남시 갈마치로 245", "핸드워시", "1", "2222-2222-2222"]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
        [
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "1111-1111-1111"),
            Detail("성남공단금융센터", "경기 성남시 갈마치로 245", "2222-2222-2222"),
        ]);

        Assert.AreEqual(1, result.AlreadySameRows);
        Assert.AreEqual(0, result.AmbiguousRows);
    }

    [TestMethod]
    public void 이력에_없는_행은_비워두고_건수로_보고한다()
    {
        CreateFile([["다른지점", "01000000000", "부산 어딘가", "핸드워시", "1", null]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(0, result.WrittenRows);
        Assert.AreEqual(1, result.NoHistoryRows);
    }

    [TestMethod]
    public void 이미_채워진_행은_이력에_없어도_미매칭으로_세지_않는다()
    {
        // 지난달 주문처럼 조회 범위 밖이라 이력 후보에 없을 뿐, 파일은 이미 완성된 행이다.
        CreateFile([["지난달지점", "01000000000", "서울 어딘가", "핸드워시", "1", "9999-9999-9999"]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(0, result.NoHistoryRows);
    }

    [TestMethod]
    public void 암호가_걸린_파일은_같은_암호로_다시_저장된다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null]], password: "0429");

        var result = CumulativeOrderTrackingWriter.Write(_filePath, "0429", NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696", courier: "CJ대한통운")]);

        Assert.AreEqual(1, result.WrittenRows);
        Assert.AreEqual("6003-9615-8696", ReadCell(2, 6, "0429"));
        Assert.AreEqual("CJ대한통운", ReadCell(2, 7, "0429"), "택배사 칸이 비어 있으면 이력의 택배사명도 함께 채운다.");
        Assert.ThrowsExactly<EncryptedExcelFileException>(() => ExcelFileOpener.Open(_filePath),
            "저장 후에도 암호가 유지돼야 한다.");
    }

    [TestMethod]
    public void 채널설정의_택배사_고정값을_택배사_칸에_채운다()
    {
        // 푸디처럼 이력의 택배사명이 비어 있는 채널에서, 회신 파일의 택배사 칸을 채우는 경로.
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null]]);
        var config = NewConfig();
        config.OrderFieldMappings[StdField.CourierName] = new FieldMapping { HeaderRow = 1, Column = "택배사", FixedValue = "CJ택배" };

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, config,
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(1, result.CourierWrittenRows);
        Assert.AreEqual("CJ택배", ReadCell(2, 7));
    }

    [TestMethod]
    public void 송장번호가_이미_맞게_적힌_행도_택배사_칸이_비었으면_채운다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", "6003-9615-8696"]]);
        var config = NewConfig();
        config.OrderFieldMappings[StdField.CourierName] = new FieldMapping { HeaderRow = 1, Column = "택배사", FixedValue = "CJ택배" };

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, config,
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(0, result.WrittenRows);
        Assert.AreEqual(1, result.AlreadySameRows);
        Assert.AreEqual("CJ택배", ReadCell(2, 7));
    }

    [TestMethod]
    public void 택배사_칸에_값이_있으면_고정값으로_덮어쓰지_않는다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null, "로젠택배"]]);
        var config = NewConfig();
        config.OrderFieldMappings[StdField.CourierName] = new FieldMapping { HeaderRow = 1, Column = "택배사", FixedValue = "CJ택배" };

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, config,
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.AreEqual(0, result.CourierWrittenRows);
        Assert.AreEqual("로젠택배", ReadCell(2, 7), "거래처가 직접 적어둔 택배사를 덮어쓰면 안 된다.");
    }

    [TestMethod]
    public void 이력의_택배사명이_있으면_고정값보다_우선한다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null]]);
        var config = NewConfig();
        config.OrderFieldMappings[StdField.CourierName] = new FieldMapping { HeaderRow = 1, Column = "택배사", FixedValue = "CJ택배" };

        CumulativeOrderTrackingWriter.Write(_filePath, null, config,
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696", courier: "우체국택배")]);

        Assert.AreEqual("우체국택배", ReadCell(2, 7));
    }

    [TestMethod]
    public void 쓴_것이_있으면_원본을_백업해둔다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null]]);

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, NewConfig(),
            [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]);

        Assert.IsNotNull(result.BackupPath);
        Assert.IsTrue(File.Exists(result.BackupPath));
    }

    [TestMethod]
    public void 주소가_두_열로_나뉜_발주서도_합쳐서_이력과_매칭한다()
    {
        // 발주서의 "주소"+"주소상세"를 합친 값이 이력에 저장돼 있으므로(OrderLoader.CombineAddress),
        // 역기입 매칭도 같은 방식으로 합쳐야 한다 — 주소 열만 보면 이력을 못 찾는다.
        using (var package = new ExcelPackage())
        {
            var sheet = package.Workbook.Worksheets.Add("2026");
            var headers = new[] { "수취인", "수취인 연락처", "수취인 주소", "수취인 주소상세", "상품명", "수량", "송장번호", "택배사" };
            for (var i = 0; i < headers.Length; i++) sheet.Cells[1, i + 1].Value = headers[i];
            var row = new[] { "메세나폴리스점", "01042961771", "서울특별시 마포구 양화로 45 (서교동, 메세나폴리스)", "102동  2504호", "핸드워시", "1", null };
            for (var c = 0; c < row.Length; c++) sheet.Cells[2, c + 1].Value = row[c];
            package.SaveAs(new FileInfo(_filePath));
        }

        var config = NewConfig();
        config.OrderFieldMappings[StdField.AddressDetail] = new FieldMapping { HeaderRow = 1, Column = "수취인 주소상세" };
        config.OrderFieldMappings[StdField.ProductName] = new FieldMapping { HeaderRow = 1, Column = "상품명" };
        config.OrderFieldMappings[StdField.Quantity] = new FieldMapping { HeaderRow = 1, Column = "수량" };
        config.OrderFieldMappings[StdField.TrackingNo] = new FieldMapping { HeaderRow = 1, Column = "송장번호" };

        var result = CumulativeOrderTrackingWriter.Write(_filePath, null, config,
            [Detail("메세나폴리스점", "서울특별시 마포구 양화로 45 (서교동, 메세나폴리스) 102동  2504호", "6003-9615-8696")]);

        Assert.AreEqual(1, result.WrittenRows);
        Assert.AreEqual("6003-9615-8696", ReadCell(2, 7));
    }

    [TestMethod]
    public void 송장번호_열을_못_찾으면_파일의_실제_헤더를_알려준다()
    {
        CreateFile([["수지동천지점", "01049127218", "경기도 용인시 수지구 수지로 419", "핸드워시", "12", null]]);
        var config = NewConfig();
        config.OrderFieldMappings[StdField.TrackingNo] = new FieldMapping { HeaderRow = 1, Column = "운송장번호" };

        var ex = Assert.ThrowsExactly<InvalidOperationException>(() =>
            CumulativeOrderTrackingWriter.Write(_filePath, null, config,
                [Detail("수지동천지점", "경기도 용인시 수지구 수지로 419", "6003-9615-8696")]));

        StringAssert.Contains(ex.Message, "운송장번호");
        StringAssert.Contains(ex.Message, "송장번호", "파일에 실제로 있는 헤더 목록을 보여줘야 어디를 고칠지 알 수 있다.");
    }
}
