# MiniERP2 인수인계 — 2026-06-27~28 Claude Code 세션 (자동화 연속 작업)

이 문서는 다음 작업자(미래의 Claude 세션 포함, 특히 이 대화의 auto-compact 이후 이어받는 경우)가
바로 이어받을 수 있도록 진행 상황을 정리한 것이다. 프로젝트 전체 배경/아키텍처는
[PLAN.md](PLAN.md) 참고. 2026-06-26 세션 작업 내역은 git log(커밋 `1322618`~`f7db495`) 참고.
아래 본문은 작업 진행 순서대로 쌓아온 로그라 시간순(110/110→173/173)으로 읽으면 된다 — 최신
상태만 보려면 아래 "지금 빌드/테스트 상태"와 가장 최근 커밋 메시지(`git log -1`)를 보면 된다.

**지금 빌드/테스트 상태(2026-06-28 기준)**: `dotnet build` 오류 0, `dotnet test`
**191/191 통과**. 전부 `origin/main`에 푸시됨(최신 커밋은 `git log -1` 참고).

## 성능 2 — EPPlus worksheet.Dimension이 실제 데이터보다 훨씬 크게 잡히는 문제 — 2026-06-28

SqliteConnectionFactory 캐싱(아래 "성능 1") 이후에도 사용자가 여전히 느리다고 재신고. 데이터가
"가장 적은" 파일조차 느리다는 점이 핵심 힌트 — 실제 행 수에 비례하지 않는 비용이 있다는 뜻이다.

`DataLoaders/SettlementLoader.cs`의 메인 루프는 `for (row = headerRow+1; row <=
worksheet.Dimension.End.Row; row++)`로 도는데, **엑셀 파일에 실제 데이터보다 훨씬 뒤까지 서식
(배경색/테두리 등)만 적용돼 있으면 EPPlus가 그 서식 범위까지를 `Dimension`으로 보고한다** —
실무에서 매우 흔한 패턴(템플릿을 복사해서 만들거나, 한 번 넓게 서식을 깔아둔 보고서 등). 이러면
실제 데이터가 몇 줄이든 상관없이 수천~수만 개의 "유령 행"을 끝까지 순회하면서 행마다 여러 번
`worksheet.Cells[row, col].Value`를 호출하게 된다 — 화면에 보이는 데이터 양과 전혀 무관하게
느려지는 구조라 "데이터가 적어도 느리다"는 신고와 정확히 일치한다.

**고침**: 상품명+옵션명이 모두 빈 행이 **200개 연속**되면 더 이상 실제 데이터가 없다고 보고
루프를 그 자리에서 멈춘다(`maxConsecutiveBlankRows`). 또한 행마다 7개 표준필드를 전부 읽던 것을
상품명/옵션명 2개만 먼저 읽어 빈 행인지 판단한 뒤, 데이터가 있는 행에서만 나머지 5개(수량/정산액/
배송비/입출고비/매출액/송장번호)를 읽도록 순서를 바꿔 빈 행 구간에서의 낭비도 줄였다. 정상적인
정산파일은 실제 데이터 중간에 200줄 연속 공백이 있을 일이 거의 없어 안전하다.

새 테스트 `SettlementLoaderBlankRowGuardTests` — 실제 데이터 3행 + 그 뒤 5,000행에 서식만 입힌
파일로 재현, 5초 이내(여유있게 잡음) 완료되는지 확인. 191/191 통과.

**다음에 또 느리다는 신고가 오면 확인할 곳**: `Mapping/SkuMapper.TryMap`/`TryMapCondition`이
규칙 리스트를 매 행마다 선형 탐색(`FirstOrDefault`/`foreach`)한다 — 채널의 조건부 규칙이
수백 건 이상으로 많아지면(레거시 이관 채널 중 일부는 485건) 이론상 다음 후보. 또한
`Forms/OfsForm.cs`(발주서 OrderLoader)에도 같은 worksheet.Dimension 패턴이 있는지 아직 확인
안 함 — 발주서 로드도 느리다는 신고가 오면 같은 가드를 넣을 것.

## 성능 1 — SqliteConnectionFactory가 연결마다 전체 스키마 마이그레이션을 재실행하던 문제 — 2026-06-28

사용자 신고: 마감/이익분석에서 가장 데이터가 적은 정산파일조차 로드에 1분 이상 걸린다. 원인은
`Database/SqliteConnectionFactory.OpenConnection()`이 **연결을 열 때마다** `DbSchema.
EnsureCreated`(17개 `CREATE TABLE IF NOT EXISTS` + 14개 `EnsureColumn`, 각각 `PRAGMA
table_info` 조회 포함 — 총 30개 이상의 SQL문)를 매번 다시 실행하고 있었던 것. 정산파일을 읽을
때 `SettlementLoader.ApplyMappingAndProfit`이 행마다 `ChannelSkuRepository.ResolveMasterSku`
+ `ItemRepository.GetBySku`를 호출하는데, 이 둘이 각각 `OpenConnection()`을 새로 호출하므로
**행 1개당 30개 이상의 SQL문이 두 번씩 반복 실행**되고 있었다 — 데이터 양과 무관하게 행 수에
비례해 느려지는 구조적 문제라, "데이터가 적어도 느리다"는 신고와 정확히 일치한다.

`SqliteConnectionFactory`에 DB 파일 경로별 캐시(`HashSet<string>` + `Lock`)를 추가해, 같은 DB
파일에 대해서는 **프로세스 생애주기 동안 EnsureCreated를 단 한 번만** 실행하도록 고쳤다(이후
호출은 단순 연결 열기 + 실제 쿼리만 수행). 테스트는 매 테스트마다 다른 임시 폴더를 쓰므로
(`PathProvider.AppDataFolder` 매번 교체) 영향이 없다 — `DbSchemaMigrationTests`의 "구버전
스키마 DB를 열면 첫 접근에서 자동으로 마이그레이션된다"는 동작도 경로별로 처음 한 번은 여전히
실행되므로 그대로 보장된다(190/190 통과로 확인). 이 문제는 정산파일 로드뿐 아니라, 반복적으로
Repository를 호출하는 다른 모든 화면(OFS 매핑 등)에도 똑같이 영향을 줬을 것이라, 이번 수정으로
앱 전반의 DB 호출이 빨라질 것으로 보인다.

## 마감/이익분석 정산파일 로드 — 진행 안내창 추가 — 2026-06-28

사용자 피드백: 정산파일 로드는 이미 `await`/`Task.Run`으로 백그라운드에서 처리되지만, 데이터가
많으면 수 초~수십 초가 걸리는 동안 창이 떠 있어도 클릭이 먹지 않아 멈춘 것처럼 보일 수 있다는
지적. 새 `Forms/LoadingProgressDialog.cs`(범용 비모달 진행 안내창 — 메시지 + Marquee/막대형
ProgressBar, `SetIndeterminate`/`SetProgress`로 갱신)를 만들어 `OnLoadSettlementClick`에서
파일 선택~로드 완료까지 띄워둔다. 파일이 1개면 움직이는 막대(전체 단계를 모를 때), 여러 개면
"(N/전체) 파일명 처리 중..." + 진행률 막대로 표시.

**구현 시 주의한 점(버그 회피)**: 처음에는 로딩 중 메인 창을 `Enabled = false`로도 막으려 했으나,
파일이 암호로 보호돼 있으면 `PasswordPromptDialog.ShowDialog(this)`가 뜨는데, WinForms는 모달
다이얼로그가 닫힐 때 owner의 `Enabled`를 자동으로 `true`로 되돌려놓는다 — 로드 루프가 아직 도는
중에 메인 창이 도중에 다시 활성화되는 버그가 생길 뻔했다. 그래서 `Enabled` 조작은 빼고, 진행
안내창 표시 + 대기 커서만으로 "작업 중"임을 보여주는 것으로 충분히 해결했다.

## 정산서 매핑 표준필드 전면 교체 + 마감/이익분석 그리드 재구성 — 2026-06-28

사용자가 채널설정의 정산서 매핑 표준필드를 다시 설계해달라고 요청. 먼저 현재 등록된 채널
15개 + 각 채널이 실제로 채워둔 표준필드 현황을 `channels_config.json`에서 뽑아 보여주고,
사용자가 원하는 새 필드 목록을 받았다. 구현 전 핵심 설계 두 가지를 `AskUserQuestion`으로 확인:
(1) "이익액"을 표준필드로 두지 않고 — 정산파일에서 직접 읽지 않고 마감/이익분석에서만 도출(=
기존처럼 원가기반 자동계산, ProfitCalculator 로직은 전혀 바꾸지 않음), (2) "실제발송송장수"는
행 단위가 아니라 별도 요약 패널에만 표시(기존 "쿠팡일반 배송비 첫행집계" 패턴은 안 씀).

**채널설정 — 정산서 매핑 표준필드** (`Forms/ChannelConfigForm.cs`): 쿠팡그로스(ChannelType.
CoupangGrowth)만 기존 6필드(상품명/옵션명/수량/정산액/배송비/입출고비)를 그대로 유지하고,
**그 외 모든 채널은** 상품명/옵션명/수량/**매출액**/배송비/정산액/**실제발송송장수** 7필드로
교체했다(`SettlementMappingFieldsDefault` vs `SettlementMappingFieldsCoupangGrowth`,
`ResolveSettlementMappingFields(config)`로 분기). PropertyGrid에서 채널유형을 바꾸면 그 즉시
정산서 매핑 그리드가 새 필드 목록으로 다시 그려진다. `StdField` enum에 `Revenue`(매출액)/
`TrackingNo`(실제발송송장수 = 원본 송장번호 열) 신설 — 기존 enum 값은 그대로 두고 끝에
추가했고, JSON 직렬화가 enum 이름 기준이라(숫자 아님) 기존 channels_config.json과 100% 호환.

**SettlementLoader/SettlementData**: `Revenue`(decimal)/`TrackingNo`(string?)/`ProductGroup`
(string?, 매핑 시점에 마스터SKU의 상품그룹을 캐싱) 3개 속성 신설. `ApplyMappingAndProfit`이
매핑될 때마다 `ProductGroup`도 함께 채운다 — 손익계산 자체(ProfitCalculator 호출부)는 전혀
바꾸지 않았다(정산액=Settlement 기준 그대로).

**마감/이익분석 메인 그리드 재구성** (`Forms/SettlementForm.cs`): 사용자 지정 순서대로
**매핑유무(Status)/채널/상품그룹/매핑SKU/상품명/옵션명/수량/매출액/배송비** 9열만 고정 노출하고
(정산액/입출고비/이익액은 그리드에서 뺐다 — 계산엔 계속 쓰이지만 식별용 그리드엔 더 이상 안
보여줌, 대신 하단 상품그룹별 요약 패널에서 확인), **그 뒤에 채널설정에서 표준필드로 매핑되지
않은 정산파일의 원본 열을 전부 그대로 나열**한다(`RebuildRawTailColumns` — 로드된 행들의
`RawValues` 키 합집합에서, 현재 행들이 속한 채널들의 매핑된 Column 이름을 빼고 남은 헤더를
동적 열로 추가, `CellFormatting`으로 값을 채움). 목적: 판매정보를 상세히 눈으로 보고 어떤
상품인지 파악해서 매핑하기 쉽게 하는 것 — 채널/파일마다 원본 헤더가 달라 로드할 때마다
동적 열을 다시 만든다.

**"실제발송송장수" 요약**: 새 `Utils/ShipmentCountEstimator.cs`(순수 로직, 테스트 가능하게
분리) — 매핑된 송장번호(TrackingNo)가 있으면 중복 제거한 개수, 전혀 없으면(쿠팡처럼 정산파일에
송장번호 열이 없는 경우) 배송비 총계 ÷ 3,000원으로 추정. 그리드에는 행 단위로 안 보이고
`_summaryTotalsLabel`(2번째 줄) + 엑셀 내보내기의 "분석요약(상품그룹별)" 시트 하단에만 표시.

**상품그룹별 요약/엑셀 내보내기**: "매출액" 칸이 기존엔 실수로 정산액(Settlement)을 보여주고
있었는데, 이번에 진짜 매출액(Revenue) 합계로 고쳤다. "분석결과상세" 시트에도 상품그룹/매출액
열을 추가.

새 테스트 4개(`SettlementLoaderRevenueAndTrackingNoTests`, `ShipmentCountEstimatorTests` 3개),
190/190 통과.

## 마감/이익분석 미매핑 행 즉석 매핑 — 1:1 자동완성 + 우클릭 메뉴 4종 — 2026-06-28

사용자 요청: 정산파일을 불러온 뒤 보이는 미매핑 리스트에서, "매핑 SKU" 칸에 바로 타이핑해서
검색하듯 1:1 매핑을 끝내고, 우클릭으로 조건부/임시/예외처리 등도 그 자리에서 실행할 수 있게
해달라는 것. 구현 전 `AskUserQuestion`으로 두 가지를 확인: (1) 오타로 잘못된 1:1 규칙이 즉시
저장되는 걸 막는 안전장치 필요 여부 → **"자동완성 목록에 있는 코드와 완전히 일치해야만 확정"**
선택, (2) 우클릭 메뉴 구성 → **SKU 매핑 도우미 + 조건부 매핑 규칙 추가 + 임시 매핑으로 등록 +
이 행 예외처리, 4개 전부** 선택.

`Forms/SettlementForm.cs`(이익분석(자동) 탭, `_settlementGrid`)에 추가:
- **"매핑 SKU" 셀 인라인 자동완성(1:1 매핑)**: `EditingControlShowing`에서 마스터SKU 전체 +
  현재 행 채널의 CSKU 코드 목록을 `TextBox.AutoCompleteCustomSource`로 단다(`SuggestAppend`).
  `CellValidating`에서 입력값이 그 후보 목록과 완전히 일치하지 않으면(대소문자 무관)
  `e.Cancel = true`로 커밋을 막고 상태표시줄에 안내만 띄운다(빈 값은 매핑 해제 의도로 항상
  허용). 검증을 통과하면 `CellEndEdit`에서 `MappingRepository.UpsertExactRule`로 1:1 규칙을
  저장하고, `ReapplyMappingAndProfit()`으로 그 행만 즉시 재계산한다(파일을 다시 불러오지 않음).
- **우클릭 메뉴 4종**: `ExcelLikeDataGridView`의 기본 메뉴(복사/붙여넣기 + 동적 "이 창의 기능")
  앞에 `Items.Insert(0, ...)`로 끼워넣었다 — `OnContextMenuOpening`이 매번 `_pasteMenuItem` 이후
  항목만 지우고 다시 그리므로, paste보다 앞쪽에 넣은 항목은 절대 지워지지 않는다(중요한 구현
  포인트, 뒤에 추가했으면 메뉴를 열 때마다 사라졌을 것).
  - **SKU 매핑 도우미**: 기존 OFS용 `OrderSkuMappingDialog`를 합성 `OfsOrderItem`으로 재사용 —
    CSKU코드/납품가/송장표시명까지 한 번에 설정하거나 임시SKU를 새로 등록할 수 있는 더 풍부한 경로.
  - **조건부 매핑 규칙 추가**: `MappingForm`에 새 공개 메서드 `StartNewConditionRuleFor(channelCode,
    productName, optionName)` 추가 — 매핑관리창을 열고(또는 앞으로 가져오고) 채널을 맞춘 뒤,
    상품명/옵션명을 Contains 조건으로 채운 새 조건부 규칙을 만들어 "조건부 매핑(상세)" 탭으로
    이동한다. 조건/대상 SKU 마무리는 그 창에서 하고, 이 창에서는 "정산파일 로드"를 다시 실행하면
    반영된다(즉시 반영 아님 — 조건부 규칙은 정보가 더 필요해서 도우미 다이얼로그로 처리하지 않음).
  - **임시 매핑으로 등록**: 새 범용 `Forms/TextPromptDialog.cs`(SKU 입력받는 작은 다이얼로그)로
    대상 SKU/CSKU를 물어보고 `MappingRuleType.Temp`로 저장 + 즉시 재계산.
  - **이 행 예외처리(매핑 제외)**: 확인창 후 `MappingRuleType.Exception` +
    `SkuMapper.ExcludedTargetSku`로 저장(배송비/수수료 안내 행 등 실제 상품이 아닌 행용).
  - 1:1/임시/예외 모두 키는 `SkuMapper.ApplyMapping`과 동일한 `ProductName+OptionName`(구분자 없음)
    형식을 그대로 쓴다(`BuildExactMappingKey`).
- `DataLoaders/SettlementLoader.ApplyMappingAndProfit`을 `private` → `public static`으로 바꿔
  재사용했다(정산파일을 다시 읽지 않고 한 행만 다시 계산하는 유일한 방법이라 새 로직을 만들지
  않고 그대로 가져다 씀). 채널의 매핑 규칙이 바뀌면 `SkuMapper`를 새로 만들어야 한다(생성 시점에
  규칙을 한 번 로드해 캐싱하기 때문) — `ReapplyMappingAndProfit()`이 매번 새 `SkuMapper`를 만든다.

새 테스트: `SettlementLoaderApplyMappingAndProfitTests`(1개, public 재사용 경로 검증). 186/186 통과.

## 마감/이익분석 하단 요약 그리드에 합계 행 추가 — 2026-06-28

사용자 피드백: 상품그룹별 요약 하단에 합계를 라벨 텍스트(`_summaryTotalsLabel`)로만 보여주니
가시성이 떨어진다 — 그리드 안에 별도 행으로 보이고 굵게 표시해달라는 요청.
`RefreshProfitAnalysisView()`가 집계한 `groups` 리스트 끝에 `ProductGroup = "합계"`인
`ProfitGroupSummary` 행을 하나 더 추가해서 `_summaryGrid`에 바인딩하고,
`OnSummaryGridRowPrePaint`에서 그 행만 `FontStyle.Bold`로 그린다(라벨 텍스트는 그대로 유지 —
두 가지를 같이 보여줌). 엑셀 내보내기의 "분석요약(상품그룹별)" 시트에 이미 있던 합계 행과 라벨
문자열을 `TotalRowLabel` 상수로 통일했다.

## 광고매핑 분석결과 내보내기 — SalesManagerV2 ad_engine.py의 save_results 그대로 이식 — 2026-06-28

사용자가 "광고매핑/광고분석 결과 파일도 SalesManagerV2를 참고해서 그대로 이식해달라"고 요청.
`AdMappingForm`에는 매핑 규칙 관리 탭들만 있었고 결과를 파일로 내보내는 기능이 전혀 없었다
(SettlementForm의 "결과 저장"/"엑셀로 내보내기"에 대응하는 게 없었음).

SalesManagerV2의 실제 결과 파일(`results/.../*_광고분석_*.xlsx`)을 openpyxl로 직접 열어 확인하고,
`ad_engine.py`의 `save_results()` 함수에서 정확한 시트 구성을 확인했다(우연히 들어있던 "Sheet2"
피벗은 사용자가 엑셀에서 수동으로 추가한 것이라 코드가 만드는 진짜 출력은 2개 시트뿐이었음):
- **"광고매핑상세"**: 원본 파일의 모든 열 그대로 + 맨 앞에 "판매채널" 열 + 표준화 열
  (AD_PRODUCT_NAME/AD_PRODUCT_ID/AD_OPTION/AD_COST/AD_EXTRA1/AD_EXTRA2) + 매핑결과 3열
  (MAPPED_GROUP/MATCH_TYPE/MAPPING_STATUS, 후자는 매핑됐으면 "O" 안되면 "X").
- **"그룹별_광고비"**: 판매채널/MAPPED_GROUP/AD_COST — **매핑된(MAPPING_STATUS=='O') 행만** 그룹별
  합산(미매핑 비용은 집계에서 빠진다 — 레거시와 동일).

`AdMappingForm`의 "광고비 데이터" 탭에 "분석결과 내보내기" 버튼을 추가해 이 두 시트를 그대로
출력한다. 원본 열을 그대로 싣기 위해 `AdSpendItem.RawValues`(Dictionary&lt;string,string&gt;?)를
신설하고 `AdSpendLoader`가 각 행 로드 시 채우도록 했다(SettlementData.RawValues와 동일한 패턴).
저장 파일명도 레거시 패턴(`{채널명}_광고분석_{yyMM}.xlsx`)을 따른다.

기존 `AdSpendLoaderTests`에 RawValues 검증 추가(새 테스트 메서드는 아님), 185/185 통과.

## 노션 체크리스트 "알려진 미구현/제약사항" 재검증 — 2026-06-28

사용자가 체크리스트의 미구현/제약사항 목록을 실제 코드 기준으로 재확인해달라고 요청. grep/구조
확인으로 9개 항목을 하나씩 다시 검증한 결과 **7개는 이미 구현 완료**(CSV 처리/암호 엑셀/창 크기
기억/이중출고 방지/그리드 붙여넣기/MainHub 요약/운송장 동명이인+합포장·분리배송 메뉴 — 모두 이전
세션들에서 만들어졌는데 체크리스트가 갱신 안 된 상태였음), **2개는 여전히 미구현**(문서관리 PDF
출력 — 화면 자체가 없음, 이익분석 결과의 SalesManagerV2 실데이터 대비 회귀검증 — 단위테스트만
있고 실데이터 대조는 안 함). 노션 체크리스트의 해당 콜아웃을 ✅/❌로 재구성해 갱신함.

## 노션 체크리스트 99.우선개발(99.1/99.2) 구현 — 2026-06-28

노션 "기능 테스트 체크리스트"의 "99. 우선개발" 항목(99.1 마감/이익분석 창, 99.2 공통 UX)을 구현했다.

### 99.2 공통 UX (전체 그리드 공통 적용)

`Controls/ExcelLikeDataGridView.cs`에 두 가지를 추가했다 — 이 컨트롤을 쓰는 모든 창에 자동 적용됨:
- **헤더 클릭 정렬**: `Utils/GridSorter.cs` 신설. `DataGridView.Sort()`는 `BindingList<T>`처럼
  `IBindingList.SupportsSorting=false`인 데이터소스에서 예외를 던지므로, 데이터소스 종류
  (DataTable/DataView/BindingSource/일반 IList)별로 직접 들여다보고 정렬한다. 일반 List처럼
  변경통지가 없는 데이터소스는 정렬 후 DataSource를 다시 설정해 그리드를 갱신시킨다.
- **우클릭 메뉴에 "이 창의 기능" 자동 추가**: 메뉴가 열릴 때마다(`ContextMenuStrip.Opening`)
  그 그리드가 속한 Form을 재귀적으로 훑어 보이고 활성화된 모든 Button을 찾아 메뉴 항목으로
  추가한다(클릭하면 `button.PerformClick()`). 복사/붙여넣기 등 고정 항목은 그대로 유지. 파생
  폼이 나중에 `ContextMenuStrip`을 통째로 교체해도(virtual 프로퍼티를 오버라이드해 가로챔)
  동적 메뉴가 새 메뉴에도 따라붙는다.
- 또한 **남아있던 모든 plain `DataGridView` 인스턴스를 `ExcelLikeDataGridView`로 교체**했다
  (AdMappingForm/ChannelConfigForm/CourierConfigForm/DataManagementForm/MappingForm/
  OrderSkuMappingDialog/OfsForm/SettlementForm/ChannelSkuPriceHistoryForm/CostHistoryForm —
  필드 타입은 `DataGridView`로 그대로 두고 생성식만 바꿔 변경 범위를 최소화). 이제 앱 전체에서
  정렬+동적 버튼메뉴가 일관되게 동작한다.
- **다중선택**: `ExcelLikeDataGridView` 생성자가 기본값 `MultiSelect = true`라 위 교체만으로
  대부분 그리드가 다중선택 가능해졌다. 다만 아래 9곳은 "하나를 골라야 그 상세를 다른 패널에
  보여준다"는 선택→상세연동 패턴이라 다중선택이 의미가 없어 의도적으로 `MultiSelect = false`로
  남겨뒀다(체크리스트의 "다중선택을 안 해야 할 것 같은 기능은 안내" 항목에 따른 것):
  `MappingForm`의 미매핑목록/마스터DB후보/CSKU검색결과/조건부규칙목록, `AdMappingForm`의
  조건부규칙목록/임시규칙목록, `DataManagementForm`의 백업목록, `OrderSkuMappingDialog`의
  후보목록. (단, `MappingForm`의 "전체 규칙 관리" 탭은 그리드 MultiSelect 대신 체크박스 컬럼으로
  다중선택을 이미 구현해뒀던 것이라 그대로 둠.)

### 99.1 마감/이익분석 창(`Forms/SettlementForm.cs`)

- **미매핑건 상단노출 + 미매핑건만 보기 토글**: `RefreshProfitAnalysisView()` 신설. 기본은
  "미매핑건만 보기" 체크(`_unmappedOnlyCheckBox`, 기본 ON) — 체크 해제 시 전체를 보되 미매핑이
  항상 위로 정렬된다. `Utils/SettlementRowStatus.IsUnresolved`로 "확인 필요"(Msku 공란/매핑
  실패/매핑 키 없음/원가 정보 없음) 판정 — 매핑테이블 예외규칙으로 의도적으로 제외된
  "제외(배송비 등)" 행은 문제로 보지 않는다. 그리드는 `_settlementRows`의 필터된 *복사본*에
  바인딩하지만 같은 SettlementData 객체 참조를 공유하므로, 셀 편집은 그대로 원본에 반영되고
  "결과 저장"/"내보내기"는 항상 `_settlementRows` 전체(필터 무관)를 대상으로 한다.
- **이익분석(=정산파일 로드) 완료 시 미매핑 건이 있으면 안내창** 표시.
- **상품그룹별 요약**: 하단에 `PersistentSplitContainer`로 분리된 요약 그리드 신설(상품그룹은
  `Msku`→`ChannelSkuRepository.ResolveMasterSku`→`ItemRepository.GetBySku().ProductGroup`로
  역추적). 매출액/수량/순이익/배송비/입출고비 합계를 라벨로도 표시. **광고비/택배비는 현재
  파이프라인에 조인되어 있지 않아 합계에 포함되지 않음을 명시**해뒀다(AdMapping/택배비 데이터를
  이익분석에 조인하는 건 더 큰 별도 작업이라 이번 범위에서 제외 — 다음 우선작업 후보).
  "결과 저장" 성공 시에도 이 요약을 메시지박스로 한 번 더 보여준다.
- **엑셀 내보내기 4종 시트**(SalesManagerV2의 다중 시트 결과 양식을 참고): "분석결과상세"(기존과
  동일) / "분석요약(상품그룹별)"(합계 행 포함) / "미매핑·예외건"(확인 필요 + 제외규칙 적용 행) /
  "원본데이터"(정산 파일에서 읽은 헤더→값 원본 그대로, 표준필드로 매핑되지 않은 열도 포함).
  "원본데이터"를 만들기 위해 `SettlementData.RawValues`(Dictionary&lt;string,string&gt;?) 필드를
  신설하고 `SettlementLoader`가 각 행을 읽을 때 함께 채우도록 했다.
- 새 테스트: `GridSorterTests`(6개), `SettlementRowStatusTests`(5개),
  `SettlementLoaderRawValuesTests`(1개). 185/185 통과.

**다음 작업자가 알아야 할 것**: 라이브 DB(`bin/Debug/net10.0-windows/ERP_Database.sqlite`)에서
한때 마이그레이션됐던 채널 16개/매핑규칙 750+건/CSKU 405건이 사라져 있는 게 확인됨(마스터SKU
550건만 남음 — `bin` 폴더 재생성 추정). 원본은 `C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`에 온전히 남아있다 — 데이터관리창의 "레거시 가져오기"
탭에서 그 파일을 다시 불러오면 복구된다(Upsert 방식이라 재실행 안전). 사용자가 직접 실행해야
하는 동작이라 이 세션에서 대신 실행하지 않았다 — 다음 세션에서 라이브 DB 상태를 볼 때 이미
복구됐는지 먼저 확인할 것.

## auto-compact 이후 추가로 한 일

10. **조건부 매핑 다중조건 전용 편집 UI** 구현 완료 — 매핑관리창에 "조건부 매핑(상세)" 탭 신설.
    `Forms/MappingForm.cs`의 `CreateConditionDetailTabPage` 이하. 좌측에서 채널의 조건부 규칙
    목록(요약 Key/TargetSku)을 고르면 우측에 그 규칙의 상세조건(HeaderField/Operator/
    TargetValue/Logic) 그리드가 뜨고, 각 영역(규칙 정보 저장/상세조건 저장)이 즉시 DB에
    반영된다. 기존 단순 "조건부 매핑" 탭의 일괄 `SaveRules` 저장과는 완전히 분리되어 있어,
    이제 단순 탭에서 저장해도 이 탭에서 관리하는 다중조건 데이터가 삭제되지 않는다
    — **이전에 기록했던 데이터 손실 위험은 해소됨**. `MappingRepository`에
    `UpdateConditionRuleSummary`/`DeleteConditionRule`/`ReplaceConditionDetails` 추가.
11. 사용자가 실제 프로그램을 켜놓고 테스트하면서 발견한 3건 추가 처리:
    - `StdField.DeliveryMessage`(배송메세지) 필드 신설 — 발주서 매핑 탭, 택배사 양식의
      "매핑할 데이터" 목록에 추가했고 `OrderLoader`가 실제로 읽어 `OfsOrderItem.
      DeliveryMessage`에 채운다. `CourierExporter`는 속성명 리플렉션 방식이라 추가 수정 없이
      택배사 출력양식에서 바로 쓸 수 있다.
    - 채널설정 발주서/정산서 매핑 탭의 샘플 엑셀 헤더 미리보기: 더블클릭으로 "열"에 적용할 때
      미리보기에서 선택한 헤더 행 번호도 함께 "헤더 행" 칸에 반영하도록 수정(기존엔 헤더 행을
      2/3/4로 바꿔 헤더를 찾아도 매핑 설정 쪽 헤더 행이 1로 남아 수동 수정이 필요했음).
    - OFS 미매핑 리스트/매핑관리창 충돌 강조/마감 원가없음 강조 셀: 배경색만 지정하고
      글자색을 지정하지 않아 시스템 다크모드에서 흰 글자 + 연분홍/허니듀 배경이 겹쳐 안 보이는
      문제 수정 — 강조 배경에는 항상 `Color.Black` 글자색을 함께 지정하도록 변경
      (`Forms/OfsForm.cs`, `Forms/MappingForm.cs`, `Forms/SettlementForm.cs`).
12. **CSKU(채널별 SKU) 설계 확장 + 매핑 도우미 재타이핑 방지** — 사용자가 실제 사용하면서 "매핑의
    핵심 목적"을 다시 설명함: 발주서마다 상품명/옵션명 구조가 제각각인데, 택배사 출력양식은
    "상품명" 칸이 보통 1개뿐이라 송장에는 간결한 표시가 나가야 한다는 것. 이번에 처리한 것:
    - `OrderSkuMappingDialog`(SKU 매핑 도우미)에서 SKU를 선택/임시등록하면, 체크박스
      "다음에도 같은 상품명/옵션명은 자동으로 이 SKU로 매핑"(기본 체크)을 통해
      `MappingRepository.UpsertExactRule`로 1:1 규칙을 바로 저장한다. **이전에는 매핑 도우미로
      골라도 일회성이라(영구 규칙 미생성) 같은 상품이 다음 발주서에서 또 미매핑으로 떴는데,
      이제는 한 번 고르면 그 조합이 영구 1:1 규칙이 되어 재발하지 않는다.**
    - `ChannelSkuModel`(CSKU)에 `InvoiceDisplayName`(송장표시명, nullable) 필드 추가
      (`Database/DbSchema.cs`의 `EnsureColumn`으로 기존 DB도 마이그레이션됨). 마스터DB
      상품명과는 별도로, 채널/SKU별로 송장에 찍을 간결한 이름을 따로 관리한다.
    - `OrderSkuMappingDialog`에 "송장표시명(선택, 채널별)" 입력란 추가, SKU를 고르면 기존에
      저장된 CSKU(납품가/송장표시명)를 자동으로 불러와 미리 채워준다(이전엔 항상 빈칸).
    - `SkuMapper`가 매핑 성공 시 `OfsOrderItem.InvoiceLabel`을 계산해 채운다
      (`{CSKU.InvoiceDisplayName} {수량}개`, CSKU에 송장표시명이 없으면 null). 택배사 양식
      관리창(`Forms/CourierConfigForm.cs`)의 "매핑할 데이터" 후보에 `InvoiceLabel` 추가 —
      채널 운영자가 택배사 출력양식의 "품목" 헤더를 이 속성에 연결하면, 상품명+옵션명+수량을
      조합한 간결한 한 줄이 자동으로 나간다. CSKU에 송장표시명을 안 정해둔 SKU는 비어있으니,
      그 경우 헤더를 `ProductName`에 연결해 원본 그대로 쓰면 된다(기존 동작과 동일).
    - CSKU 납품가 변경 이력(`ChannelSkuPriceHistory`)은 이전 세션에 이미 구현되어 있었음
      (신규 발견, 별도 작업 불필요) — `ChannelSkuRepository.GetPriceHistory`로 조회 가능.
      송장표시명 변경 이력은 별도로 로깅하지 않는다(가격처럼 자주 바뀌지 않고, 변경 추적의
      실익이 적다고 판단 — 필요해지면 같은 패턴으로 추가 가능).
13. **매핑관리창에 "미매핑 처리" 탭 신설(상하 분리 레이아웃)** — 사용자가 "매핑창을 열어도 미매핑
    리스트가 아무것도 안 보인다"고 지적함. 실제로 매핑관리창은 그동안 저장된 매핑 *규칙*만
    관리했고, OFS에서 로드한 발주서의 실제 미매핑 *주문 항목*을 보여주는 화면이 없었다. 이번에
    `Forms/MappingForm.cs`에 첫 번째 탭으로 "미매핑 처리"를 추가:
    - 상단(`_unmappedGrid`): 현재 선택된 채널의 미매핑 항목(상품명/옵션명/수량/상태) 목록.
      `ShowUnmappedItems(channelCode, BindingList<OfsOrderItem> orders, Action? onMappingApplied)`로
      OFS의 `_orders`를 **참조로** 그대로 받기 때문에, 여기서 매핑을 적용하면 그 객체에 바로
      반영되고 `onMappingApplied` 콜백으로 OFS 그리드도 `Invalidate()`된다.
    - 하단: 마스터DB 검색(SKU **또는 상품명**, 입력하는 대로 실시간 필터) 결과 그리드, 그 아래
      "CSKU 매핑 이력" 그리드(선택한 SKU에 이미 매핑된 다른 상품명+옵션명 조합들을 그대로
      나열 — 사용자가 예로 든 "상품A+옵션B"와 "상품A+옵션C"가 같은 SKU인 경우 둘 다 따로 표시),
      납품단가(VAT포함/별도)·송장표시명 입력란.
    - 버튼 4개(+ 그리드 우클릭 컨텍스트 메뉴로 동일하게 제공, "실무에선 우클릭을 더 많이 쓴다"는
      요청 반영): **1:1 매핑 적용**(선택한 미매핑 항목 키를 `UpsertExactRule`로 영구 저장 +
      CSKU 정보 저장), **임시 SKU 등록 후 매핑**, **조건부 매핑 규칙 추가**(선택한 항목의
      상품명/옵션명을 그대로 포함하는 AND 조건 2개로 규칙을 만들고 "조건부 매핑(상세)" 탭으로
      이동해 다듬게 함), **예외 처리(매핑 제외)**(`UpsertRule(Exception, ...)`로 즉시 제외 규칙
      저장).
    - `MappingRepository.UpsertExactRule`을 일반화한 `UpsertRule(MappingRuleType, ...)`을 새로
      추가(기존 `UpsertExactRule`은 이걸 호출하는 얇은 래퍼로 유지, 기존 호출부/테스트 영향 없음).
    - `Forms/OfsForm.cs`: 발주서 로드 후 미매핑 자동 안내 시 호출하던
      `mappingForm.SelectChannelByCode(...)`를 `mappingForm.ShowUnmappedItems(...)`로 교체(채널만
      선택하던 것에서 실제 미매핑 목록까지 보여주도록). 또한 언제든 다시 열 수 있는 "미매핑 일괄
      처리" 버튼을 툴바에 추가. `SkuMapper` 생성 시 `_channelSkuRepository`를 함께 넘기도록 수정해
      OFS에서 로드한 주문에도 `InvoiceLabel`이 채워지게 함(이전엔 OFS 쪽 SkuMapper 생성 코드가
      이 매개변수를 안 넘기고 있었음 — 누락 수정).

### 분할배송/합포장 1단계 완료 — "묶음(송장 1건 단위)" 개념 도입

이전 세션엔 설계만 해두고 미구현이었으나, 이번 세션에서 1단계를 구현했다. 계획 문서는
`C:\Users\thebo\.claude\plans\tidy-rolling-hopcroft.md`에 남아있음(2단계 메모 포함).

- **핵심 발견(이후 아래 "사용자가 실제로 묶음 기능을 써보며 발견한 점"에서 정정됨)**: 기존
  `CourierExporter.ExportAsync`는 `OfsOrderItem` 1건 = 출력 1행이었다. 처음엔 "같은 주문번호는
  기본적으로 한 송장"으로 자동 합치게 만들었으나, **이후 사용자 피드백으로 이 기본 자동합포장은
  제거되었다** — 합포장은 택배사 프로그램이 다운스트림에서 자동으로 처리하므로 MiniERP2가 주문
  번호 등을 기준으로 임의로 합칠 필요가 없다는 설명이었음. 최종 동작은 아래 정정 절 참고.
- **`Models/OfsOrderItem.cs`**: `ShipmentGroupId`(nullable) 필드 추가. **기본값은 그룹화 없음**
  (줄마다 별도 송장). 사용자가 명시적으로 합포장을 지정해야만 값이 채워져 여러 줄이 합쳐진다.
- **`Utils/ShipmentGrouping.cs`**(신규): `GetEffectiveGroupId(OfsOrderItem)` — 실제 묶음 키를
  계산하는 순수 함수(우선순위: ShipmentGroupId(명시값) > 줄 단독 취급. **주문번호 기반 자동
  묶음은 없음** — 아래 정정 절 참고). `Tests/ShipmentGroupingTests.cs`로 검증.
- **`Forms/OfsForm.cs`**: 그리드에 읽기전용 "묶음" 표시열 추가(`OnOrdersGridCellFormatting`,
  몇 줄이 묶여있는지만 보여줌). 컨텍스트 메뉴(기존 복사/붙여넣기 메뉴에 추가, `SetupShipmentGroupingContextMenu`)에
  **합포장으로 묶기**(2줄 이상 선택, 수취인 다르면 확인) / **분리배송으로 분리**(1줄 이상,
  새 그룹ID 부여) / **묶음 해제** 3개 항목 추가. `OnOrdersGridCellValueChanged`에 운송장번호
  자동 전파 로직도 추가(한 줄에 입력하면 같은 묶음 다른 줄에도 복사 — 실제로 한 패키지엔
  운송장 1개이므로).
- **`Forms/CourierExporter.cs`**: `ExportAsync`가 `orders.GroupBy(ShipmentGrouping.
  GetEffectiveGroupId)`로 묶음 단위 1행 출력하도록 변경. "품목"으로 매핑된 속성
  (`ProductName`/`InvoiceLabel`)에 대해서만 묶음 내 모든 줄의 표시문자열을 줄바꿈으로 이어붙이고,
  그 외 속성(수취인/주소 등)은 묶음의 대표 줄(첫 줄) 값을 그대로 쓴다. **반환형이
  `Task<List<string>>`로 바뀜** — 품목이 4줄을 초과하는 묶음의 대표 주문번호 목록(내보내기는
  초과 여부와 무관하게 항상 끝까지 진행되고, 호출 측이 비차단 경고를 띄움). 호출부
  `Forms/OfsForm.cs`의 `OnExportClick`도 함께 수정해 경고 메시지를 띄우게 했다.
- **2단계(미구현, 메모만)**: 4줄 초과 묶음에서 운영자가 2개 이상 품목을 하나의 표시줄로 수동
  합치는 전용 편집 UI(예: "상품A 2개 / 상품B 1개"). 지금은 경고만 띄우고 줄바꿈으로 다 내보내는
  것으로 충분한지 실사용 피드백을 받은 뒤 진행하기로 함.
- 테스트 9건 추가(`ShipmentGroupingTests` 4건 + `CourierExporterTests` 5건: 같은 주문번호 합치기,
  다른 주문번호 같은 그룹ID로 합포장, 같은 주문번호 다른 그룹ID로 분리배송, 4줄 초과 경고,
  InvoiceLabel 우선 사용).

### 사용자가 실제로 묶음 기능을 써보며 발견한 점 + 추가 개선 3건

**"발주서 1건만 출력됨" 문의 → 당시엔 "묶음 기본 동작이라 의도된 것"이라고 답했으나, 이후 사용자가
재확인 후 그 기본 동작 자체가 잘못된 설계였다고 정정함**(바로 아래 절 참고). 별도 코드 수정 없이
넘어갔던 항목이라 여기 기록만 정확히 갱신해둔다.

이어서 요청받은 3가지 기능개선을 구현:

1. **부분 선택 내보내기** — `Forms/OfsForm.cs`의 `OnExportClick`. 그리드에서 줄을 선택해둔 상태면
   선택한 건만, 아무것도 선택하지 않았으면 매핑된 전체를 내보낸다(`GetSelectedOrderItems()` 재사용).
2. **택배사 출력 미리보기 패널** — OFS 화면을 위(상세 줄)/아래(미리보기) `SplitContainer`로 나눠
   하단에 신설(`CreateExportPreviewPanel`). 매핑 성공한 줄을
   `ShipmentGrouping.GetEffectiveGroupId`로 묶어 `ShipmentPreviewRow`(주문번호들/수취인/연락처/
   주소/배송메세지/품목(`ShipmentGrouping.BuildCombinedItemDescription`로 CourierExporter와 동일한
   조합 로직 공유)/총수량/운송장번호/줄수)를 한 행씩 보여준다. 배송메세지·운송장번호는 미리보기
   에서 바로 고치면 그 묶음의 모든 원본 줄에 반영된다(속성 setter가 처리). 우클릭으로 "합포장(묶음
   합치기)"/"묶음 해제"도 가능(분리배송은 줄 단위 선택이 필요해 상세 그리드에서 하도록 안내).
   품목 4줄 초과 묶음은 미리보기에서도 연분홍으로 강조. 발주서 로드/매핑 적용/그룹 조작 등 묶음에
   영향을 줄 수 있는 모든 지점에 `RefreshExportPreview()` 호출을 추가해 자동 갱신되게 했고, 수동
   "새로고침" 버튼도 함께 둠.
3. **발주확정 시 발주이력 기록 + 추적관리** — 기존 `OutboundDetailTable`(저장(출고확정) 시
   `OutboundRepository.SaveOutbound`가 채움, `SettlementForm`의 "마감 대조(수기)" 탭에서 조회)이
   이미 발주이력 역할을 하고 있었으나 상태 추적이 없었다. `OutboundDetail`에 `Status`("발송대기"/
   "발송완료")와 `ConfirmedAt`(nullable) 추가. 발주확정 시점에 운송장번호가 이미 있으면
   "발송완료"로, 없으면 "발송대기"로 시작(이미 발송완료였던 건을 재확정해도 뒤로 되돌아가지 않게
   SQL CASE로 보호). 마감 대조 탭에 버튼 2개 추가:
   - **선택건 발송확인 처리** — `OutboundRepository.MarkAsShipped(ids)`로 운송장번호 없이도 수동
     확정(매장 직접배송 등).
   - **운송장번호 업로드** — 택배사 등에서 받은 "주문번호/운송장번호" 2열 엑셀을 읽어
     `OutboundRepository.BulkUpdateTrackingNoByOrderNo`로 일괄 갱신 + 자동 발송완료 처리(주문번호가
     일치하지 않으면 조용히 건너뜀).
   - `Database/DbSchema.cs`에 `EnsureColumn`으로 기존 DB도 마이그레이션.
4. 테스트 5건 추가(`OutboundRepositoryTests`: 운송장번호 유무에 따른 초기 상태, 재확정 시 상태
   유지, MarkAsShipped, BulkUpdateTrackingNoByOrderNo 일부매칭). 103/103 통과.

### 정정: 묶음 기본 동작에서 "주문번호 자동합포장"을 제거함 (중요)

위 1단계에서 "같은 주문번호는 기본적으로 한 송장"을 기본 동작으로 만들었는데, 사용자가 테스트
발주서(3건의 발주, 그중 2건만 합포장 대상이었음)로 확인해보니 **3건이 1건으로 합쳐져 나왔다**고
지적했다. 사용자의 설명: **합포장은 택배사 프로그램이 다운스트림에서(수취인/주소 기준 등으로)
자동으로 처리하는 것이라, MiniERP2가 주문번호를 기준으로 임의로 합칠 필요가 없다.** 즉 발주가
3건이면 매핑 후 출력 양식에도 3건이 그대로 나와야 한다는 것 — 이게 맞는 설계였다.

- **`Utils/ShipmentGrouping.GetEffectiveGroupId`**: `OrderNo` 기반 폴백을 제거했다. 이제 기본값은
  "그룹화 없음"(줄마다 별도 송장)이고, `ShipmentGroupId`가 명시적으로 설정된 경우에만(=사용자가
  직접 합포장 조작을 한 경우) 합쳐진다. 이로써 "발주 N건 → 출력 N행"이 기본적으로 보장된다.
- `Tests/ShipmentGroupingTests.cs`/`Tests/CourierExporterTests.cs`의 "같은 주문번호면 자동으로
  합쳐진다"를 가정한 테스트들을 "명시적 그룹ID가 없으면 합쳐지지 않는다"로 수정.
- **OFS 그리드의 분리배송/합포장/묶음해제 컨텍스트 메뉴는 그대로 유지** — 자동으로 합치지 않을
  뿐, 운영자가 특정 상황(예: 같은 수취인에게 무게상 합쳐 보내고 싶은 경우)에 수동으로 합포장하는
  기능은 여전히 필요하다고 판단해 남겨둠.
- **미리보기 패널에 추가 요청 2건도 함께 처리**:
  - 컨텍스트 메뉴 라벨을 "묶음 해제(주문번호 단위로 되돌리기)" → "분리배송 처리(묶음 풀기)"로
    변경(자동합포장이 없어진 지금은 두 동작이 같은 의미이므로 사용자가 요청한 용어로 통일).
  - **"이 줄 복사(상품명 공란 — 송장에 표시할 메시지용)"** 신설(`OnDuplicatePreviewRowClick`).
    선택한 미리보기 묶음의 대표 줄을 복제해 `_orders`에 새 `OfsOrderItem`을 추가하는데, **상품명만
    공란으로** 둔다(나머지 필드는 그대로 복사). 택배사 양식에는 별도 메모란이 없는 경우가 많아서,
    운영자가 위 상세 그리드에서 이 새 줄의 "상품명" 칸에 자유 텍스트(CS 메시지, 안내문구 등)를
    입력하면 그게 같은 묶음의 품목란에 한 줄로 같이 출력되는 방식으로 활용한다. 복제 시 원본도
    명시적으로 같은 `ShipmentGroupId`를 갖도록 고정해, 이후에도 항상 같은 송장으로 묶이게 한다.
  - `OnOrdersGridCellValueChanged`가 이제 어떤 열이 바뀌든(상품명 포함) 끝에서 항상
    `RefreshExportPreview()`를 호출하도록 단순화(이전엔 MappedSku/TrackingNo 열만 개별 처리해서,
    상품명 칸에 메시지를 입력해도 미리보기가 즉시 갱신되지 않는 문제가 있었음).

### 미리보기 셀 직접 수정 + 합포장 품목 표시 형식 개선

1. **미리보기 그리드 셀 직접 수정 가능** — `ShipmentPreviewRow`의 `Recipient`/`Phone`/`Address`도
   (기존엔 `DeliveryMessage`/`TrackingNo`만 가능했음) setter를 추가해 그리드에서 바로 고치면 그
   묶음의 모든 원본 줄에 반영되게 했다(`ReadOnly = true` 제거). `ItemsDescription`(품목, 실제
   출력될 내용)도 직접 고칠 수 있게 했는데, 별도 오버라이드 저장소를 두지 않고 **첫 줄의
   `InvoiceLabel`을 입력값으로 덮어쓰고 나머지 줄들의 `InvoiceLabel`은 빈 문자열로 비우는 방식**
   으로 구현했다 — `BuildCombinedItemDescription`이 빈 줄은 걸러내므로 결합 결과가 입력한 값
   그대로 나가고, `CourierExporter`도 같은 `InvoiceLabel`을 읽으므로 실제 내보내기에도 그대로
   반영된다(미리보기 전용 상태를 따로 동기화할 필요가 없는 설계). 진짜 집계값(주문번호들/
   총수량/줄수)만 읽기전용으로 남겨둠.
2. **합포장 품목 표시 형식 변경** — 줄바꿈만으로는 송장에서 어디까지가 한 품목인지 헷갈린다는
   피드백을 반영해, `ShipmentGrouping.BuildCombinedItemDescription`이 줄이 2개 이상이면
   `"((A품목 2개))   +   ((B품목 3개))"`처럼 괄호로 묶고 `   +   `로 구분하도록 바꿨다(줄이
   1개면 괄호 없이 그대로). 4줄 초과 경고 판단은 더 이상 `\n` 개수로 셀 수 없어서
   `ShipmentGrouping.CountDescriptionLines`(합치기 전 줄 목록 개수)를 새로 추가해
   `CourierExporter`와 미리보기의 강조 표시 양쪽에서 같이 쓰게 했다.
3. 테스트 3건 추가(`BuildCombinedItemDescription` 단일/복수 줄 형식, `CountDescriptionLines`),
   기존 `CourierExporterTests` 2건의 기대값을 새 형식으로 수정. 106/106 통과.

### 분할선 크기 기억 + 미리보기 실행취소(5건)

1. **분할선(SplitContainer) 위치 기억** — OFS(상단 발주서/하단 미리보기)와 매핑관리창 "미매핑
   처리" 탭(상단 미매핑목록/하단 영역, 그리고 하단 안의 마스터DB 후보/CSKU 검색결과 좌우분할)의
   분할선을 조절해도 다음에 창을 열 때 기본값으로 돌아가던 것을 고쳤다. 그리드 컬럼폭을 기억하는
   기존 `ExcelLikeDataGridView`/`GridSettingsService` 패턴과 똑같은 구조로:
   - `Config/SplitterSettingsService.cs`(신규) — 키별 `SplitterDistance`(int)를
     `splitter_layouts.json`에 저장/조회. `Tests/SplitterSettingsServiceTests.cs`로 검증.
   - `Controls/PersistentSplitContainer.cs`(신규, `SplitContainer` 상속) — `PersistenceKey`를
     지정하면 저장된 분할 위치를 불러오고, `SplitterMoved`마다 자동 저장한다. 컨트롤이 아직
     부모에 붙기 전(크기 0)이라 바로 적용할 수 없는 경우를 위해 `OnSizeChanged`에서 크기가
     잡힐 때까지 재시도하도록 처리(`ArgumentOutOfRangeException` 방어).
   - `Forms/OfsForm.cs`의 `gridSplit`, `Forms/MappingForm.cs`의 `split`/`candidatesSplit`을
     일반 `SplitContainer`에서 `PersistentSplitContainer`로 교체하고 각각
     `PersistenceKey`("OfsForm.GridSplit"/"MappingForm.UnmappedSplit"/
     "MappingForm.CandidatesSplit")를 부여.
2. **미리보기 실행취소(최근 5건)** — `Forms/OfsForm.cs`에 `_previewUndoStack`(최대 5개) 추가.
   미리보기 그리드의 셀 편집 시작 시(`CellBeginEdit`, 값이 바뀌기 전에 잡아야 해서 `CellValueChanged`
   대신 사용) 및 합포장/분리배송 처리/줄복사 액션 직전에 `PushPreviewUndoSnapshot()`으로 그 시점의
   `_orders` 전체를 복제해 쌓아둔다(5개 초과 시 가장 오래된 것을 버림). 미리보기 패널 툴바의
   "실행취소" 버튼(+ 우클릭 메뉴에도 동일 항목) 클릭 시 가장 최근 스냅샷으로 `_orders`를 되돌리고
   미리보기/상세 그리드를 새로고침한다.

### 발주확정/출고확정 용어 정정 + 택배사 양식 전체헤더 출력 + 검색창 자동입력 제거

사용자가 실제로 써보며 4가지를 지적함:

1. **(이미 구현돼 있던 기능 재확인, 코드 변경 없음)** 부분선택 내보내기(`OnExportClick`의
   `GetSelectedOrderItems()` 분기)와 발주이력 Status/ConfirmedAt 추적은 이전 세션에 이미 구현되어
   있었다 — 그대로 유지.
2. **발주확정 vs 출고확정 용어 정정(중요)** — 실무에서 "출고완료"는 운송장번호가 있어야 성립하는데,
   `Forms/OfsForm.cs`의 저장 버튼("저장 (출고 확정)")을 누르면 운송장번호 유무와 무관하게 무조건
   `item.Status = "출고 완료"`였다. 이를 **저장 단계 = "발주확정"**, **운송장번호가 있을 때(또는
   나중에 등록될 때) = "출고확정"**으로 분리했다:
   - 버튼 텍스트 "저장 (출고 확정)" → "저장 (발주확정)".
   - `UpdateOrderStatusAfterSave`: `order.Status = string.IsNullOrWhiteSpace(order.TrackingNo) ?
     "발주확정" : "출고확정"`(저장 시점에 운송장번호가 이미 있으면 즉시 출고확정).
   - `OnOrdersGridRowPrePaint`의 녹색 강조 조건에 `"발주확정"`/`"출고확정"` 둘 다 포함.
   - **`Models/OutboundDetail.Status`/`Database/OutboundRepository.cs`의 값도 "발송대기"/
     "발송완료"에서 "발주확정"/"출고확정"으로 통일**(OFS 버튼과 같은 용어를 쓰도록). 마감 대조
     탭(`SettlementForm`)의 "운송장번호 업로드"가 이미 하던 동작(주문번호 매칭 시 Status를
     출고확정으로 바꾸는 것)은 그대로 유지, 문구만 갱신.
   - `Tests/OutboundRepositoryTests.cs`의 모든 기대값을 새 용어로 수정. 참고: 이미 저장된 과거
     DB의 Status 값("발송대기"/"발송완료")은 마이그레이션하지 않았다 — 새로 저장되는 건부터 새
     용어가 적용되고, 과거 값은 문자열이 다를 뿐 화면 표시 외 로직(트래킹 유무 기반 CASE문 등)에는
     영향이 없다.
3. **택배사 양식 — 매핑 안 한 헤더도 전체 출력** — 샘플 파일에 a,b,c,d,e 헤더가 있고 그중 a,b,d만
   "매핑할 데이터"를 지정해도, 출력 파일에는 a,b,c,d,e 헤더가 전부 있어야 한다(매핑 안 한 c,e는
   데이터만 빈 칸) — 그래야 택배사 프로그램에 그 파일 그대로 올릴 수 있기 때문. `Forms/
   CourierConfigForm.cs`의 `OnSaveClick`이 저장 시 `PropertyName`이 빈 행을 통째로 걸러내고 있던
   게 원인(샘플 불러오기 자체는 이미 전체 헤더를 행으로 추가해주고 있었음, `OnLoadSampleClick`).
   `Header`만 있으면 행을 유지하도록 필터를 고쳐 `HeaderMappingJson`에 빈 매핑(`PropertyName=""`)
   도 함께 저장되게 했다 — `CourierExporter`는 `GetProperty("")`가 null을 반환해 자연스럽게
   빈 칸으로 출력하므로 별도 수정이 필요 없었다.
4. **매핑관리창 SKU 검색창 자동입력 제거** — `Forms/MappingForm.cs`의
   `OnUnmappedRowSelectionChanged`가 미매핑 항목을 선택할 때마다 검색창에 그 줄의 상품명을 채워
   넣고 있어서, 매번 지우고 다시 입력해야 하는 불편이 있었다. 이 자동입력은 애초에 "CSKU 송장표시명
   영역에 상품명을 옮겨쓰고 싶다"는 요청에서 나온 것인데, 그건 이미 별도 기능
   (`UseCurrentCellAsInvoiceDisplayName`, 우클릭 "CSKU 상품명으로 사용")으로 구현돼 있어서 검색창
   자동입력은 더 이상 필요 없었다. 제거하고 `RefreshInvoicePreview()`만 남김(검색창은 항상 빈칸
   유지, 창을 열 때 자동 포커스되는 기존 동작은 그대로).

테스트 기존 5건의 기대값만 수정(신규 테스트는 없음 — 용어 변경/필터 조건/이벤트 핸들러 단순화라
새 동작 케이스는 기존 테스트가 이미 커버). 110/110 통과.

### 분할선 위치가 재시작해도 기억 안 되던 버그 수정 (`PersistentSplitContainer`)

사용자가 직접 써보고 분할선(OFS 상세/미리보기 등) 크기를 조절해도 창을 닫고 다시 열면 원래대로
돌아간다고 지적함. 원인은 타이밍 버그였다:

- 이전 구현은 `PersistenceKey` 설정 시 또는 `OnSizeChanged`가 호출될 때마다 즉시
  `SplitterDistance`를 적용하려 했다. 그런데 `SplitContainer`는 `FixedPanel.None`(기본값)일 때
  **컨테이너 크기가 바뀔 때마다 두 패널의 비율을 유지하려고 SplitterDistance를 내부적으로
  재계산**한다. `InitializeComponent` 도중에는 아직 컨트롤이 부모에 완전히 도킹되어 최종 크기에
  정착하기 전이라, 그 시점에 값을 적용해도 이후 폼이 실제 크기로 자리잡는 과정에서 한 번 더
  resize가 일어나며 적용한 값이 어긋나 버렸다(저장은 됐지만 다시 불러올 때 엉뚱한 비율로
  반영됨).
- 해결: `Controls/PersistentSplitContainer.cs`를 WinForms 커뮤니티에서 흔히 쓰는 패턴으로
  재작성 — `HandleCreated` 이벤트 시점에 `BeginInvoke`로 적용을 메시지 루프 한 틱 뒤로 미룬다
  (그 시점엔 레이아웃이 이미 정착해 있음). 그래도 범위를 벗어나 실패하면 최대 10회까지 다시
  `BeginInvoke`로 재시도(무한루프 방지용 횟수 제한).
- `Forms/OfsForm.cs`의 `gridSplit` 기본값도 420 → 250으로 낮춰, 분할선을 한 번도 안 건드린
  첫 실행 상태에서 상세 목록이 화면을 너무 많이 차지해 미리보기가 잘 안 보이던 문제를 줄였다
  (한 번 조절하면 그 값이 정상적으로 기억된다).
- 자동 테스트로 검증하기 어려운 WinForms 레이아웃 타이밍 문제라 수동 확인이 필요함 — 사용자가
  OFS/매핑관리창에서 분할선을 조절한 뒤 창을 완전히 닫고 다시 열어 위치가 유지되는지 직접
  확인해주길 권장.

## 발주/출고 이력 관리창 신설 (`Forms/OutboundHistoryForm.cs`)

OFS 화면에 "발주/출고 이력" 버튼을 새로 추가해 여는 전용 창. 발주확정/출고확정 이력
(`OutboundDetail`)을 채널/기간으로 조회하고, 택배사 프로그램에서 받은 운송장 결과 엑셀을
불러와 자동으로 매칭·반영하는 기능을 담는다. 기존 `SettlementForm`의 "마감 대조(수기)" 탭에
있던 "선택건 발송확인 처리"/"운송장번호 업로드" 버튼은 이 창으로 완전히 대체되어 제거했다
(매칭 방식이 OrderNo 기반 → 수령인 기반으로 바뀌어 두 군데에 다른 로직을 두면 혼란스러움).

- **수령인 기준 매칭**: 운송장 결과 파일에는 전체주소/품명 등이 불분명하게 나오는 경우가 많아,
  사용자 요청대로 매칭은 **수령인 이름만으로** 한다. 동일 수령인의 발주확정 건이 여러 개면
  `Forms/TrackingMatchPickerDialog.cs`(신규)로 후보 목록(주문번호/수령인/주소/품목명/발주확정
  시점)을 보여주고 사용자가 직접 골라야 적용된다(자동 추정 안 함). 적용되면 운송장번호가
  채워지고 실제 택배사 이동 여부와 무관하게 즉시 "출고확정"으로 바뀐다.
- **운송장 결과 파일의 헤더 설정**: `Models/CourierMaster.cs`에 `TrackingImportHeaderRow`/
  `TrackingImportRecipientHeader`/`TrackingImportTrackingNoHeader`를 추가해, 택배사 출력 양식
  설정과 같은 방식(택배사 양식 관리 창 → "운송장 결과 가져오기 양식" 영역)으로 헤더 시작행과
  수령인/운송장번호 열 헤더명을 지정할 수 있게 했다. 출력 양식과는 별개의 파일 형식이라
  `HeaderMappingJson`과 분리해서 둔다.
- **이력 매칭에 필요한 정보 보강**: `OutboundDetail`에 `Recipient`/`Address`/`ProductName`을
  추가하고, `OfsForm.OnSaveClick`이 저장 시점에 함께 채운다(이전엔 이 정보가 없어서 수령인
  매칭/동명이인 구분이 불가능했다).
- **직접 편집/삭제**: 이력 그리드에서 수량/납품가/운송장번호/상태(콤보)를 바로 수정하면
  `CellEndEdit`에서 즉시 `OutboundRepository.UpdateDetail`로 저장된다(상태를 출고확정으로 직접
  바꾸면 확정일시가 없으면 현재 시각으로 채움). 여러 줄을 다중 선택해 "선택 삭제"하면 확인창을
  거쳐 `DeleteByIds`로 삭제한다(되돌릴 수 없음을 안내).
- `OutboundRepository`: `BulkUpdateTrackingNoByOrderNo`(OrderNo 기반, 구식)는 제거하고
  `ApplyTrackingNo`(단일 건, 수령인 매칭 후 적용용)/`UpdateDetail`/`DeleteByIds`/`GetHistory`
  (채널 null이면 전체 채널)를 추가했다.

테스트: `Tests/OutboundRepositoryTests.cs`에 Recipient/Address/ProductName 저장, ApplyTrackingNo,
UpdateDetail, DeleteByIds, GetHistory(전체 채널) 케이스 추가, 구식 BulkUpdate 테스트는 제거.
114/114 통과. UI 매칭/디스앰비규에이션 플로우는 자동 테스트로 검증하기 어려워 수동 확인 필요 —
실제 운송장 결과 샘플 파일로 택배사 양식 설정 → 발주/출고 이력 관리창에서 불러오기 → 동명이인
선택창 동작까지 사용자가 직접 확인해주길 권장.

### 발주/출고 이력에서 임의선택 출력 + 택배사 양식 헤더 순서 보존 + ComboBoxCell 오류 수정

위 발주/출고 이력 관리창을 실사용해보고 나온 후속 피드백 3건 + 추가요청 1건:

1. **이력에서 임의선택해 택배사 양식 출력**: 발주확정만 해두고 OFS에서 택배사 양식 출력을
   빠뜨린 건을 나중에 처리할 방법이 없었다. `OutboundHistoryForm`에 "선택 건 택배사 양식 출력"
   버튼을 추가해, 그리드에서 다중선택한 `OutboundDetail`로 `OfsOrderItem`을 구성해 기존
   `CourierExporter`로 그대로 출력한다. 단, `OutboundDetail`에는 연락처/배송메세지/CSKU
   송장표시명이 저장되어 있지 않아(OFS 그리드 전용 데이터) 그 항목은 빈 칸으로 나간다 — 통상
   택배사 양식에 필수인 수령인/주소/운송장번호/품목/수량은 정상 출력된다.
2. **택배사 양식 출력 순서가 샘플과 달랐던 버그**: `CourierMaster.HeaderMappingJson`을
   `Dictionary<string,string>`(헤더→속성)으로 저장하고 있었는데, JSON 객체(딕셔너리)의 키 순서는
   사양상 보장되지 않아 저장/불러오기를 거치며 샘플에서 불러온 순서와 달라질 수 있었다. 택배사
   프로그램은 그 파일의 열 순서로 데이터를 인식하므로 이건 실사용에 치명적인 버그였다.
   `Utils/CourierHeaderMapping.cs`(신규)를 만들어 순서가 보장되는 **JSON 배열**
   (`List<HeaderMappingEntry(Header, PropertyName)>`)로 저장 형식을 바꿨고,
   `CourierConfigForm`/`CourierExporter` 둘 다 이걸 사용하도록 변경했다. 기존에 Dictionary
   형식으로 저장된 구버전 데이터도 읽을 수 있게 폴백을 둬서(`CourierHeaderMapping.Parse`가 배열
   파싱 실패 시 Dictionary로 재시도) 마이그레이션 없이 그대로 동작하고, 다음에 저장하면 새
   형식으로 정규화된다. 회귀 테스트로 `CourierExporterTests.ExportAsync_PreservesSampleHeaderOrderExactly`
   추가(헤더 5개를 일부러 섞은 순서로 줘서 출력 파일의 열 순서가 그 순서를 정확히 따르는지 확인).
3. **발주/출고 이력 날짜 조회 시 DataGridViewComboBoxCell 오류**: 원인은 발주확정/출고확정
   용어 변경 전("발송대기"/"발송완료")에 저장된 옛 상태값이 DB에 남아있는데, 이력 그리드의 상태
   콤보 열(Items가 "발주확정"/"출고확정" 두 값뿐)이 그 값을 표시하려다 던지는 WinForms의 표준
   예외였다. 두 겹으로 고쳤다: (a) `Database/DbSchema.cs`에 기동 시마다 실행되는 정규화
   쿼리(`NormalizeLegacyOutboundStatus`)를 추가해 옛 값을 새 용어로 한 번에 바꾸고, (b)
   `OutboundHistoryForm`이 조회한 데이터에 실제로 등장하는 값을 콤보 Items에 보강해두고
   (`EnsureStatusItemsInclude`, `CourierConfigForm`의 기존 패턴과 동일), `DataGridView.DataError`를
   구독해 혹시 모를 다른 값에도 창이 죽지 않게 방어했다. 회귀 테스트
   `DbSchemaMigrationTests.EnsureCreated_OnLegacyOutboundStatus_NormalizesToCurrentTerminology` 추가.

테스트 116/116 통과.

### 엑셀 내보내기 "파일을 내보내는 중 오류가 발생했습니다" — 원인은 파일을 엑셀에서 이미 열어둔 상태

발주/출고 이력 관리창에서 "선택 건 택배사 양식 출력"을 했을 때 오류가 났다고 보고됨. 확인해보니
저장하려던 엑셀 파일을 이미 엑셀 프로그램에서 열어둔 상태였다 — Windows 파일 공유 위반
(ERROR_SHARING_VIOLATION)으로 다른 모든 엑셀 저장 코드(`CourierExporter`, `OfsForm`,
`SettlementForm`, `CSkuForm`, `MasterSkuForm`)에서도 똑같이 날 수 있는 문제였는데, 지금까지는
`ex.Message`(영문 IO 예외 원문)를 그대로 보여줘서 사용자가 원인을 알기 어려웠다.

`Utils/ExportHelper.cs`에 `DescribeSaveError(Exception ex)`를 추가했다 — 예외 체인(InnerException
포함)에서 Win32 HResult가 공유 위반(0x80070020)/잠금 위반(0x80070021)인 `IOException`을 찾으면
"파일이 이미 다른 프로그램(엑셀 등)에서 열려 있어 저장할 수 없습니다. 파일을 닫고 다시
시도하세요."로 바꿔 보여주고, 그 외 예외는 원래 메시지를 그대로 보여준다. 위 5개 파일의 엑셀
저장 관련 catch 블록을 모두 이 헬퍼를 쓰도록 바꿨다. 테스트
`Tests/ExportHelperTests.cs`(HResult를 리플렉션으로 주입해 시뮬레이션) 추가. 119/119 통과.

### 택배사 양식 관리 — 샘플 불러오기 시 헤더 순서를 항상 샘플과 일치시킴

저장 형식을 순서 보장 JSON 배열로 바꾼 이전 수정(위 항목 2)만으로는 부족했다 — 그건 "저장된
순서를 그대로 다시 읽는다"만 보장했을 뿐, `CourierConfigForm`의 "샘플 양식 불러오기" 자체가
그리드에 **이미 있는 헤더 행의 순서는 그대로 두고 새 헤더만 끝에 추가**하는 방식이어서, 기존
행 순서가 샘플과 어긋나 있으면(예: 옛 Dictionary 형식 데이터를 다시 연 경우, 또는 사용자가
드롭다운으로 헤더를 순서 없이 골라둔 경우) 다시 샘플을 불러와도 순서가 안 맞을 수 있었다.

`OnLoadSampleClick`을 "그리드 행 순서를 매번 샘플 헤더 순서와 정확히 일치시키는" 방식으로
다시 짰다 — 기존에 지정해둔 헤더→속성 매핑은 헤더 이름으로 그대로 이어받고, 행 순서만 새로
불러온 샘플 순서로 재배열한다. 샘플에 없는 기존 헤더(수동으로 추가했던 행 등)는 잃지 않도록
샘플 헤더들 뒤에 그대로 이어붙인다. 이제 "샘플 양식 불러오기"를 누르면 항상 그 즉시 그리드가
샘플과 같은 순서가 되고, 저장하면(JSON 배열 형식) 그 순서가 그대로 보존되어 출력된다.

### "매핑된 SKU" 헤더가 CSKU 코드를 그대로 출력하던 버그 + 발주 중복 이력 안내

두 가지 버그 신고 처리:

1. **"매핑된 SKU" 출력 버그**: 택배사 출력 양식에서 헤더를 "매핑된 SKU"로 매핑하면
   `CourierExporter`가 그 헤더에 내부 CSKU 코드(`OfsOrderItem.MappedSku`, 예: `"NAV_상품A"`)를
   그대로 출력하고 있었다 — 실제 송장에는 코드가 아니라 그 CSKU에 설정된 송장표시명(상품명)이
   나가야 한다. `CourierExporter.ExportAsync`가 `propertyName == "MappedSku"`인 헤더를 만나면
   `ChannelSkuRepository.GetByChannelAndCskuCode`로 그 CSKU의 `InvoiceDisplayName`을 조회해
   출력하도록 바꿨다(같은 CSKU가 여러 줄에 나와도 DB 조회가 한 번만 일어나게 그룹 처리 동안
   캐싱). 송장표시명이 설정되어 있지 않은 CSKU는 빈 칸 대신 코드 그대로 출력해 무엇이 매핑 안
   됐는지는 알아볼 수 있게 했다. `CourierConfigForm`의 "매핑된 SKU" 드롭다운 라벨에도 이 동작을
   설명을 덧붙였다. `CourierExporter`가 이제 `ChannelSkuRepository`에 의존하므로 생성자에서
   선택적으로 주입 가능하게 했다(기본값 `new()`).
2. **발주서 중복 처리 안내**: 같은 발주서 파일을 실수로 다시 불러오거나, 의도적으로 같은 곳에
   두 번 출고하는 경우를 구분할 방법이 없었다. "동일 주문"의 판단 기준은 `OutboundDetailTable`의
   충돌 판단 키(UNIQUE INDEX)와 같은 **주문번호(OrderNo, 채널 무관)**로 정했다 — 처리 자체를
   막으면 의도적인 재출고를 못 하게 되므로, 정책상 **항상 안내만 하고 발주 처리는 그대로
   진행**한다(사용자 요구사항). `OutboundRepository.FindByOrderNos`(신규)로 불러온 발주서의
   주문번호들 중 기존 발주확정/출고확정 이력이 있는 건을 찾고, `OfsForm.OnLoadOrdersClick`에서
   로드 완료 직후(미매핑 안내보다 먼저) `WarnIfOrdersAlreadyHaveHistory`가 발견 시
   "{N}건의 주문번호가 {M월 D일 H시경} 발주건과 동일한 이력이 있습니다(발주확정 X건, 출고확정
   Y건). 동일한 곳으로 두 번 출고하는 경우일 수 있어 발주 처리는 그대로 진행됩니다." 형태의
   안내창을 띄운다.

테스트: `CourierExporterTests`에 InvoiceDisplayName 출력/미설정 시 코드 폴백 케이스,
`OutboundRepositoryTests`에 `FindByOrderNos` 케이스 추가. 123/123 통과.

### 미매핑 목록 기본 높이 + 운송장 동명이인 선택창에서 합포장 다중 적용

1. **미매핑 목록 상단 기본 높이**: `MappingForm`의 "미매핑 처리" 탭 상단(미매핑 목록) 분할선이
   조절해도 고정이 안 되는 것처럼 느껴진다는 피드백 — 이미 `PersistentSplitContainer`(이전
   타이밍 버그 수정 적용됨)로 조절한 값은 정상적으로 기억되지만, 기본값(220px)이 너무 작아 몇 줄
   만에 잘려 보였다. 기본값을 270px(약 10줄)로 늘렸다 — 사용자가 한 번 조절하면 그 값이 여전히
   기억된다(`MappingForm.UnmappedSplit` 키).
2. **운송장 동명이인 선택창에서 합포장(여러 건에 같은 운송장번호) 처리**: 발주/출고 이력
   관리창의 "운송장번호 불러오기"에서 동일 수령인이 여럿이면 뜨는
   `TrackingMatchPickerDialog`가 이제까지는 1건만 고를 수 있었다. 택배사에서 여러 주문을
   합포장해 한 운송장으로 같이 보낸 경우 같은 수령인의 여러 건에 같은 운송장번호를 한 번에
   넣어야 하는데 그게 불가능했다. 그리드를 다중선택(`MultiSelect = true`)으로 바꾸고, "선택 건에
   적용"을 누르면 선택된 모든 건에 같은 운송장번호를 적용하도록 `OutboundHistoryForm.ImportTrackingFile`을
   고쳤다(`TrackingMatchPickerDialog.Selected` 단일 속성 → `SelectedItems` 목록으로 변경). 1건만
   선택하면 이전처럼 그 건에만 개별로 적용된다 — 선택은 항상 사용자가 직접 한다.

### 누적발주서 채널 — 발주일 기준 최근 5일 이내 항목만 골라 선택 처리

일부 판매채널은 발주서 파일이 그때그때의 신규 주문만 담는 게 아니라, 과거 이력까지 누적해서
계속 쌓인 채로 내려온다(채널 자체 특성). 이런 채널에서 발주서를 그대로 불러오면 이미 처리된
옛 주문까지 매번 다시 섞여 들어와 헷갈린다. 전체 채널에 적용할 필요는 없으므로(대부분은
"이번에 새로 들어온 주문만" 담긴 파일) 채널별로 켜고 끌 수 있는 옵션으로 만들었다.

- `Models/ChannelConfig.cs`에 `IsCumulativeOrderFile`(bool) 추가 — "기본 정보" 탭 PropertyGrid에
  체크박스로 자동 표시된다("누적발주서").
- `Models/StdField.cs`에 `OrderDate` 추가, `Forms/ChannelConfigForm.cs`의 "발주서 매핑" 탭
  필드 목록에 포함시켜 기존 매핑 UI(시트/헤더행/엑셀 헤더/고정값)를 그대로 재사용 — 새 UI를
  따로 만들 필요가 없었다.
- `DataLoaders/OrderLoader.cs`가 발주일을 `OfsOrderItem.OrderDate`(DateTime?)로 읽는다. 엑셀
  날짜서식 셀(EPPlus가 내부적으로 일련번호로 들고 있을 수 있음)과 텍스트로 입력된 날짜
  ("2026-06-20" 등) 둘 다 처리한다(`GetDateValue`, `GetValue<DateTime?>` 우선 시도 후 텍스트
  파싱 폴백).
- `Forms/OfsForm.cs`의 `OnLoadOrdersClick`: 채널이 누적발주서로 체크되어 있고 발주일 헤더가
  매핑되어 있으면(둘 다 충족해야 함 — 매핑이 없으면 평소처럼 전체를 그대로 추가), 불러온 항목을
  발주일 기준 오늘로부터 5일 이내(발주일이 비어있는 행은 걸러내지 않고 항상 포함— 매핑 오류로
  데이터가 누락되는 것보다는 나음)로 추려서 새 선택창
  `Forms/CumulativeOrderSelectionDialog.cs`(신규)를 띄운다. 사용자가 거기서 다중선택(또는 "전체
  선택")한 건만 실제로 OFS 그리드에 추가되고, 선택하지 않은 건은 이번에 처리되지 않는다(취소하면
  아무것도 추가되지 않음).

테스트: `Tests/OrderLoaderTests.cs`(신규) — 날짜서식 셀/텍스트 날짜 셀 파싱, 매핑 안 했을 때
null로 남는지 검증. 126/126 통과.

### 발주/출고 이력창 — 셀 편집을 "변경사항 저장" 눌러야 반영 + 복사가 행 전체를 긁어가던 버그

1. **편집 즉시저장 → 저장 버튼 방식으로 변경(실수 방지)**: 그리드 셀(수량/납품가/운송장번호/상태)을
   고치면 그 즉시 DB에 쓰이던 것을, 변경된 항목만 메모리에서 추적(`_dirtyDetails`, 참조 동일성
   기준)했다가 새로 추가한 "변경사항 저장" 버튼을 눌러야 한꺼번에 `OutboundRepository.UpdateDetail`이
   호출되도록 바꿨다. 저장하지 않은 변경사항이 있는 채로 "조회"를 다시 누르거나 창을 닫으려 하면
   확인창이 뜬다(`OnFormClosing`/`OnLoadClick` 가드). 운송장번호 불러오기(`ImportTrackingFile`)는
   이미 그 자체로 명시적 확인을 거치는 별도 동작이라 즉시저장 방식을 그대로 유지했다(셀 직접
   편집만 대상).
2. **오른클릭 복사가 클릭한 셀이 아니라 행 전체를 복사하던 버그**: 그리드의 `SelectionMode`가
   `FullRowSelect`였던 게 원인 — 이 모드에서는 셀을 하나만 클릭해도 그 행 전체가 "선택"된 것으로
   취급돼, `ExcelLikeDataGridView`의 복사가 선택된 모든 셀(=행 전체)을 클립보드에 담았다.
   `RowHeaderSelect`로 바꿔 셀을 클릭하면 그 셀만 선택되게 했다(행 머리글을 클릭하면 행 전체
   선택은 그대로 가능). 다만 "선택 삭제"/"선택 건 택배사 양식 출력"은 기존처럼 행의 어느 셀을
   클릭해도 그 줄이 대상에 포함되는 게 자연스러워, 새 헬퍼 `GetSelectedDetails()`가
   `SelectedRows`(행 머리글로 선택)와 `SelectedCells`가 가리키는 행(셀 클릭으로 선택) 둘 다를
   합쳐서 판단하도록 했다 — 복사만 영향을 받고 삭제/출력 사용 흐름은 그대로다.

UI 동작이라 자동 테스트로 검증하기 어려움(기존 코드도 같은 사정) — 직접 셀 수정 후 저장 버튼
없이 닫을 때 확인창이 뜨는지, 셀 하나만 복사했을 때 그 값만 붙여넣어지는지 확인 권장.

### 송장표시명 수량 표기 형식을 택배사별로 커스터마이즈 + 합포장 시 "xx" 강조 표시

"품목명 수량개"처럼 일렬로만 나오면 합포장(한 묶음에 여러 품목)인지 한눈에 알아보기 어렵다는
피드백. 택배사 양식 관리 창에 "수량 표기 형식" 텍스트박스를 신설해, "##"을 실제 수량으로
치환하는 커스텀 형식을 택배사별로 지정할 수 있게 했다(예: "   ▶[##개]" + 수량 2 →
"   ▶[2개]"). 합포장(한 묶음에 품목이 2건 이상)이면 작업자가 다중건 포장임을 바로 알아챌 수
있도록, 그 형식 앞뒤에 "xx" 두 글자가 자동으로 붙는다(예: "A상품xx   ▶[2개]xx").

- **데이터 흐름 재구성이 필요했던 이유**: 기존엔 `SkuMapper.BuildInvoiceLabel`이 매핑 시점에
  "CSKU 송장표시명 + 수량" 문자열을 미리 만들어 `OfsOrderItem.InvoiceLabel`에 박아 넣었다. 그런데
  수량 표기 형식은 **택배사별** 설정이고, 매핑은 어느 택배사로 내보낼지 정해지기 전에 일어나므로
  그 시점엔 형식을 적용할 수 없다. `OfsOrderItem.InvoiceDisplayName`(신규, 수량 미포함 순수
  표시명)을 추가해 매핑 시점엔 이것만 채우고, 수량 표기는 실제 내보내기 시점(택배사가 정해진 뒤)
  에 `Utils/ShipmentGrouping.cs`가 조합하도록 옮겼다. `InvoiceLabel`은 이제 "사용자가 직접
  지정한 수동 오버라이드"만을 의미한다(OFS 미리보기 셀 직접편집/"CSKU 상품명으로 사용"/행 복사 —
  기존 기능 그대로 동작).
- `Models/CourierMaster.cs`에 `QuantityNotationFormat` 추가, `CourierConfigForm`에 입력란 추가.
- `ShipmentGrouping.BuildCombinedItemDescription`/`GetDescriptionLines`가 `quantityFormat` 선택
  인자를 받는다. `CourierExporter`가 선택된 택배사의 형식을 전달하고, OFS 미리보기처럼 특정
  택배사가 아직 정해지지 않은 화면은 기본 형식(" ##개", 기존 동작과 동일)을 그대로 쓴다 —
  미리보기와 실제 출력 형식이 정확히 같을 필요는 없다는 설계 판단(애초에 미리보기는 어느
  택배사로 낼지 모르는 상태에서 보여주는 화면이라 같을 수가 없음).
- `CourierExporter`의 "MappedSku" 헤더 처리(CSKU 코드 대신 송장표시명 출력, 직전 라운드에서
  추가한 기능)도 이제 `item.InvoiceDisplayName`이 이미 채워져 있으면 그걸 그대로 쓰고, 비어있을
  때만(발주/출고 이력에서 재출력하는 경우) DB를 조회하도록 최적화했다.

테스트: `SkuMapperTests`(InvoiceDisplayName으로 이름 변경, 수량 미포함 검증),
`ShipmentGroupingTests`(커스텀 형식/xx 래핑/InvoiceDisplayName 우선순위 케이스 추가),
`CourierExporterTests`(택배사별 커스텀 형식 적용 + 기존 xx 래핑 반영). 130/130 통과.

## 데이터 관리창에 "레거시 가져오기" 탭 신설 — 3가지 이관 진입점 통합

사용자가 실제 라이브 DB를 확인해보니, 이전 세션에서 마이그레이션됐다고 기록된 채널 16개/매핑규칙
750+건/CSKU 405건이 **현재 DB 파일에는 없는 상태**였음(`bin` 폴더가 한 번 재생성되며 DB 파일이
새로 만들어진 것으로 추정 — 원본 소스(구버전 C# MiniERP V3 DB)는 그대로 보존되어 있어 데이터
손실은 아님). 이 과정에서 사용자가 "데이터 관리창에 폴더 불러오기가 없다"고 지적 — 확인해보니
데이터관리창의 "엑셀 불러오기"는 파일(xlsx) 전용이고, 레거시 이관 3종(구버전 sqlite/SalesManagerV2
채널설정/광고매핑)은 각각 MainHub·채널설정창·광고매핑창에 따로따로 있어 한 곳에 모여있지 않았다.

`Forms/DataManagementForm.cs`에 **"레거시 가져오기" 탭**을 신설해 3가지를 한 곳에 모았다(기존
MainHub/채널설정창/광고매핑창의 버튼은 그대로 둠 — 단순히 진입점을 하나 더 추가):

1. **구버전 MiniERP(V3) 데이터 가져오기**: sqlite 파일 선택 → `LegacyMigrationService.Migrate` →
   이 창의 마스터SKU/CSKU/매핑규칙 4종 탭을 즉시 다시 불러와 결과가 바로 보이게 함(다른 진입점들과
   달리 이 창이 관리하는 테이블과 직접 겹쳐서, 가져오자마자 그 자리에서 확인 가능).
2. **SalesManagerV2 채널 설정 가져오기**: config 폴더 선택 → `SalesChannelLegacyMigrationService.Migrate`.
3. **SalesManagerV2 광고매핑 가져오기**: 채널 선택(이 탭에 채널 콤보 추가) + config 폴더 선택 →
   `AdLegacyMigrationService.Migrate`.

기존 서비스 코드는 전혀 건드리지 않고 호출부만 추가한 것이라 새 비즈니스 로직 없음(기존 테스트로
이미 커버됨). `MainHub.OnLegacyImportClick`의 안내문 중 "조건부 매핑 규칙은 매핑관리창에 전용
편집 UI가 아직 없다"는 문구도 정리(그 사이 "조건부 매핑(상세)" 탭이 만들어져 더 이상 사실이 아님).

173/173 통과(신규 테스트 없음 — UI 배선만 추가, 모든 마이그레이션 서비스는 이미 별도로 테스트됨).

## 채널설정(정산서 매핑) 레거시 이관 — SalesManagerV2 channels_config.json

사용자가 "그 폴더의 채널설정값을 MiniERP2에 맞게 변형해 자동이식할 수 있는지" 질문. 분석 결과
`config/channels_config.json`(레거시의 채널별 정산 헤더 매핑/환율/채널유형/쿠팡그로스 보조소스)이
MiniERP2의 `ChannelConfig.SettlementFieldMappings`/`ExchangeRate`/`ChannelType`/
`GrowthAuxSources`와 **거의 1:1로 대응**됨을 확인(특히 `GrowthAuxSource` 모델 필드가
레거시 `growth_aux_sources` 항목과 이름까지 거의 동일 — MiniERP2가 원래 이 레거시 시스템을
참고해 만들어졌기 때문). 레거시는 "정산서"만 다루는 도구라(발주서/택배 출력 개념이 없음)
이관 대상은 항상 정산서 매핑이고, 수취인/연락처 등 발주서 전용 필드는 이관되지 않는다.

`Database/SalesChannelLegacyMigrationService.cs`(신규) + 채널설정창에 "SalesManagerV2 채널
가져오기" 버튼을 추가했다. config 폴더를 고르면:

- 채널명이 **정확히 일치하는 기존 채널**은 설정(매핑/환율/유형/보조소스)을 덮어쓰고, 없는
  채널명은 `ChannelCodeGenerator`로 새 코드("CH004" 등)를 부여해 새로 만든다(레거시의 임의
  8자리 코드를 그대로 쓰지 않음 — 기존 MiniERP2 코드 체계를 따름).
- 채널유형 매핑: `COUPANG_GROWTH→CoupangGrowth`, `COUPANG_NORMAL→CoupangGeneral`,
  `COUPANG_ROCKET→CoupangRocket`, `ETC_11ST→ElevenStreet`, `AMAZON_US→AmazonUs`,
  `AMAZON_JP→AmazonJp`, `GENERAL→General`(레거시 12개 채널에서 실제 쓰인 7개 유형 모두 1:1
  매칭 확인됨).
- 표준필드 매핑: `STD_PRODUCT_NAME/STD_OPTION/STD_QTY/STD_SETTLEMENT/STD_SHIPPING/STD_FEE`
  → `ProductName/OptionName/Quantity/SettlementAmount/ShippingFee/HandlingFee`(헤더 행은
  레거시의 `global_header_row`를 그대로 사용). `STD_TAX_DATE/STD_TAX_NO/STD_PRODUCT_ID/
  STD_DATE/STD_ORDER_ID/STD_EXTRA1/STD_EXTRA2`는 MiniERP2 정산서 매핑에 대응 슬롯이 없어
  이관되지 않음(정산 SKU매칭/이익계산에 쓰이지 않는 필드라 손실 없음).
- **이관 한계 1건**: 레거시는 일부 필드(실제 데이터 기준 150여 개 필드-채널 조합 중 2건)에
  "조건부 값 추출"(예: 옵션ID에 '배송' 포함 시 다른 열 값 사용)을 지원하는데, MiniERP2의
  `FieldMapping`은 이 기능이 없어 해당 항목은 건너뛰고 결과 안내에 모아 보여준다(채널설정창
  에서 직접 확인 필요).

**실제 이관은 사용자가 채널설정창에서 직접 실행해야 함** — 라이브 DB에 채널을 새로 만들거나
기존 채널 설정을 덮어쓰는 동작이라 이 세션에서 자동으로 실행하지 않았다.

테스트: `SalesChannelLegacyMigrationServiceTests`(신규 채널 생성, 기존 채널 갱신, 조건부 필드
건너뛰기+안내, 보조소스 이관, 파일 없음 케이스) 4건 추가. 173/173 통과.

## 광고 매핑 신설 — SalesManagerV2 광고매핑(ad_*.py) 인터페이스 포팅 + 레거시 JSON 이관

매핑 UI 패턴 포팅(위 1·2단계)에 이어, 사용자가 같은 레거시 폴더의 광고매핑 파일들
(`ad_engine.py`/`ad_manager.py`/`ad_popups.py` + `config/ad_*.json`)을 분석해 MiniERP2에
**새 기능으로** 들여와달라고 요청. 일반 매핑과 다른 점: 마스터SKU/CSKU가 아니라 **상품그룹
단위**로만 분류하면 되고(1:1 매핑 단계 없음), 채널마다 광고 리포트 헤더 구성이 제각각이라
"실제 파일로 테스트하며 설정할 예정"이라는 전제로 인터페이스 골격을 만드는 것이 목표.

레거시 우선순위 확인(`ad_engine.py` `apply_mapping`): **예외(행 제외) > 임시(정확 키 일치) >
조건부** — 일반 매핑의 "예외>1:1>임시>조건부"에서 1:1 단계만 빠진 것과 동일. MainHub에
"광고 매핑" 버튼 신설, `Forms/AdMappingForm.cs`가 채널 콤보 + 5개 탭으로 구성:

1. **광고비 데이터**: 광고비 엑셀을 불러와 매핑 결과(상품그룹/매핑타입/상태) + 합계 광고비를
   보여준다. 우클릭으로 선택 행을 임시매핑/조건부매핑/예외처리로 즉시 등록 가능.
2. **임시 매핑**: Key(상품명_옵션_상품번호) → 대상 상품그룹 단순 그리드.
3. **조건부 매핑(상세)**: 일반 매핑과 동일한 구조(규칙 목록 + 다중 AND/OR 상세조건 편집기) +
   **실시간 매칭예상 미리보기**(1단계에서 만든 패턴을 그대로 재사용, 현재 불러온 광고비 데이터
   기준). 연산자는 일반 매핑보다 많다(`AdConditionOperator`: Contains/NotContains/Equals/
   NotEquals/GreaterThan/LessThan/GreaterOrEqual/LessOrEqual/IsZero) — 레거시가 광고비 수치
   비교/0원 제외까지 지원했기 때문.
4. **예외 처리**: 합계/소계 등 계산 제외 행을 "헤더=값" 필터로 지정(레거시 ad_exception_rules.json
   과 동일한 형태 — 일반 매핑의 상품 단위 예외처리와 달리 행 단위 필터).
5. **필드 매핑**: 채널별로 광고비 파일의 어느 시트/헤더행/열에 각 표준 항목이 있는지 지정
   (`ChannelConfig.AdFieldMappings`, 발주서/정산서 매핑과 같은 `FieldMapping` 모델 재사용).
   레거시의 "여러 헤더명/여러 시트 fallback(sources)" 기능은 1단계 범위에서 제외 — 채널마다
   직접 테스트하며 설정하는 것을 전제로 단순화.

**새 DB 테이블**(`AdRuleTemp`/`AdRuleCondition`/`AdRuleConditionDetail`/`AdRuleException`) +
`Database/AdMappingRepository.cs`, 새 매핑 엔진 `Mapping/AdMappingEngine.cs` +
`Mapping/AdConditionEvaluator.cs`(일반 매핑의 ConditionEvaluator와 같은 AND/OR 결합 규칙,
비교 대상만 AdSpendItem/AdStdField), 새 로더 `DataLoaders/AdSpendLoader.cs`(OrderLoader와 같은
구조, 광고비 숫자 전처리 — 쉼표/통화표기/음수괄호 제거 — 포함).

### 레거시 JSON 데이터 이관 — `Database/AdLegacyMigrationService.cs`(신규)

광고 매핑창에 "SalesManagerV2 데이터 가져오기" 버튼을 추가해, 폴더 선택창으로 레거시
`config/` 폴더를 고르면 `ad_condition_rules.json`/`ad_exception_rules.json`을 현재 선택된
채널로 이관하고, `ad_channels_config.json`의 채널별 헤더 매핑은 **이름이 정확히 일치하는
기존 채널에만** "필드 매핑" 탭 설정으로 채워준다(없는 채널명은 자동 생성하지 않고 결과
안내에만 남김 — 채널 목록을 임의로 늘리지 않기 위한 안전장치).

- **헤더 번역의 한계**: 레거시 조건부 규칙(`ad_condition_rules.json`)의 조건은 원본 헤더
  텍스트(예: "광고집행 상품명")를 그대로 들고 있어 표준 필드(AdStdField)로 바로 매칭되지
  않는다. `ad_channels_config.json`의 모든 채널 헤더 매핑을 모아 "원본 헤더 → AdStdField"
  역방향 사전을 만들어 최대한 번역하고, 사전에 없는 헤더는 ProductName으로 잠정 배정한 뒤
  결과 안내에 모아 보여준다(조건부 매핑(상세) 탭에서 드롭다운만 바로잡으면 됨 — 완전 자동화는
  레거시 헤더가 표준화되어 있지 않아 애초에 불가능).
- 예외 규칙(`ad_exception_rules.json`)의 헤더는 이미 표준 필드명(`AD_PRODUCT_ID` 등)이라
  애매함 없이 직접 매칭된다.
- 레거시 조건부/예외 규칙 파일엔 채널코드가 없는 **전체 공용 규칙**이라, 이관 시 사용자가
  고른 단일 채널로 전부 가져온다(다른 채널에도 필요하면 추후 데이터 관리창 등으로 복제).

**실제 이관은 사용자가 직접 광고매핑창에서 실행해야 함** — 라이브 DB에 실제 채널/규칙
데이터를 추가하는 동작이라 이 세션에서 자동으로 실행하지 않았다(폴더 경로:
`C:\00_CompanyWorks\회사폴더\02_견적,거래명세_보냄\온라인 매출내역\SalesManager_V2\config`).

테스트: `AdConditionEvaluatorTests`/`AdMappingRepositoryTests`/`AdMappingEngineTests`
(우선순위: 예외>임시>조건부 검증 포함)/`AdSpendLoaderTests`/`AdLegacyMigrationServiceTests`
(실제 레거시 JSON 형태를 픽스처로 사용, 헤더 번역 성공/실패 양쪥 경로, 채널명 불일치 케이스
포함) 신규 22건 추가. 169/169 통과. WinForms UI(`AdMappingForm`)는 자동 테스트가 어려워
수동 확인 필요 — 실제 광고비 파일로 "필드 매핑" 탭 설정 후 불러오기, 조건부 매핑 미리보기,
레거시 이관까지 사용자가 직접 확인해주길 권장.

## SalesManagerV2(레거시 Python) 매핑 UI 패턴 도입 — 2단계: 전체 규칙 관리 탭

1단계(실시간 미리보기/셀클릭 자동주입)에 이어, 매핑관리창에 "전체 규칙 관리" 탭을 신설했다
(`MappingForm.CreateUnifiedRulesTabPage`). 레거시 `MappingRulesManagerDialog`를 본떠 예외/1:1/
임시/조건부 4종 규칙을 한 그리드에 모으고, 채널 콤보 + 검색 항목 콤보(전체/타입/채널/키/대상
SKU/상세) + 검색어로 필터링한다. **MiniERP2만의 보강 지점**: 각 행의 대상 SKU(=CSKU 코드)로
`ChannelSkuRepository`를 조회해 CSKU 송장표시명/납품가를 같은 행에 JOIN해서 보여준다(레거시는
CSKU 개념이 없어 타겟 SKU만 있었음).

- 체크박스 다중선택 + "선택 삭제" — 현재 채널에 발주서가 로드되어 있으면(`_sourceOrders`) 삭제될
  규칙으로 매칭될 건수를 추정해 "약 N건이 미매핑 상태로 되돌아갈 수 있습니다" 경고를 덧붙인다
  (레거시의 `count`/`match_count` 기반 경고와 같은 취지 — 컬럼을 추가하지 않고 현재 로드된
  데이터로 즉석 계산하는 방식으로 단순화).
- 더블클릭/우클릭 "수정하기"로 해당 규칙의 원래 탭(1:1/임시/예외 그리드 또는 조건부 매핑(상세))
  으로 이동해 그 행을 선택해준다. 단, 그 규칙이 현재 상단에서 선택된 채널과 다르면(채널 전환이
  비동기로 일어나 그 직후 선택을 시도하면 어긋날 수 있어) 채널을 직접 바꿔달라고 안내만 한다.
- 조건부 매핑의 상세조건은 "Condition" 한 열에 `AND ProductName Contains "셔츠" ; OR ...` 형식
  텍스트로 직렬화해 보여준다(데이터 관리창의 `ConditionalMappingManagedTable`과 같은 표기 방식 —
  단, 이 탭은 읽기 전용 요약이라 재파싱하지 않음).
- `MappingRepository.GetAllRules`/`GetAllConditionRulesWithDetails`/`DeleteRule`(데이터
  관리창 작업에서 이미 추가됐던 채널 무관 전체조회/삭제 메서드)을 그대로 재사용 — 새 DB
  메서드 추가가 거의 필요 없었다.

WinForms UI라 자동 테스트는 어려움 — 직접 여러 채널의 규칙을 등록한 뒤 "전체 규칙 관리" 탭에서
채널 필터/검색이 잘 되는지, 체크박스 삭제 후 영향 건수 경고가 뜨는지, 더블클릭으로 원래 탭에
잘 이동하는지 확인 권장. 147/147 기존 테스트 회귀 없이 통과(이 탭 자체는 신규 비즈니스 로직
없이 기존 검증된 메서드를 조합만 함).

## SalesManagerV2(레거시 Python) 매핑 UI 패턴 도입 — 1단계: 실시간 미리보기 + 셀 클릭 자동주입

사용자가 로컬에 보관 중인 레거시 SalesManagerV2(`mapping_manager.py`/`mapping_popups.py`/
`mapping_engine.py`)를 분석해 MiniERP2 매핑 UI에 가져올 점을 찾아달라고 요청. 매칭 우선순위
(예외 > 1:1 > 임시 > 조건부)는 이미 `SkuMapper`와 동일해 데이터/엔진 쪽은 옮길 게 없었고,
**인터페이스 패턴 3가지**가 MiniERP2에 없는 가치 있는 차이로 확인됨:
1. 조건부 매핑의 실시간 "N건 매칭예상" 미리보기
2. 그리드 셀 클릭 → 그 컬럼/값을 조건으로 즉시 자동주입(팝업이 열려있으면 누적 추가)
3. 4종 규칙(예외/1:1/임시/조건부)을 한 테이블에 모아보는 통합 규칙 관리자(검색/필터/일괄삭제)

사용자가 1→2→3 순서로 순차 진행을 확정. 이번 라운드는 **1, 2번**을 구현(3번 통합 관리자는
다음 라운드).

- **실시간 매칭 예상 건수** (`MappingForm.UpdateConditionPreview`): "조건부 매핑(상세)" 탭에
  파란색 굵은 글씨 레이블을 추가, 조건 추가/삭제/값수정마다 `ConditionEvaluator.Matches`를
  현재 채널의 `_sourceOrders`(OFS에서 넘겨받은 것과 같은 인스턴스)에 즉시 적용해 "N건 매칭 /
  전체 M건"을 보여준다. 발주서가 로드 안 된 상태(매핑관리창을 OFS 거치지 않고 단독으로 연
  경우)는 "발주서를 불러와야 미리볼 수 있습니다"로 우아하게 안내. 콤보 열(비교할 항목/조건/
  논리)은 `CurrentCellDirtyStateChanged`에서 즉시 커밋시켜 포커스 이동 없이도 바로 갱신되게 함.
- **셀 클릭 자동주입**: 미매핑 그리드 컨텍스트 메뉴에 "이 셀 값을 조건부 규칙에 조건으로 추가"
  신설. 우클릭한 셀의 열(상품명/옵션명/수량만 지원, 그 외 열은 안내 메시지)과 값을 그대로
  조건(Contains, AND)으로 만든다 — "조건부 매핑(상세)" 탭에 이미 편집 중인 규칙이 열려있으면
  그 규칙에 조건을 누적 추가(아직 저장 전, '상세조건 저장'을 눌러야 반영 — 레거시의 "팝업이
  열려있으면 조건 추가" 동작과 동일한 효과), 편집 중인 규칙이 없으면 이 조건 하나로 새 규칙을
  만들어 그 탭으로 이동한다.

WinForms UI 로직이라 자동 테스트로 검증하기 어려움(미리보기가 쓰는 `ConditionEvaluator`는 이미
별도로 테스트됨) — 직접 미매핑 탭에서 상품명/옵션명/수량 셀을 우클릭해 조건이 잘 추가되는지,
조건부 매핑(상세) 탭에서 조건을 고칠 때마다 미리보기 숫자가 즉시 바뀌는지 확인 권장. 147/147
기존 테스트는 회귀 없이 통과.

## 데이터 관리창 신설 — 마스터SKU/CSKU/매핑규칙 통합 백업·엑셀 가져오기·내보내기

MainHub에 "데이터 관리" 버튼을 추가해 여는 새 창(`Forms/DataManagementForm.cs`). 마스터SKU,
CSKU, 매핑규칙(1:1/임시/예외/조건부) 6개 탭 + "백업/롤백" 탭 + "내보내기 로그" 탭으로 구성된다.
이번 1단계 범위는 사용자가 직접 확정: **마스터SKU + CSKU + 매핑 4종만** 포함하고(채널/택배사
양식은 이미 전용 설정 창이 있어 제외), **백업/롤백 단위는 테이블별이 아니라 DB 파일 전체
스냅샷**(SQLite가 단일 파일이라 이게 훨씬 단순하고 트랜잭션적으로 안전함)로 정했다.

### 아키텍처 — `DataManagement/` 폴더(신규)

- **`IManagedDataTable`**: 한 종류의 DB 테이블을 `System.Data.DataTable`로 노출하는 어댑터
  인터페이스(`LoadCurrent`/`Insert`/`Update`/`Delete` + 중복판단용 `KeyColumns`). 화면은
  `DataTable`/`DataGridView`만 알면 되고, 실제 SQL은 어댑터 구현체(`MasterSkuManagedTable`,
  `CskuManagedTable`, `SimpleMappingManagedTable`(1:1/임시/예외 공용, 생성자에 RuleType 전달),
  `ConditionalMappingManagedTable`)에 위임한다.
- **`System.Data.DataTable`을 스테이징 매체로 채택한 이유**: `DataRow.RowState`(Added/Modified/
  Deleted/Unchanged)가 "불러오고 바로 반영 안 됨 → 변경내역 저장을 눌러야 반영" 요구사항을
  공짜로 구현해준다. `DataGridView`도 `DataTable` 바인딩을 기본 지원해 직접 추가/삭제(행 추가,
  Delete 키)까지 자연스럽게 된다.
- **`ManagedTableChangeApplier.Apply`**: `table.GetChanges()`를 순회하며 RowState별로
  어댑터의 Insert/Update/Delete를 호출한다. 자연키(KeyColumns) 값 자체가 수정된 행(이름변경)은
  Update만으로 매칭이 안 되므로(매칭 대상이 사라짐), 옛 키로 Delete + 새 키로 Insert 두 단계로
  자동 분리한다.
- **`ConditionalMappingManagedTable`**: 한 규칙이 다중 상세조건(AND/OR)을 가질 수 있어, 엑셀
  한 행 = 한 규칙으로 맞추려고 상세조건 목록을 `"AND ProductName Contains "셔츠" ; OR ..."`
  형식의 텍스트로 직렬화해 "Condition" 한 열에 담는다(가져오기 시 역직렬화). 자연키는
  (ChannelCode, Key) — DB에 유니크 제약은 없지만 이 창에서는 Key를 규칙 구분용 고유 설명으로
  취급한다고 문서화해둠.
- **`ManagedTableExcelIO`**: `DataTable` → 선택된 열만(+선택적 1열=값 필터) 엑셀로 내보내기,
  엑셀 → 헤더/행 텍스트 딕셔너리로 읽기, 셀 텍스트 → 컬럼 타입 변환(`ConvertValue`, 숫자 파싱
  실패 시 DBNull로 안전 처리).

### 가져오기 흐름(중복 안내 + 스테이징)

`DataManagementForm.OnImportClick`: 엑셀을 읽고 각 행의 자연키로 `DataTable.Rows.Find`를 호출해
기존 행과 매칭 — 매칭되면 "중복", 안 되면 "신규". 중복이 있으면
`"엑셀 N건 중 M건이 이미 존재합니다. 예: 덮어쓰기 / 아니오: 신규만 추가 / 취소"`로 확인하고,
신규 행은 항상 추가, 중복 행은 선택에 따라 덮어쓰거나 그대로 둔다. 이 시점에는 `DataTable`만
바뀌고(Added/Modified로 표시) DB에는 전혀 쓰지 않으며, "변경내역 저장"을 눌러야
`ManagedTableChangeApplier`가 실제로 반영한다. 저장 직전에 항상 `DbBackupService.CreateBackup`
(테이블명 태그)을 호출한다.

### 백업/롤백 — `Database/DbBackupService.cs`(신규)

DB 파일(`ERP_Database.sqlite`) 전체를 타임스탬프 이름으로 `backups/` 폴더에 복사하고, 보관
개수를 초과하면 가장 오래된 것부터 삭제해 항상 최신 3개만 남긴다. 복원은 단순 파일 교체
(`SqliteConnection.ClearAllPools()`로 커넥션을 비운 뒤 덮어쓰기)이며, 복원 후에는 이미 열린
화면들의 메모리 상태가 옛 DB 기준이라 **앱 재시작이 필수**임을 안내하고 `Application.Exit()`을
호출한다. "백업/롤백" 탭에서 수동 백업 + 목록에서 골라 복원할 수 있다.

### 내보내기 로그 — `ExportLogTable`(신규) / `Database/ExportLogRepository.cs`

엑셀 내보내기마다 시각/테이블명/파일경로/건수/내보낸 헤더 목록을 기록한다("내보내기 로그" 탭에서
조회 가능).

### 알아둘 제약/추후 과제

- 매핑규칙 단순 3종(1:1/임시/예외)과 조건부 매핑 모두 **자연키는 (ChannelCode, Key)** — DB에
  유니크 제약이 없으므로 같은 채널에 같은 Key가 이미 중복으로 들어있는 기존 데이터가 있으면
  매칭이 첫 번째 일치 행만 잡는다(드문 경우로 가정).
  `MappingRepository`에 `GetAllRules(ruleType)`(채널 무관 전체 조회)/`DeleteRule`/
  `GetAllConditionRulesWithDetails`를 새로 추가했다(기존 `SaveRules`는 채널 단위 전체교체라
  이 기능엔 위험해서 쓰지 않고, 기존에 있던 `UpsertRule`/`AddConditionRuleWithDetails` 등 행
  단위 메서드를 그대로 활용).
- 채널/택배사양식/발주출고이력 등은 이번 범위에서 제외(전용 설정 창이 이미 있음) — 필요해지면
  같은 `IManagedDataTable` 패턴으로 어댑터만 추가하면 됨.

테스트: `DbBackupServiceTests`, `ExportLogRepositoryTests`, `ManagedDataTableTests`(4개 어댑터의
Insert/Update/Delete/키변경 라우팅, 조건부 매핑 직렬화 라운드트립), `ManagedTableExcelIOTests`
(선택열+필터 내보내기, 읽기, 타입 변환) 신규 추가. 147/147 통과. WinForms UI
(`DataManagementForm` 자체)는 자동 테스트가 어려워 수동 확인 필요 — 실제로 마스터SKU 탭에서
가져오기/중복 확인창/변경내역 저장/백업 탭에서 복원까지 한 번씩 직접 확인 권장.

## CSKU 코드 신설 — 매핑 규칙의 TargetSku가 CSKU 코드로 바뀜 (중요, 전체 영향)

사용자가 "채널 안에서 같은 마스터SKU도 옵션별로 CSKU를 구분해야 한다"고 요청해, CSKU(채널별 SKU)에
전용 코드를 도입했다. **이건 단순 UI 추가가 아니라 데이터 모델 변경**이라 영향 범위를 정리해둔다.

- **무엇이 바뀌었나**: `ChannelSkuModel`에 `CskuCode`(고유키, 채널+CskuCode가 PK)가 새로 생기고
  기존 `Msku` 필드는 "이 CSKU가 원가 조회를 위해 연결되는 실제 마스터SKU"라는 의미로 남았다.
  매핑 규칙(`MappingRule.TargetSku`)과 `OfsOrderItem.MappedSku`가 실제로 담는 값은 이제
  **CSKU 코드**다(예전에는 마스터SKU를 그대로 담았음). 기본 CSKU 코드는
  `Utils/CskuCodeGenerator.BuildDefault`로 "채널명 앞 3글자_마스터SKU"(예: `AAA_BBB`) 형태로
  자동 제안되지만, `OrderSkuMappingDialog`/`MappingForm`의 "미매핑 처리" 탭에서 직접 편집할 수
  있다(같은 마스터SKU라도 옵션1/2/3마다 다른 코드를 줄 수 있음).
- **기존 CSKU와 코드가 같으면**: 새로 만들지 않고 "기존 CSKU 존재. 이 조합을 매핑 조건으로
  추가합니다" 안내만 띄우고, 그 CSKU의 납품가/송장표시명은 그대로 둔 채 매핑 규칙(1:1 또는
  조건부)만 추가한다(`SaveChannelSkuInfoFromUnmappedPanel`/`OrderSkuMappingDialog.
  SaveChannelSkuInfoIfEntered` 참고).
- **DB 마이그레이션**: `ChannelSkuTable`의 기본키가 `(ChannelCode, Msku)`에서
  `(ChannelCode, CskuCode)`로 바뀌어야 해서(같은 Msku로 여러 CskuCode가 공존해야 하므로)
  ALTER로 처리할 수 없었다. `Database/DbSchema.MigrateChannelSkuTableToCskuCodeIfNeeded`가
  앱 시작 시 옛 스키마를 감지하면 테이블을 이름변경 → 새 스키마로 재생성 → 데이터 복사
  (CskuCode = 옛 Msku 값) → 옛 테이블 삭제 순으로 처리한다. 검증:
  `Tests/DbSchemaMigrationTests.EnsureCreated_OnLegacyChannelSkuTable_MigratesDataToCskuCodeSchema`.
- **원가 조회(이익계산)에 미친 영향 — 가장 중요한 부분**: `SettlementLoader.ApplyMappingAndProfit`이
  예전엔 `orderItem.MappedSku`를 마스터SKU로 간주해 곧바로 `ItemRepository.GetBySku`를 호출했다.
  이제는 `ChannelSkuRepository.ResolveMasterSku(channelCode, mappedSku)`를 거쳐 "CSKU 코드면 그
  CSKU의 Msku로, CSKU가 아니면(과거 방식 단순 1:1 규칙) 입력값을 그대로 마스터SKU로" 변환한 뒤
  원가를 조회한다. 이 변환을 빼먹으면 CSKU로 매핑된 모든 건이 "원가 정보 없음"으로 잘못
  처리된다 — `Tests/SettlementLoaderCskuResolutionTests`로 회귀 방지 검증해둠.
  `SkuMapper`의 `InvoiceLabel` 계산용 내부 딕셔너리도 `Msku` 키에서 `CskuCode` 키로 바꿨다
  (`_channelSkusByCskuCode`).
- **하위 호환**: 레거시 DB 마이그레이션(`LegacyMigrationService.MigrateChannelSkus`)과 마이그레이션
  데이터는 CskuCode를 별도로 몰랐으므로 CskuCode = Msku로 채운다(기존 동작과 동일하게 보임).
  과거에 만들어진 단순 1:1/조건부 규칙(TargetSku가 마스터SKU를 그대로 가리키고 CSKU 레코드가
  없는 경우)도 `ResolveMasterSku`가 그대로 통과시켜주므로 별도 마이그레이션 없이 계속 동작한다.
- **CSkuForm(마스터SKU 관리창 → "채널 SKU 관리")**: 그리드에 CSKU 코드/송장표시명 열이 추가됨.
  채널 코드를 입력하면 CskuCode가 비어있을 때만 기본값을 자동 제안한다.

## "미매핑 처리" 탭 UX 추가 개선 (CSKU 코드 신설 직후 사용자 피드백 반영)

사용자가 실제로 써보면서 4가지를 추가로 요청해 처리함(`Forms/MappingForm.cs` 변경):

1. **"매핑하기" 버튼이 안 보임(우클릭 메뉴는 동작)** — 원인은 레이아웃 버그였다. 정보 입력란
   (CSKU코드/납품가/송장표시명)이 한 줄에 다 안 들어가 줄바꿈되면서, 같은 고정 높이 영역에
   끼어 있던 버튼들이 화면 밖으로 밀려나 보이지 않는 경우가 있었다. 핵심 동작인 "매핑하기"
   버튼을 별도 행으로 분리하고 굵은 글씨로 강조해 항상 보이게 했다(`primaryButtonPanel`).
   나머지(임시SKU등록/조건부매핑추가/예외처리)는 `secondaryButtonPanel`로 분리.
2. **검색에 CSKU 결과 통합** — 검색창(`_masterSearchBox`)이 마스터DB(SKU/상품명) 검색과 동시에
   CSKU(코드/마스터SKU/송장표시명) 검색도 함께 수행하도록 `RunCskuSearch()`를 추가했다.
   결과는 별도의 "CSKU 검색결과" 그리드(옛 `_cskuHistoryGrid`를 재활용, 컬럼을 CskuCode/Msku/
   InvoiceDisplayName/SupplyPrice로 변경)에 나오고, 더블클릭하면 새로 만들지 않고 바로 그
   CSKU에 매핑된다. `ResolveSelectedMasterSku()`가 "CSKU 검색결과에서 골랐으면 그 CSKU의
   Msku, 아니면 마스터DB 후보에서 고른 Sku"를 우선순위대로 반환해 1:1매핑/조건부매핑 양쪽에서
   재사용한다.
3. **같은 발주서 안의 동일 조합 자동매핑** — 매핑/제외 처리를 하면(`ApplyMappingToItem`/
   `ExcludeSelectedUnmapped`) `ApplySameKeyToOtherUnmappedSiblings`가 지금 로드된 `_sourceOrders`
   안에서 같은 (상품명+옵션명) 키를 가진 다른 미매핑 항목을 찾아 즉시 같은 결과로 처리한다.
   재로딩 없이 한 번의 조작으로 같은 조합 전체가 해결된다. (참고: 발주서를 다시 로드할 때는
   `SkuMapper`가 항상 DB에서 최신 규칙을 다시 읽으므로 이미 자동 매핑되고 있었음 — 이번
   추가분은 "같은 배치 안에서 재로딩 없이 즉시 반영"하는 부분만 새로 보강한 것.)
4. **미매핑 리스트 헤더 폭이 고정되지 않음** — `OptionName` 컬럼에 `AutoSizeMode.Fill`을 쓰고
   있어서, 사용자가 폭을 조절해도 Fill 컬럼이 즉시 남는 공간을 다시 채워가며 되돌리는 것처럼
   보였다. 모든 컬럼을 고정 폭으로 바꾸고, 그리드를 `ExcelLikeDataGridView` +
   `PersistenceKey = "MappingForm.UnmappedGrid"`로 교체해(앱의 다른 그리드들과 동일한 패턴)
   사용자가 조절한 폭이 창을 닫을 때(`OnFormClosing`에서 `SaveLayout()`) 저장되고 다음에 열 때도
   유지되게 했다.

## 이 세션의 배경

사용자가 "Notion 기능 체크리스트 하단의 미구현/제약사항을 자동으로 구현, 필요한 권한은 자동 승인,
애매하면 권장 방향으로 먼저 개발 후 메모"를 요청해 자동화 모드로 연속 작업 중이다. 작업 단위가
끝날 때마다 체크하고, 컨텍스트 사용량이 높아지면 이 문서와 Notion 일지를 갱신하기로 합의했다.
대화가 auto-compact될 예정이라, 이 문서 + git 커밋 + 메모리 파일(`.claude/.../memory/`)이 그
이후의 유일한 컨텍스트가 된다 — 여기 적힌 정보가 가장 신뢰할 수 있는 최신 상태다.

## 이번 세션에서 한 일 (커밋 순서, 최신이 아래)

1. 다른 CLI가 남긴 중복/깨진 파일 정리(`DataLoaders/ProfitCalculator.cs` 등 4개 삭제)
2. GitHub 백업 푸시(미푸시 로컬 13개 커밋 → origin/main)
3. ExcelLikeDataGridView 붙여넣기 구현(Ctrl+V/우클릭, 기존 행 범위 안에서만)
4. 이중 출고 방지(Upsert) — `(OrderNo, MskuCode)` 유니크 인덱스 + `ON CONFLICT DO UPDATE`
5. 창 크기/위치 기억 — `FormManager` + `window_bounds.json`
6. **레거시(구버전 C# MiniERP V3) DB 마이그레이션 도구 + MainHub 동기화 요약** — 아래 절 참고
7. CSV 파일 실제 처리(`CsvWorkbookReader`, UTF-8/CP949 자동 감지)
8. 암호 걸린 엑셀 자동 해제(`ExcelFileOpener` + `PasswordPromptDialog`, 앱 내 7곳 전부 연결)
9. **조건부 매핑 다중 AND/OR 아키텍처 + 레거시 조건부 규칙 485건 이관** — 아래 절 참고

## 레거시 데이터 마이그레이션 (이번 세션 핵심 발견 — 가장 중요)

사용자가 "기존 파이썬(SalesManagerV2)으로 만든 매핑조건 json을 이식해달라"고 요청했으나, 이
머신에서 SalesManagerV2 자체나 그 json 파일은 끝내 찾지 못했다. 대신
**`C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`**
(구버전 C# MiniERP V3의 실제 운영 DB)에서 매핑 데이터를 발견했다. 이 DB의 `SalesChannelTable`
한 행에 `Memo: "[파이썬 마이그레이션]"`이 적혀있어, **구버전 C# 앱이 이미 Python SalesManagerV2의
exact_rules.json 등을 이관해 보관해온 것**으로 확인됨(`FormLegacyMigrator.cs` 참고). 즉 사용자가
찾던 "파이썬 매핑조건"의 최종 산출물이 이 DB다. (이 사실은 메모리에도 저장되어 있다: memory의
`legacy_migration_source` 항목.)

발견된 실데이터 규모: 채널 16개, 마스터SKU 550건, 채널별 납품가 405건, 1:1 매핑규칙 270건,
예외규칙 13건, 조건부 매핑규칙 485건(+상세조건 750건, AND/OR 다중조건), 임시SKU 9건.

`Database/LegacyMigrationService.cs` + MainHub의 [레거시 데이터 가져오기] 버튼으로 실행한다.
**이관 완료된 것** (전부):
- 채널 설정(필드매핑/보조소스JOIN/고정값 변환 포함)
- 마스터SKU, 채널별 납품가
- 1:1/예외 매핑규칙(270+13건)
- **조건부 매핑규칙(485건, 다중 AND/OR 조건)** — `RuleConditionDetail` 신규 테이블 +
  `ConditionEvaluator`(평가 로직) + `SkuMapper` 확장으로 임의 단순화 없이 이관 완료.
  `SkuMapper`가 실제로 이 다중조건을 평가해서 매핑하는 것까지 통합 테스트로 검증됨
  (`Migrate_ConditionRule_WorksThroughSkuMapper`).

**아직 이관하지 않은 것**(범위가 작아서 보류, 우선순위 낮음):
- `CourierMasterTable.TemplateJson`(택배사 양식 1건) — role 라벨 번역이 필요한데 데이터가
  1건뿐이라 택배사양식관리창에서 수동 입력이 더 빠름.
- 운영 이력성 테이블(OutboundTable, UploadBatchTable, ItemHistoryTable, OnlineSettlementTable,
  TrackingProfileTable) — 과거 운영기록이라 이관 대상에서 제외.

**조건부 매핑 편집 UI는 이제 있다(해결됨)**: 매핑관리창에 "조건부 매핑(상세)" 탭이 신설되어
다중조건 규칙을 보고/추가/수정/삭제할 수 있다. 기존 단순 "조건부 매핑" 탭에서 [저장]을 눌러도
이 탭이 관리하는 상세조건은 더 이상 삭제되지 않는다(분리된 즉시-저장 방식으로 구현). 자세한
내용은 위 "auto-compact 이후 추가로 한 일" 10번 참고.

`Mapping/SkuMapper.cs`에 `[EXCLUDED]` 마커 처리도 추가했다(레거시 예외규칙의 TargetSku가
`"[EXCLUDED]"`이면 실제 SKU가 아니라 "배송비/수수료 등 상품 아닌 행은 매핑 제외"라는 뜻이었음).

⚠️ **주의**: 분석 과정에서 실수로 레거시 DB의 실제 데이터(상품명/가격 등)가 담긴 디버그용 json
2개를 커밋·푸시했다가 다음 커밋에서 삭제했다(`9c9741e`). **git history에는 여전히 남아있다**
(`6793fb4` 커밋). 진짜 민감정보(비밀번호 등)는 아니지만, 완전히 지우려면 history rewrite가
필요하므로 사용자에게 확인 후 진행할 것 — 임의로 force-push/rewrite 하지 말 것.

## 알아둬야 할 설계 결정 / 임의 선택 사항 (이번 세션분)

- **레거시 ChannelType 판별은 ChannelTypeLabel 키워드 추정**이다(`LegacyMigrationService.
  GuessChannelType`). 부정확할 수 있으니 가져오기 후 채널설정에서 확인 권장.
- **레거시 STD_PRODUCT_ID, STD_AMT, STD_TAX_*, STD_DATE, STD_EXTRA1/2는 MiniERP2에 대응 필드가
  없어 이관 시 버려진다.** STD_ORDER_ID는 StdField.ProductNo로 매핑.
- **레거시는 발주서/정산서 매핑을 구분하지 않았다.** 이관 시 같은 MappingJson을
  OrderFieldMappings와 SettlementFieldMappings 양쪽에 동시 적용한다(겹치는 필드만).
- **레거시 조건 상세의 HeaderName(헤더 텍스트)→StdField 변환은 키워드 추정**이다
  (`LegacyMigrationService.TryTranslateLegacyHeaderName`: "옵션"→OptionName, "상품명"/"품목명"
  →ProductName, "수량"→Quantity, "주문"→ProductNo, "수령/받는"→Recipient, "연락처/전화"→Phone,
  "주소"→Address). 매칭 안 되는 헤더의 조건은 건너뛰고, 모든 조건이 번역 불가능하면 규칙 전체를
  건너뛴다.
- **조건부 매핑 이관은 1회성 ADD라 재실행하면 중복 추가된다**(1:1/예외 규칙처럼 병합하지 않음).
  같은 레거시 DB를 두 번 가져오면 조건부 규칙이 두 배가 되니 주의.
- **이중출고 방지 유니크 인덱스 생성 실패는 조용히 무시한다** — 과거 버그로 이미 중복 데이터가
  쌓인 DB라면 인덱스가 생성되지 않아 Upsert도 동작하지 않을 수 있음.
- CSV 인코딩은 UTF-8(BOM 포함) 우선 시도 후 실패하면 CP949로 폴백한다. 그 외 인코딩은 미지원.

## 알려진 미구현/제약사항 (남은 것)

- 문서관리(PDF 출력, Phase 8) 전체 미구현
- 운송장 매핑/동명이인 조율/합포장·분리배송 컨텍스트 메뉴(PLAN.md 5.5절 신규설계 항목) 미착수
- 이익분석 결과의 SalesManagerV2 실데이터 대비 회귀검증 — 비교할 "정답" 산출물이 없어 미수행.
  PLAN.md 명세 자체를 정답으로 한 단위테스트만 존재.
- SKU 매핑 도우미로 선택한 매핑은 1회성(영구 규칙 미저장) — 이전 세션 노트, 여전히 유효.

## 다음 작업 후보 (우선순위 제안)

1. 분할배송/합포장 **2단계**: 4줄 초과 묶음의 수동 줄합치기 편집 UI(위 절 참고, 1단계는 완료).
2. Phase 8 문서관리(거래명세표/견적서/마감내역서 PDF 출력)
3. OFS 동명이인 조율 컨텍스트 메뉴
4. git history에 남은 레거시 데이터 디버그 파일 완전 제거(사용자 확인 후 history rewrite)

## 참고

- Notion "MiniERP2" 페이지: https://app.notion.com/p/38bde24a14bc80488a35d5e74307fd08
- Notion "작업일지": https://app.notion.com/p/38bde24a14bc80488ff9d2f25e485ffa
- Notion "기능 테스트 체크리스트": https://app.notion.com/p/38bde24a14bc81348051fd2d32843288
- 레거시 운영 DB(읽기 전용으로 참고): `C:\Users\thebo\source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite`
- 정체불명의 빈 병렬 작업폴더: `C:\Users\thebo\source\repos\MiniERP2`(git 커밋 0개, 용도 불명 —
  사용자에게 삭제해도 되는지 확인 후 처리 권장)
- 이 프로젝트의 영구 메모리: `C:\Users\thebo\.claude\projects\c--Users-thebo-Documents-MiniERP2\memory\`
  (`legacy_migration_source.md`, `legacy_condition_rules_todo.md` — auto-compact 이후에도 유지됨)
