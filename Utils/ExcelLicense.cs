using OfficeOpenXml;

namespace MiniERP2.Utils;

/// <summary>
/// EPPlus 8부터 라이선스 설정 방식이 ExcelPackage.License API로 변경되었습니다.
/// 이 메서드를 EPPlus를 사용하는 모든 코드의 진입점에서 호출해야 합니다(중복 호출은 안전합니다).
/// </summary>
public static class ExcelLicense
{
    private static bool _isSet;

    public static void Ensure()
    {
        if (_isSet) return;
        ExcelPackage.License.SetNonCommercialPersonal("MiniERP2");
        _isSet = true;
    }
}
