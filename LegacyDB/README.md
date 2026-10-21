# LegacyDB — 구DB (옛 C# MiniERP V3 데이터)

이 폴더의 `.sqlite` 파일은 **현재 MiniERP2가 쓰는 DB가 아니다.** MiniERP2 이전에 쓰던
C# MiniERP V3(`github.com/theborges4skin-code/MiniERP`)의 데이터베이스를 보관용으로 옮겨 둔 것이다.

- 현재 운영 DB: `bin/Debug/net10.0-windows/ERP_Database.sqlite` (이 폴더와 무관)
- 이 폴더의 DB에 **쓰지 말 것.** 읽을 때는 읽기 전용으로 열 것 (`file:<경로>?mode=ro`).
- `.sqlite`는 `.gitignore`(`*.sqlite`) 대상이라 GitHub에 올라가지 않는다. 이 PC에만 있으므로 지우지 말 것.
- 2026-10-21 `C:\Users\thebo\source\repos\MiniERP`(옛 V3 소스 폴더)를 정리하면서 이곳으로 옮겼다. 원래 폴더는 삭제됨.

## 파일

| 파일 | 원래 위치 | 내용 |
|---|---|---|
| `구DB_MiniERP-V3_20260626.sqlite` | `source\repos\MiniERP\MiniERP\bin\Debug\net10.0-windows\ERP_Database.sqlite` | **V3 최종본.** MiniERP2 레거시 이관의 원본 |
| `구DB_MiniERP-V3_20260609_Debug백업.sqlite` | `MiniERP_0609Day4작업전백업.zip` 안의 Debug DB | 6/9 백업 시점. 매핑 규칙과 채널만 있음 (품목 0건) |
| `구DB_MiniERP-V3_20260605_Release백업.sqlite` | 같은 zip 안의 Release DB | 6/5 시점. 매핑 규칙 도입 전, 품목·주문·택배 위주 |

## 최종본(20260626) 주요 테이블 행 수

- `ItemTable` 550, `ItemHistoryTable` 2514, `ChannelSkuTable` 405, `SalesChannelTable` 16
- `RuleExactTable` 270 (1:1 매핑), `RuleExceptionTable` 13 (`TargetSku="[EXCLUDED]"`는 매핑 제외라는 뜻)
- `RuleConditionTable` 485 + `RuleConditionDetailTable` 750 (AND/OR 다중조건 규칙)
- `RuleTempSkuTable` 9, `OutboundTable` 7 / `OutboundDetailTable` 16, `UploadBatchTable` 4

Python SalesManagerV2의 매핑 규칙과 채널 설정은 V3의 `FormLegacyMigrator`가 먼저 이 DB에 옮겼다.
`SalesChannelTable`에서 `Memo="[파이썬 마이그레이션]"`인 행이 그때 들어온 것이다.

## MiniERP2에서 다시 가져오기

`Database/LegacyMigrationService.cs`가 이 DB를 읽는다. 경로는 파일 선택창에서 고르며, 코드에 고정된 경로는 없다.
- 메인 허브 또는 데이터관리창의 "레거시 가져오기" 탭에서 `구DB_MiniERP-V3_20260626.sqlite`를 선택한다.
- 품목, CSKU, 채널, 단순 규칙 가져오기는 여러 번 실행해도 안전하다(upsert 방식).
  **조건 규칙 가져오기는 두 번 실행하면 중복된다.** 다시 실행하기 전에 현재 DB를 먼저 확인할 것.
