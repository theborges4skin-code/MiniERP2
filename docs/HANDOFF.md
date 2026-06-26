# MiniERP2 인수인계 — 2026-06-26 Claude Code 세션

이 문서는 2026-06-26 Claude Code 세션에서 진행한 작업을 다음 작업자(미래의 Claude 세션 포함)가
이어받을 수 있도록 정리한 것이다. 프로젝트 전체 배경/아키텍처는 [PLAN.md](PLAN.md) 참고.

## 세션 시작 시점 상태

- git에는 "first commit" 1개만 있었고, 실제 Gemini가 개발한 Phase 2~6 분량 코드는 전부 미커밋 상태였다.
- 미커밋 코드 자체에 빌드 오류 4건이 있었다(`ChannelConfigForm.cs`의 오타, `StdField` 필드 누락 등).

## 이번 세션에서 한 일 (커밋 순서대로)

1. **백업 + 빌드 오류 수정** — Gemini 작업분 커밋, 빌드 오류 4건 수정.
2. **Phase 7 (마감/이익분석) 신규 구현** — `ProfitCalculator`(일반/쿠팡그로스/아마존 VAT보정 공식), `SettlementLoader`, `SettlementForm`(이익분석 자동 + 마감대조 수기).
3. **OFS CourierExporter 연동 + EPPlus 8 버그 수정** — `ExcelPackage.LicenseContext` 설정이 EPPlus 8부터 런타임 예외를 던지는 치명적 버그를 발견, `Utils/ExcelLicense.Ensure()`로 전체 교체.
4. **매핑 충돌 자동 감지** — `MappingConflictDetector`, 매핑관리창에 충돌 탭 + 행 강조.
5. **GrowthAuxSource JOIN 엔진** — 쿠팡그로스 등 보조시트(배송비/입출고비) JOIN 구현(`GrowthAuxJoinEngine`).
6. **버그 수정** — 택배사 양식 재오픈 시 콤보박스 크래시, 채널선택창에 신규채널 추가 버튼 누락.
7. **채널유형/필드고정값/택배사고정텍스트/엑셀미리보기** — `ChannelType`에 거래처/기타 추가, `FieldMapping.FixedValue`, `CourierHeaderOverride`(채널별 택배사 출력 고정값), 채널설정/마스터DB/택배사양식 3곳 모두 "엑셀 불러와서 보면서 설정" 패턴 적용.
8. **헤더행 경고/미매핑 안내/채널명 동기화 버그** — 헤더행이 비거나 병합되어 매핑 실패 시 경고, 미매핑건 자동으로 매핑창 안내, 채널명 PropertyGrid 수정이 트리에 반영 안 되던 버그 수정.
9. **마스터DB 예비필드/가져오기 개선 + SKU 매핑 도우미** — `Reserve1~3`, `OrderSkuMappingDialog`(마스터DB 검색 + VAT 변환 + 임시SKU 등록).
10. **DB 마이그레이션 버그 수정** — `CREATE TABLE IF NOT EXISTS`는 기존 테이블에 컬럼을 추가 안 해줌. `DbSchema.EnsureColumn`으로 보강. `ProductGroup` 필드 추가.

각 커밋 메시지에 더 자세한 설명이 있으니 `git log`로 확인.

## 현재 빌드/테스트 상태

```
dotnet build MiniERP2.csproj   # 오류 0
dotnet test Tests/MiniERP2.Tests.csproj   # 51/51 통과
```

## 알아둬야 할 설계 결정 / 임의 선택 사항

- **임시 SKU 네이밍**: `TEMP001, TEMP002...` 전역 순번(채널 구분 없음). `Utils/TempSkuGenerator.cs`.
- **채널 코드 자동 생성**: `CH001, CH002...` 순번. `Utils/ChannelCodeGenerator.cs`.
- **SKU 매핑 도우미로 선택한 매핑은 1회성**이다. 매핑관리창의 영구 규칙(RuleExact 등)에는 자동 저장되지 않음 — 같은 상품이 또 나오면 다시 도우미를 써야 한다. 영구 규칙 자동 등록을 원하면 추가 작업 필요.
- **임시 SKU의 마스터DB 원가는 0으로 등록**된다. 추후 마스터SKU 관리창에서 직접 보완 필요.
- **납품단가는 항상 VAT포함 기준으로 저장**(마스터DB 제조원가와 동일 기준 통일). 입력 시 VAT별도를 고르면 ×1.1 환산.
- **GrowthAuxSource JOIN은 메인 시트와 보조 시트에 동일한 이름의 키 컬럼(예: "옵션ID")이 있어야 동작**한다.
- **DB 스키마 변경 시 주의**: `CREATE TABLE IF NOT EXISTS`만으로는 기존 DB 파일에 컬럼이 추가되지 않는다. 새 컬럼을 추가할 때는 `DbSchema.EnsureColumn(connection, "테이블명", "컬럼명", "TEXT")` 패턴으로 마이그레이션도 같이 추가해야 한다(이번 세션에서 이걸 빠뜨려 마스터SKU 관리창이 크래시했었다 — 재발 방지용 패턴이 이미 `DbSchema.cs`에 있음, 새 컬럼 추가 시 반드시 따라할 것).

## 알려진 미구현/제약사항 (이전 세션 노트, 여전히 유효)

- CSV 파일 실제 파싱 미구현(다이얼로그 필터에는 있으나 OrderLoader/SettlementLoader는 xlsx만 처리)
- 암호 걸린 엑셀 파일 자동 해제 미구현
- 창 크기/위치 기억 미구현(`FormManager`는 중복실행 방지만 구현됨)
- 이중 출고 방지(Upsert) 미구현 — OFS에서 같은 주문 여러 번 저장 시 `OutboundDetailTable`에 중복 저장됨
- `ExcelLikeDataGridView`의 붙여넣기 로직 TODO 상태
- MainHub 마스터 데이터 동기화 상태 요약 미구현
- 문서관리(PDF 출력, Phase 8) 전체 미구현
- 이익분석 결과의 SalesManagerV2 실데이터 대비 회귀검증 미수행(PLAN.md 명세를 정답으로 한 단위테스트만 존재)
- 운송장 매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴(PLAN.md 5.5절 신규설계 항목) 미착수

## 다음 작업 후보 (우선순위 제안)

1. SKU 매핑 도우미에서 선택한 매핑을 영구 규칙(RuleExact)으로도 저장할지 사용자에게 물어보는 옵션 추가
2. Phase 8 문서관리(거래명세표/견적서/마감내역서 PDF 출력)
3. 이중 출고 방지(Upsert) — `OutboundRepository.SaveOutbound`가 단순 INSERT라 중복 가능
4. `ExcelLikeDataGridView` 붙여넣기 로직 구현
5. 실데이터 기준 SalesManagerV2 대비 이익분석 회귀테스트

## 참고

- Notion "작업일지" 페이지에 이번 세션 작업 내역이 추가됨: https://app.notion.com/p/38bde24a14bc80488ff9d2f25e485ffa
- Notion "기능 테스트 체크리스트" 페이지(이전 세션 작성, 현재 기준 일부 항목은 이번 세션에서 해소됨): https://app.notion.com/p/38bde24a14bc81348051fd2d32843288
