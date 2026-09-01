namespace MiniERP2.Models;

/// <summary>
/// 메인 허브 검색창이 쓰는 검색 대상 한 줄. 최상위 메뉴 화면 자체이거나, 그 화면(또는 그 화면에서
/// 열리는 하위 창) 안에 있는 버튼 하나를 가리킨다. 버튼을 자동으로 누르지는 않는다 — 대부분의 하위
/// 버튼은 파일 선택이나 사전 조건(행 선택 등)이 있어 자동 클릭이 오히려 위험하므로, 검색 결과를
/// 고르면 그 기능이 있는 화면을 열고 Path에 적힌 경로를 안내하는 데 그친다.
/// </summary>
public sealed record FeatureIndexEntry(string Group, string TopLabel, string? Path, Action Open)
{
    public string DisplayText => Path is null ? $"{Group} > {TopLabel}" : $"{Group} > {TopLabel} > {Path}";

    public string SearchText => Path is null ? $"{Group} {TopLabel}" : $"{Group} {TopLabel} {Path}";
}
