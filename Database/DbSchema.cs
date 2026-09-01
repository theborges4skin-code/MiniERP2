using Microsoft.Data.Sqlite;

namespace MiniERP2.Database;

public static class DbSchema
{
    public static void EnsureCreated(SqliteConnection connection)
    {
        // ChannelSkuTable의 기본키를 (ChannelCode, Msku)에서 (ChannelCode, CskuCode)로 바꿔야 해서
        // (한 마스터SKU가 채널 안에서 여러 CSKU로 분화될 수 있게) ALTER로는 처리할 수 없다.
        // 아래 CREATE TABLE IF NOT EXISTS가 실행되기 전에 먼저 옛 스키마를 감지해 옮겨준다.
        MigrateChannelSkuTableToCskuCodeIfNeeded(connection);

        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS ItemTable (
                Sku TEXT PRIMARY KEY,
                ItemName TEXT NOT NULL,
                CostPrice REAL NOT NULL,
                Reserve1 TEXT,
                Reserve2 TEXT,
                Reserve3 TEXT,
                ProductGroup TEXT
            );

            CREATE TABLE IF NOT EXISTS ItemCostHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Sku TEXT NOT NULL,
                OldCost REAL NOT NULL,
                NewCost REAL NOT NULL,
                ChangedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ChannelSkuTable (
                ChannelCode TEXT NOT NULL,
                CskuCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                SupplyPrice REAL NOT NULL,
                InvoiceDisplayName TEXT,
                PRIMARY KEY (ChannelCode, CskuCode)
            );

            CREATE TABLE IF NOT EXISTS ChannelSkuPriceHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                OldPrice REAL NOT NULL,
                NewPrice REAL NOT NULL,
                ChangedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ChannelSkuFieldHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                CskuCode TEXT NOT NULL,
                FieldName TEXT NOT NULL,
                OldValue TEXT,
                NewValue TEXT,
                ChangedAt TEXT NOT NULL
            );

            -- B2B 견적관리(매입측): 채널(매입처)별 마스터SKU 매입가. 매출측 ChannelSkuTable과 대칭 구조.
            CREATE TABLE IF NOT EXISTS PurchaseSkuTable (
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                PurchasePrice REAL NOT NULL,
                Unit TEXT NOT NULL DEFAULT 'kg',
                Note TEXT,
                UpdatedAt TEXT,
                PRIMARY KEY (ChannelCode, Msku)
            );

            CREATE TABLE IF NOT EXISTS PurchaseSkuPriceHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                OldPrice REAL NOT NULL,
                NewPrice REAL NOT NULL,
                ChangedAt TEXT NOT NULL,
                Reason TEXT,
                Note TEXT
            );

            -- B2B 견적관리: 발송헤더(ShipmentGroupKey) 1건 = 그 발송에 속한 모든 출고 라인이 공통으로
            -- 나누는 실운임. OutboundDetailTable.ShipmentGroupKey와 같은 값으로 연결한다.
            CREATE TABLE IF NOT EXISTS OutboundShipmentTable (
                ShipmentGroupKey TEXT PRIMARY KEY,
                FreightCost REAL NOT NULL DEFAULT 0,
                ShippedAt TEXT,
                Note TEXT
            );

            CREATE TABLE IF NOT EXISTS CourierMasterTable (
                CourierName TEXT PRIMARY KEY,
                HeaderMappingJson TEXT NOT NULL,
                TrackingImportHeaderRow INTEGER NOT NULL DEFAULT 1,
                TrackingImportRecipientHeader TEXT NOT NULL DEFAULT '',
                TrackingImportTrackingNoHeader TEXT NOT NULL DEFAULT '',
                QuantityNotationFormat TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS SalesChannelTable (
                ChannelCode TEXT PRIMARY KEY,
                ChannelName TEXT NOT NULL,
                GroupName TEXT,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                DisplayOrder INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS SettlementData (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                ProductName TEXT,
                OptionName TEXT,
                Msku TEXT,
                Qty INTEGER NOT NULL,
                Settlement REAL NOT NULL,
                Shipping REAL NOT NULL,
                Fee REAL NOT NULL,
                Profit REAL NOT NULL,
                Status TEXT
            );

            CREATE TABLE IF NOT EXISTS OutboundDetailTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL DEFAULT '',
                OrderNo TEXT NOT NULL,
                ShipmentGroupKey TEXT NOT NULL DEFAULT '',
                TrackingNo TEXT NOT NULL,
                MskuCode TEXT NOT NULL,
                Qty INTEGER NOT NULL,
                SupplyPrice REAL NOT NULL,
                CreatedAt TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT '발송대기',
                ConfirmedAt TEXT,
                Recipient TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                ProductName TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS RuleExact (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleCondition (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL,
                TargetMsku TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS RuleTemp (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleException (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetSku TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS RuleConditionDetail (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleId INTEGER NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL,
                Logic TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS MappingHistory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                OldSku TEXT NOT NULL,
                NewSku TEXT NOT NULL,
                MatchType TEXT NOT NULL,
                ChangedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ExportLogTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ExportedAt TEXT NOT NULL,
                TableName TEXT NOT NULL,
                FilePath TEXT NOT NULL,
                RowCount INTEGER NOT NULL,
                Headers TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleTemp (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetGroup TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleCondition (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Key TEXT NOT NULL,
                TargetGroup TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleConditionDetail (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleId INTEGER NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL,
                Logic TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdRuleException (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                HeaderField TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdChannelSplitSettings (
                ChannelCode TEXT PRIMARY KEY,
                Enabled INTEGER NOT NULL DEFAULT 0,
                CampaignSourceHeaders TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS AdChannelSplitInventory (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                HeaderName TEXT NOT NULL,
                Value TEXT NOT NULL,
                TargetChannel TEXT NOT NULL,
                ConfirmedAt TEXT,
                LastSeenYymm TEXT,
                LastCost REAL NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS AdChannelSplitPrerule (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ChannelCode TEXT NOT NULL,
                Priority INTEGER NOT NULL DEFAULT 0,
                TargetChannel TEXT NOT NULL,
                Note TEXT,
                Enabled INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS AdChannelSplitPreruleDetail (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RuleId INTEGER NOT NULL,
                HeaderName TEXT NOT NULL,
                Operator TEXT NOT NULL,
                TargetValue TEXT NOT NULL,
                Logic TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClosingRun (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                FolderPath TEXT NOT NULL,
                Period TEXT NOT NULL,
                Status TEXT NOT NULL DEFAULT 'draft',
                CreatedAt TEXT NOT NULL,
                UpdatedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ClosingStagedFile (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                SourceType TEXT NOT NULL DEFAULT 'settlement',
                OriginalPath TEXT NOT NULL,
                FileCreatedAt TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT 'pending',
                RowCount INTEGER NOT NULL DEFAULT 0,
                UnmappedCount INTEGER NOT NULL DEFAULT 0,
                ErrorMessage TEXT
            );

            CREATE TABLE IF NOT EXISTS ClosingUnmapped (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RunId INTEGER NOT NULL,
                ChannelCode TEXT NOT NULL,
                SourceKey TEXT NOT NULL,
                OccurrenceCount INTEGER NOT NULL DEFAULT 1,
                SampleAmount REAL NOT NULL DEFAULT 0,
                MappedSku TEXT
            );

            CREATE TABLE IF NOT EXISTS ProfitFactTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                ProductGroup TEXT NOT NULL,
                Qty INTEGER NOT NULL DEFAULT 0,
                Revenue REAL NOT NULL DEFAULT 0,
                GrossProfit REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdFactTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                ChannelCode TEXT NOT NULL,
                ChannelName TEXT NOT NULL DEFAULT '',
                ProductGroup TEXT NOT NULL,
                AdCost REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ExportSummaryDraftEntry (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                MarketCode TEXT NOT NULL,
                YearMonth TEXT NOT NULL,
                Indicator TEXT NOT NULL,
                Currency TEXT NOT NULL DEFAULT '',
                Amount REAL NOT NULL DEFAULT 0,
                SavedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS DocFavoritePhraseTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Title TEXT NOT NULL DEFAULT '',
                Body TEXT NOT NULL DEFAULT '',
                Category TEXT NOT NULL DEFAULT '일반',
                IsFavorite INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DocPartyTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ProfileName TEXT NOT NULL DEFAULT '',
                RegNo TEXT NOT NULL DEFAULT '',
                CompanyName TEXT NOT NULL DEFAULT '',
                CeoName TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                BizType TEXT NOT NULL DEFAULT '',
                BizItem TEXT NOT NULL DEFAULT '',
                Tel TEXT NOT NULL DEFAULT '',
                Email TEXT NOT NULL DEFAULT '',
                IsDefaultSupplier INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS DocStatementTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                PartyId INTEGER NOT NULL,
                IssueDate TEXT,
                IssueYearMonth TEXT NOT NULL DEFAULT '',
                TotalSupply REAL NOT NULL DEFAULT 0,
                TotalTax REAL NOT NULL DEFAULT 0,
                TotalAmount REAL NOT NULL DEFAULT 0,
                TotalQty REAL NOT NULL DEFAULT 0,
                CarryoverBalance REAL NOT NULL DEFAULT 0,
                ReconcileNote TEXT NOT NULL DEFAULT '',
                TemplateSignature TEXT NOT NULL DEFAULT '',
                StatusFlags TEXT NOT NULL DEFAULT '',
                SourceFileName TEXT NOT NULL DEFAULT '',
                SourceSheetName TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UNIQUE(SourceFileName, SourceSheetName)
            );

            CREATE TABLE IF NOT EXISTS DocStatementLineTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                StatementId INTEGER NOT NULL,
                RowNo INTEGER NOT NULL DEFAULT 0,
                LineDate TEXT,
                ItemName TEXT NOT NULL DEFAULT '',
                Spec TEXT NOT NULL DEFAULT '',
                Qty REAL NOT NULL DEFAULT 0,
                UnitPrice REAL NOT NULL DEFAULT 0,
                UnitPriceVatIncluded INTEGER NOT NULL DEFAULT 0,
                SupplyAmount REAL NOT NULL DEFAULT 0,
                Tax REAL NOT NULL DEFAULT 0,
                Total REAL NOT NULL DEFAULT 0,
                Note TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS DocHistoryTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                DocType TEXT NOT NULL DEFAULT '',
                IssueDate TEXT NOT NULL DEFAULT '',
                BuyerName TEXT NOT NULL DEFAULT '',
                TotalAmount REAL NOT NULL DEFAULT 0,
                FilePath TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT ''
            );

            -- 거래처 마감보드(거래처마감보드_개발기획서.md §5.1): 거래처 자체(기간과 무관)를 나타내는
            -- 상시 마스터. 즐겨찾기(고정 노출)·수동 거래처 등록의 기준이 된다. SalesChannelTable.
            -- IsFavorite(OFS 채널 선택용)와는 목적이 다른 별개의 즐겨찾기 축이라 재사용하지 않는다.
            CREATE TABLE IF NOT EXISTS PartnerMasterTable (
                PartyKey TEXT PRIMARY KEY,
                PartyName TEXT NOT NULL DEFAULT '',
                IsManual INTEGER NOT NULL DEFAULT 0,
                IsFavorite INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1
            );

            -- 거래처×월 마감 헤더(§5.2). 라인 스냅샷은 PartnerClosingLineTable(§5.3)에 별도 보관해
            -- 확정 이후 원본 OutboundDetailTable 라인이 편집·삭제돼도 발행된 명세표와 어긋나지 않는다.
            CREATE TABLE IF NOT EXISTS PartnerClosingTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                PartyKey TEXT NOT NULL,
                PartyName TEXT NOT NULL DEFAULT '',
                IsManual INTEGER NOT NULL DEFAULT 0,
                Status TEXT NOT NULL DEFAULT '미확인',
                TotalQty REAL NOT NULL DEFAULT 0,
                TotalSupply REAL NOT NULL DEFAULT 0,
                TotalCost REAL NOT NULL DEFAULT 0,
                TotalProfit REAL NOT NULL DEFAULT 0,
                FreightAllocated REAL NOT NULL DEFAULT 0,
                ReconcileNote TEXT NOT NULL DEFAULT '',
                ConfirmedAt TEXT,
                DocHistoryId INTEGER
            );

            CREATE UNIQUE INDEX IF NOT EXISTS UX_PartnerClosing_Period_PartyKey ON PartnerClosingTable (Period, PartyKey);

            CREATE TABLE IF NOT EXISTS PartnerClosingLineTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ClosingId INTEGER NOT NULL,
                OutboundDetailId INTEGER,
                LineDate TEXT NOT NULL DEFAULT '',
                CskuCode TEXT NOT NULL DEFAULT '',
                MasterSku TEXT NOT NULL DEFAULT '',
                ItemName TEXT NOT NULL DEFAULT '',
                Spec TEXT NOT NULL DEFAULT '',
                Qty REAL NOT NULL DEFAULT 0,
                UnitPrice REAL NOT NULL DEFAULT 0,
                CostPrice REAL NOT NULL DEFAULT 0,
                Profit REAL NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_PartnerClosingLine_ClosingId ON PartnerClosingLineTable (ClosingId);

            -- 거래처 마감보드 메모. 거래처 전체에 대한 메모(OutboundDetailIds='')와, 특정 라인(들)을
            -- 참조하는 메모(OutboundDetailIds에 쉼표구분 Id 목록) 두 종류를 같은 테이블에서 다룬다.
            -- 명세표/매출장 발행 문서에 그대로 노출되므로 ShowOnStatement/ShowOnLedger로 각각 켜고 끈다
            -- (PartnerClosingDocumentBuilder 참고).
            CREATE TABLE IF NOT EXISTS PartnerClosingMemoTable (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Period TEXT NOT NULL,
                PartyKey TEXT NOT NULL,
                MemoText TEXT NOT NULL DEFAULT '',
                ShowOnStatement INTEGER NOT NULL DEFAULT 1,
                ShowOnLedger INTEGER NOT NULL DEFAULT 1,
                OutboundDetailIds TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_PartnerClosingMemo_Period_PartyKey ON PartnerClosingMemoTable (Period, PartyKey);

            CREATE TABLE IF NOT EXISTS FboCskuMaster (
                Csku TEXT PRIMARY KEY,
                FboItemCode TEXT NOT NULL DEFAULT '',
                ItemName TEXT NOT NULL DEFAULT '',
                QtyPerBox INTEGER NOT NULL DEFAULT 0,
                BoxType TEXT NOT NULL DEFAULT '소',
                FreightType TEXT,
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FboChannelConfig (
                ChannelId TEXT PRIMARY KEY,
                ChannelName TEXT NOT NULL DEFAULT '',
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                ReceiverSeqFormat TEXT NOT NULL DEFAULT '{name}{seq:00}',
                ChannelLabel TEXT NOT NULL DEFAULT '',
                OrderNoPrefix TEXT NOT NULL DEFAULT '#FBO',
                InboundType TEXT NOT NULL DEFAULT '31',
                IsDefault INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS FboOrder (
                FboNo TEXT PRIMARY KEY,
                OrderDate TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT '작성중',
                Memo TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FboBox (
                FboNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                ReceiverDisplayName TEXT NOT NULL DEFAULT '',
                MatchKey TEXT NOT NULL DEFAULT '',
                BoxType TEXT NOT NULL DEFAULT '소',
                TrackingNo TEXT,
                TrackingLoadedAt TEXT,
                Status TEXT NOT NULL DEFAULT '대기',
                PRIMARY KEY (FboNo, BoxSeq)
            );

            CREATE TABLE IF NOT EXISTS FboBoxItem (
                FboNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                ItemSeq INTEGER NOT NULL,
                Csku TEXT NOT NULL,
                FboItemCode TEXT NOT NULL DEFAULT '',
                ItemName TEXT NOT NULL DEFAULT '',
                QtyPerBox INTEGER NOT NULL DEFAULT 0,
                Qty INTEGER NOT NULL DEFAULT 0,
                ExpiryDate TEXT,
                PRIMARY KEY (FboNo, BoxSeq, ItemSeq)
            );

            CREATE INDEX IF NOT EXISTS IX_FboBox_MatchKey ON FboBox (MatchKey);

            -- 아마존 FBA 발주 관리(FBA발주관리_개발기획서.md): FBO를 레퍼런스로 하되, 박스=CSKU
            -- 혼재(박스 우선 구성), 박스규격 마스터+치수 스냅샷, 고객주문번호 매칭키가 다르다(§2).
            CREATE TABLE IF NOT EXISTS FbaCskuMaster (
                Csku TEXT PRIMARY KEY,
                ItemName TEXT NOT NULL DEFAULT '',
                InvoiceDisplayName TEXT,
                Asin TEXT NOT NULL DEFAULT '',
                CommodityDescription TEXT NOT NULL DEFAULT '',
                HsCode TEXT NOT NULL DEFAULT '',
                ItemPrice REAL NOT NULL DEFAULT 0,
                Volume TEXT NOT NULL DEFAULT '',
                UnitWeightG REAL NOT NULL DEFAULT 0,
                QtyPerLayer INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            -- 초기 4건(실측)은 스키마 초기화가 아니라 FbaBoxSpecRepository.EnsureDefaultBoxSpecs()에서
            -- MainHub 시작 시 1회만 시드한다(§10 수정사항 — 테스트가 매번 만드는 임시 DB까지 시드되어
            -- 행 개수를 세는 기존 테스트가 깨지는 것을 방지, SalesChannelRepository.EnsureSampleChannel과 동일 이유).
            CREATE TABLE IF NOT EXISTS FbaBoxSpec (
                BoxName TEXT PRIMARY KEY,
                WidthMm REAL NOT NULL DEFAULT 0,
                DepthMm REAL NOT NULL DEFAULT 0,
                HeightMm REAL NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                IsActive INTEGER NOT NULL DEFAULT 1,
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FbaConfig (
                ConfigKey TEXT PRIMARY KEY DEFAULT 'DEFAULT',
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Phone2 TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                DeliveryMessage TEXT NOT NULL DEFAULT '',
                BoxTypeLabel TEXT NOT NULL DEFAULT '중',
                TransferType TEXT NOT NULL DEFAULT '',
                Etc1 TEXT NOT NULL DEFAULT '',
                OrderNoPrefix TEXT NOT NULL DEFAULT '#FBA'
            );

            CREATE TABLE IF NOT EXISTS FbaOrder (
                FbaNo TEXT PRIMARY KEY,
                OrderDate TEXT NOT NULL,
                ShipmentId TEXT,
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone TEXT NOT NULL DEFAULT '',
                Address TEXT NOT NULL DEFAULT '',
                Status TEXT NOT NULL DEFAULT '작성중',
                Memo TEXT NOT NULL DEFAULT '',
                CreatedAt TEXT NOT NULL DEFAULT '',
                UpdatedAt TEXT NOT NULL DEFAULT ''
            );

            CREATE TABLE IF NOT EXISTS FbaBox (
                FbaNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                BoxSpecName TEXT NOT NULL DEFAULT '',
                WidthMm REAL NOT NULL DEFAULT 0,
                DepthMm REAL NOT NULL DEFAULT 0,
                HeightMm REAL NOT NULL DEFAULT 0,
                IsCustomSize INTEGER NOT NULL DEFAULT 0,
                WeightG REAL NOT NULL DEFAULT 0,
                MatchKey TEXT NOT NULL DEFAULT '',
                TrackingNo TEXT,
                TrackingLoadedAt TEXT,
                Status TEXT NOT NULL DEFAULT '대기',
                PRIMARY KEY (FbaNo, BoxSeq)
            );

            CREATE TABLE IF NOT EXISTS FbaBoxItem (
                FbaNo TEXT NOT NULL,
                BoxSeq INTEGER NOT NULL,
                ItemSeq INTEGER NOT NULL,
                Csku TEXT NOT NULL,
                ItemName TEXT NOT NULL DEFAULT '',
                InvoiceDisplayName TEXT,
                Asin TEXT NOT NULL DEFAULT '',
                CommodityDescription TEXT NOT NULL DEFAULT '',
                HsCode TEXT NOT NULL DEFAULT '',
                ItemPrice REAL NOT NULL DEFAULT 0,
                UnitWeightG REAL NOT NULL DEFAULT 0,
                QtyPerLayer INTEGER NOT NULL DEFAULT 0,
                Qty INTEGER NOT NULL DEFAULT 0,
                ExpiryDate TEXT,
                PRIMARY KEY (FbaNo, BoxSeq, ItemSeq)
            );

            CREATE INDEX IF NOT EXISTS IX_FbaBox_MatchKey ON FbaBox (MatchKey);

            -- FBA 발주 작성 화면의 "미배정 품목" 임시저장 장바구니. 발주 1건과 무관한 단일 슬롯이며
            -- 저장할 때마다 전체를 지우고 다시 채운다(FbaCartRepository.SaveAll — ExportSummaryDraftRepository와
            -- 동일한 전체교체 패턴). 그때그때 발송 확정되는 품목을 담아뒀다가 박스 수량이 찰 때
            -- 한꺼번에 박스 구성으로 넘기기 위함이다.
            CREATE TABLE IF NOT EXISTS FbaCartItem (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Csku TEXT NOT NULL,
                Qty INTEGER NOT NULL DEFAULT 0,
                ExpiryDate TEXT,
                SavedAt TEXT NOT NULL DEFAULT ''
            );

            -- 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.1~3.2): 견적 1건 = 1회 전달
            -- 단위(헤더) + 품목별 라인. 기존 ChannelSkuPriceHistory/PurchaseSkuPriceHistory는 Upsert가
            -- 만드는 감사로그라 "이번 달 단가표 1건 = N품목" 업무 단위를 담지 못해서 별도로 신설한다.
            CREATE TABLE IF NOT EXISTS PriceQuoteTable (
                Id              INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteNo         TEXT NOT NULL DEFAULT '',
                ChannelCode     TEXT NOT NULL,
                PriceKind       TEXT NOT NULL DEFAULT 'Supply',
                QuoteFormType   TEXT NOT NULL DEFAULT 'UnitOnly',
                Origin          TEXT NOT NULL DEFAULT 'Manual',
                Title           TEXT NOT NULL DEFAULT '',
                QuoteDate       TEXT NOT NULL DEFAULT '',
                EffectiveFrom   TEXT NOT NULL DEFAULT '',
                EffectiveTo     TEXT,
                AutoApply       INTEGER NOT NULL DEFAULT 0,
                Status          TEXT NOT NULL DEFAULT 'Draft',
                DeliveryMethod  TEXT NOT NULL DEFAULT '',
                DeliveredAt     TEXT,
                DeliveredTo     TEXT NOT NULL DEFAULT '',
                Note            TEXT NOT NULL DEFAULT '',
                PriceBasis      TEXT NOT NULL DEFAULT 'VatExcl',
                RootQuoteId     INTEGER,
                RevisionNo      INTEGER NOT NULL DEFAULT 0,
                SupersededBy    INTEGER,
                RevisionReason  TEXT NOT NULL DEFAULT '',
                CreatedAt       TEXT NOT NULL DEFAULT '',
                UpdatedAt       TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Channel_Eff ON PriceQuoteTable (ChannelCode, EffectiveFrom);
            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Revision ON PriceQuoteTable (RootQuoteId, RevisionNo);
            CREATE INDEX IF NOT EXISTS IX_PriceQuote_Origin ON PriceQuoteTable (ChannelCode, Origin, Status);
            CREATE UNIQUE INDEX IF NOT EXISTS UX_PriceQuote_QuoteNo ON PriceQuoteTable (QuoteNo);

            CREATE TABLE IF NOT EXISTS AutoOrderInboxTable (
                Id TEXT PRIMARY KEY,
                SubjectSnip TEXT NOT NULL DEFAULT '',
                ReceivedAt TEXT NOT NULL,
                XlsxPath TEXT NOT NULL DEFAULT '',
                Sha256 TEXT NOT NULL DEFAULT '',
                RowCount INTEGER NOT NULL DEFAULT 0,
                ParseStatus TEXT NOT NULL DEFAULT 'ok',
                Status TEXT NOT NULL DEFAULT 'new',
                LocalFilePath TEXT,
                SeenAt TEXT NOT NULL,
                ImportedAt TEXT
            );

            CREATE TABLE IF NOT EXISTS PriceQuoteLineTable (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                QuoteId       INTEGER NOT NULL,
                RowNo         INTEGER NOT NULL DEFAULT 0,
                CskuCode      TEXT NOT NULL DEFAULT '',
                Msku          TEXT NOT NULL DEFAULT '',
                ItemNameSnap  TEXT NOT NULL DEFAULT '',
                Spec          TEXT NOT NULL DEFAULT '',
                Unit          TEXT NOT NULL DEFAULT 'EA',
                Qty           REAL NOT NULL DEFAULT 0,
                OldPrice      REAL,
                NewPrice      REAL NOT NULL DEFAULT 0,
                SupplyAmount  REAL NOT NULL DEFAULT 0,
                Tax           REAL NOT NULL DEFAULT 0,
                Total         REAL NOT NULL DEFAULT 0,
                ChangeReason  TEXT NOT NULL DEFAULT '',
                Note          TEXT NOT NULL DEFAULT '',
                IsApplied     INTEGER NOT NULL DEFAULT 0,
                PromotedFrom  INTEGER
            );

            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Quote_Row ON PriceQuoteLineTable (QuoteId, RowNo);
            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Csku ON PriceQuoteLineTable (CskuCode);
            CREATE INDEX IF NOT EXISTS IX_PriceQuoteLine_Msku ON PriceQuoteLineTable (Msku);

            -- ⚠ 임시(실험용) — 문서이력_조회축_갭재검토_A.md rev.2. 기존 PriceQuoteTable/DocHistoryTable/
            -- ChannelSkuPriceHistory 등 실제 서비스 테이블과 완전히 독립적으로, 견적/거래명세표/
            -- 가격조정 3종 문서의 라인 이력을 채널×CSKU×기간으로 통합 조회하는 기능을 먼저
            -- 단독으로 개발·검증하기 위한 표(tidy-long, 문서유형이 달라도 한 행 = 한 품목줄로
            -- 정규화). 검증이 끝나면 실제 문서관리 기능에 편입하거나 이 표 자체를 폐기한다.
            CREATE TABLE IF NOT EXISTS DocLineHistoryTable (
                Id            INTEGER PRIMARY KEY AUTOINCREMENT,
                DocGroupKey   TEXT NOT NULL DEFAULT '',   -- 같은 문서(발행 1건)에 속한 줄을 묶는 키
                DocNo         TEXT NOT NULL DEFAULT '',   -- 문서번호/식별자(있으면)
                DocType       TEXT NOT NULL DEFAULT '',   -- Quote / Statement / PriceAdjustment
                ChannelCode   TEXT NOT NULL DEFAULT '',
                ChannelName   TEXT NOT NULL DEFAULT '',   -- 표시용 스냅샷(조인 없이 바로 보여주기 위함)
                CskuCode      TEXT NOT NULL DEFAULT '',   -- '' = 미매핑(자유품목) 버킷
                ItemNameSnap  TEXT NOT NULL DEFAULT '',
                Qty           REAL NOT NULL DEFAULT 0,
                UnitPrice     REAL NOT NULL DEFAULT 0,
                SupplyAmount  REAL NOT NULL DEFAULT 0,
                Tax           REAL NOT NULL DEFAULT 0,
                Total         REAL NOT NULL DEFAULT 0,
                IssueDate     TEXT NOT NULL DEFAULT '',   -- 귀속일(견적=발행일/명세=출고확정일/가격조정=적용일)
                YearMonth     TEXT NOT NULL DEFAULT '',   -- 'yyyy-MM' 파생 저장(조회/집계용)
                Quarter       TEXT NOT NULL DEFAULT '',   -- 'yyyy-Q1'~'yyyy-Q4' 파생 저장
                SourceRef     TEXT NOT NULL DEFAULT '',   -- 원본 문서 참조(파일경로 등 느슨한 텍스트 링크)
                Note          TEXT NOT NULL DEFAULT '',
                CreatedAt     TEXT NOT NULL DEFAULT ''
            );

            CREATE INDEX IF NOT EXISTS IX_DocLineHistory_Channel_Csku ON DocLineHistoryTable (ChannelCode, CskuCode);
            CREATE INDEX IF NOT EXISTS IX_DocLineHistory_YearMonth ON DocLineHistoryTable (YearMonth);
            CREATE INDEX IF NOT EXISTS IX_DocLineHistory_Quarter ON DocLineHistoryTable (Quarter);
            CREATE INDEX IF NOT EXISTS IX_DocLineHistory_DocGroup ON DocLineHistoryTable (DocGroupKey);

            -- 배송지주소록_개발기획서_확정본.md: 채널 종속 없는 범용 배송지 주소록(발주지 주소 원장).
            -- SalesChannelTable/FboChannelConfig/DocPartyTable과는 완전히 별개이며(§1 확정), OFS의
            -- "배송지 불러오기"에서만 골라 쓴다.
            CREATE TABLE IF NOT EXISTS AddressBookTable (
                AddressId    INTEGER PRIMARY KEY AUTOINCREMENT,
                Label        TEXT NOT NULL DEFAULT '',
                ReceiverName TEXT NOT NULL DEFAULT '',
                Phone        TEXT NOT NULL DEFAULT '',
                Address      TEXT NOT NULL DEFAULT '',
                Memo         TEXT NOT NULL DEFAULT '',
                IsActive     INTEGER NOT NULL DEFAULT 1,
                DisplayOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAt    TEXT NOT NULL DEFAULT ''
            );

            -- 주소 ↔ 채널 태그(다대다, 선택적). ChannelCode는 참조만 하고 FK를 강제하지 않는다
            -- (채널이 삭제돼도 주소 원장은 보존 — DocPartyTable.ChannelCode와 같은 느슨한 참조 관례).
            CREATE TABLE IF NOT EXISTS AddressChannelTagTable (
                AddressId   INTEGER NOT NULL,
                ChannelCode TEXT NOT NULL,
                PRIMARY KEY (AddressId, ChannelCode)
            );

            -- CSKU별 통계(CSKU별통계_개발기획서.md §5). 배치 단위 스냅샷 누적 — 같은 기간·채널을
            -- 다시 로드해도 기존 배치를 덮어쓰지 않는다.
            CREATE TABLE IF NOT EXISTS CskuStatBatchTable (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                Period       TEXT NOT NULL,
                Memo         TEXT NOT NULL DEFAULT '',
                ExchangeRate REAL NOT NULL DEFAULT 0,
                FileCount    INTEGER NOT NULL DEFAULT 0,
                RowCount     INTEGER NOT NULL DEFAULT 0,
                CreatedAt    TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CskuStatLineTable (
                Id           INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId      INTEGER NOT NULL,
                FileKind     TEXT NOT NULL,
                ChannelCode  TEXT NOT NULL,
                ChannelName  TEXT NOT NULL DEFAULT '',
                CskuCode     TEXT NOT NULL,
                ProductGroup TEXT NOT NULL DEFAULT '',
                ProductName  TEXT NOT NULL DEFAULT '',
                RowCount     INTEGER NOT NULL DEFAULT 0,
                Qty          INTEGER NOT NULL DEFAULT 0,
                Revenue      REAL NOT NULL DEFAULT 0,
                Settlement   REAL NOT NULL DEFAULT 0,
                Shipping     REAL NOT NULL DEFAULT 0,
                Fee          REAL NOT NULL DEFAULT 0,
                Profit       REAL NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS IX_CskuStatLine_BatchId ON CskuStatLineTable (BatchId);

            -- 배치에 포함된 파일 이력(중복판정 근거, §7).
            CREATE TABLE IF NOT EXISTS CskuStatFileTable (
                Id         INTEGER PRIMARY KEY AUTOINCREMENT,
                BatchId    INTEGER NOT NULL,
                FileName   TEXT NOT NULL,
                FileKind   TEXT NOT NULL,
                RowCount   INTEGER NOT NULL DEFAULT 0,
                SumQty     INTEGER NOT NULL DEFAULT 0,
                SumRevenue REAL NOT NULL DEFAULT 0,
                SumProfit  REAL NOT NULL DEFAULT 0,
                LoadedAt   TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS IX_CskuStatFile_FileName ON CskuStatFileTable (FileName);
            """;
        command.ExecuteNonQuery();

        // RuleCondition에 TargetMsku 추가 — Settlement 전용 규칙(CSKU 없이 MSKU만 매핑)에 사용한다.
        EnsureColumn(connection, "RuleCondition", "TargetMsku", "TEXT NOT NULL DEFAULT ''");

        // 이중 출고 방지(Upsert) 유니크 인덱스.
        // 분리배송(ShipmentGroupId가 다른 동일 OrderNo) 지원을 위해 ShipmentGroupKey 기준으로 교체한다.
        // 기존 DB는 ShipmentGroupKey=''이므로, 먼저 OrderNo로 채운 뒤 인덱스를 전환한다.
        EnsureColumn(connection, "OutboundDetailTable", "ShipmentGroupKey", "TEXT NOT NULL DEFAULT ''");
        try
        {
            using var fillCmd = connection.CreateCommand();
            fillCmd.CommandText = "UPDATE OutboundDetailTable SET ShipmentGroupKey = OrderNo WHERE ShipmentGroupKey = ''";
            fillCmd.ExecuteNonQuery();

            using var dropCmd = connection.CreateCommand();
            dropCmd.CommandText = "DROP INDEX IF EXISTS IX_OutboundDetailTable_OrderNo_MskuCode";
            dropCmd.ExecuteNonQuery();

            using var createCmd = connection.CreateCommand();
            createCmd.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_OutboundDetailTable_ShipmentGroupKey_MskuCode ON OutboundDetailTable (ShipmentGroupKey, MskuCode)";
            createCmd.ExecuteNonQuery();
        }
        catch (SqliteException)
        {
            // 기존 중복 데이터로 인덱스 전환이 실패해도 무시하고 계속 진행한다.
        }

        // CREATE TABLE IF NOT EXISTS는 이미 존재하는 테이블에 새 컬럼을 추가해주지 않으므로,
        // 이전 버전의 DB 파일에서도 신규 컬럼이 누락되지 않도록 직접 보강한다.
        EnsureColumn(connection, "ItemTable", "Reserve1", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve2", "TEXT");
        EnsureColumn(connection, "ItemTable", "Reserve3", "TEXT");
        EnsureColumn(connection, "ItemTable", "ProductGroup", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "InvoiceDisplayName", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "Note", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "UpdatedAt", "TEXT");
        EnsureColumn(connection, "ChannelSkuTable", "Unit", "TEXT NOT NULL DEFAULT 'kg'");
        EnsureColumn(connection, "ChannelSkuTable", "Packing", "TEXT");
        // NULL=마스터DB 원가(ItemTable.CostPrice) 연동, 값 있음=이 CSKU만 개별 원가 관리(CSKU제조원가_개별관리_개발기획서.md §4.1).
        EnsureColumn(connection, "ChannelSkuTable", "CostPriceOverride", "REAL");
        EnsureColumn(connection, "ChannelSkuPriceHistory", "Reason", "TEXT");
        EnsureColumn(connection, "ChannelSkuPriceHistory", "Note", "TEXT");
        EnsureColumn(connection, "ItemTable", "Unit", "TEXT NOT NULL DEFAULT 'kg'");
        EnsureColumn(connection, "OutboundDetailTable", "Status", "TEXT NOT NULL DEFAULT '발송대기'");
        EnsureColumn(connection, "OutboundDetailTable", "ConfirmedAt", "TEXT");
        EnsureColumn(connection, "OutboundDetailTable", "Recipient", "TEXT NOT NULL DEFAULT ''");
        // 발주/출고 이력 관리창에서 "선택 건 택배사 양식 출력" 시 연락처 칸이 항상 공란으로 나가던
        // 문제 — Recipient/Address와 같은 이유(발주확정 시점 스냅샷)로 Phone도 함께 저장한다.
        EnsureColumn(connection, "OutboundDetailTable", "Phone", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "Address", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "OutboundDetailTable", "ProductName", "TEXT NOT NULL DEFAULT ''");
        // B2B 견적관리(§2.8): 판매 출고 라인에 매입처/원가 스냅샷/중량을 붙인다. CSKU는 별도 컬럼을
        // 두지 않는다 — MskuCode가 이름과 달리 이미 CSKU 코드를 저장하고 있다(OfsOrderItem.MappedSku).
        EnsureColumn(connection, "OutboundDetailTable", "PurchaseChannelCode", "TEXT");
        EnsureColumn(connection, "OutboundDetailTable", "PurchasePrice", "REAL");
        EnsureColumn(connection, "OutboundDetailTable", "WeightKg", "REAL");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportHeaderRow", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportRecipientHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportTrackingNoHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "QuantityNotationFormat", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportOrderNoHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportAddressHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportProductNameHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportReceivedDateHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "CourierMasterTable", "TrackingImportFreightCostHeader", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "SalesChannelTable", "LastUsedDate", "TEXT");
        // B2B 견적관리(§2.1): 한 채널이 매입·매출을 동시에 겸할 수 있어 별도 VendorTable 대신 플래그로
        // 구분한다. 기존 채널은 전부 판매 채널이었으므로 IsSales 기본값 1, IsPurchase 기본값 0.
        EnsureColumn(connection, "SalesChannelTable", "IsPurchase", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "SalesChannelTable", "IsSales", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(connection, "DocPartyTable", "ChannelCode", "TEXT");
        EnsureColumn(connection, "DocPartyTable", "IsActive", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DocPartyTable", "CreatedAt", "TEXT");
        EnsureColumn(connection, "DocPartyTable", "StampImagePath", "TEXT");
        // FBO 발주처리 — 하배출고이서(택배사 출력양식)용 품목 표시명을 내부 관리용 ItemName과
        // 분리한다(OFS ChannelSkuTable.InvoiceDisplayName과 같은 목적). FboBoxItem은 저장 시점의
        // 스냅샷을 갖는다.
        EnsureColumn(connection, "FboCskuMaster", "InvoiceDisplayName", "TEXT");
        EnsureColumn(connection, "FboBoxItem", "InvoiceDisplayName", "TEXT");

        // 견적/가격 기록 관리(견적기록관리_개발기획서_확정본.md §3.3~3.4, Step 1).
        EnsureColumn(connection, "ChannelSkuPriceHistory", "QuoteId", "INTEGER");
        EnsureColumn(connection, "PurchaseSkuTable", "IsPrimary", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "PurchaseSkuPriceHistory", "QuoteId", "INTEGER");
        EnsureColumn(connection, "SalesChannelTable", "AutoQuoteDraft", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(connection, "DocHistoryTable", "SourceQuoteId", "INTEGER");
        // OutboundDetailTable.CskuCode(G9 해소) — MskuCode는 이름과 달리 이미 CSKU 코드를 저장하고
        // 있어(§7.0) 당장 필수는 아니지만, §7.1 실적 조회에서 CSKU 기준 명시적 조회가 필요해 추가한다.
        EnsureColumn(connection, "OutboundDetailTable", "CskuCode", "TEXT NOT NULL DEFAULT ''");
        // 자동발주처리 연동: 비고(내부관리용 메모, OfsOrderItem.Remark) 발주확정 시점 스냅샷.
        EnsureColumn(connection, "OutboundDetailTable", "Remark", "TEXT NOT NULL DEFAULT ''");
        // 문서발행 이력에서 FilePath만 들고 있으면 사용자가 파일을 옮기거나 지운 뒤 이력에서 "파일
        // 열기"를 누를 때 못 찾는 문제가 있었다(사용자 신고) — 생성 시점의 엑셀 바이트를 DB에도
        // 함께 백업해, 원본 경로가 없어져도 이력에서 그대로 복원해 열 수 있게 한다.
        EnsureColumn(connection, "DocHistoryTable", "FileBytes", "BLOB");

        // 거래처 마감보드(거래처마감보드_개발기획서.md §4, §5.4): 귀속월 수동 고정값. 빈 값이면
        // 보드가 ConfirmedAt의 연월로 자동 판정한다.
        EnsureColumn(connection, "OutboundDetailTable", "ClosingPeriod", "TEXT NOT NULL DEFAULT ''");
        // 보드에서 발행 실적을 역참조하기 위한 채널·귀속월 기록(문서 발행 시점에 채워짐).
        EnsureColumn(connection, "DocHistoryTable", "ChannelCode", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(connection, "DocHistoryTable", "Period", "TEXT NOT NULL DEFAULT ''");
        // 조건부매핑 상세조건에서 정산파일 원본 열(HeaderField=Raw일 때 참조할 실제 열 이름)을 저장.
        EnsureColumn(connection, "RuleConditionDetail", "RawFieldName", "TEXT");
        // 샘플발송 이력관리(샘플발송이력관리_개발기획서.md §2 D1): 비매출성 발송 구분값.
        // 비어있으면 정상 거래, 비어있지 않으면(샘플/CS/기타) 마감·정산·명세표 집계에서 제외한다.
        EnsureColumn(connection, "OutboundDetailTable", "LineKind", "TEXT NOT NULL DEFAULT ''");
        // §2 D3의 "샘플등" 통합 채널 시드는 여기(모든 DB 연결마다/테스트 DB마다 실행됨)가 아니라
        // SalesChannelRepository.EnsureSampleChannel()로 옮겨 MainHub 시작 시 1회만 보장한다 —
        // 여기 두면 테스트가 매번 새로 만드는 임시 DB에도 채널이 하나 더 생겨, 채널 개수를 세는
        // 기존 테스트들이 깨진다(실제로 겪음).

        // 매핑시스템 통합개편 Phase 1(§5): 1:1 정확매핑을 상품명+옵션명+수량+매출액 4필드로 확장하기
        // 위한 스키마. 두 컬럼 모두 NULL이면 기존 2필드(상품명+옵션명) 레거시 규칙 그대로 동작하고,
        // 채워지면 신규 4필드 규칙이 된다(신구 병행 — Phase 2에서 매칭 로직 결선 예정).
        EnsureColumn(connection, "RuleExact", "Quantity", "INTEGER");
        EnsureColumn(connection, "RuleExact", "Price", "REAL");
        EnsureColumn(connection, "RuleTemp", "Quantity", "INTEGER");
        EnsureColumn(connection, "RuleTemp", "Price", "REAL");
        // 정산분석 이익분석 결과의 매출액(Revenue)을 영속화(기존엔 메모리 전용, Phase 2에서 실제 저장/조회 결선 예정).
        EnsureColumn(connection, "SettlementData", "Revenue", "REAL NOT NULL DEFAULT 0");
        // 가격 매핑이 있는 채널의 미매핑 집계를 상품명+옵션명+수량+매출액 4필드 키로 정밀화하기 위한 컬럼
        // (Phase 2에서 집계 로직 결선 예정).
        EnsureColumn(connection, "ClosingUnmapped", "Quantity", "INTEGER");
        EnsureColumn(connection, "ClosingUnmapped", "SampleRevenue", "REAL");
        // FBA 품목마스터 관리용 필드: MOCRA(미국 화장품 규제) Listing No.
        EnsureColumn(connection, "FbaCskuMaster", "MocraListingNo", "TEXT NOT NULL DEFAULT ''");

        // 발주/출고 이력관리 "누적발주서 송장번호 입력"이 채널 발주서매핑의 택배사 열에서 읽어
        // 채워 넣는 값(§StdField.CourierName). 그 외 경로로는 채워지지 않는다.
        EnsureColumn(connection, "OutboundDetailTable", "CourierName", "TEXT NOT NULL DEFAULT ''");

        // 발주확정/출고확정 용어로 바뀌기 전에 저장된 옛 상태값("발송대기"/"발송완료")이 남아있으면
        // 발주/출고 이력 관리창의 상태 콤보(두 값만 허용)에서 DataGridViewComboBoxCell 오류가 난다.
        // 기동 시마다 실행해도 안전한 정규화(이미 새 값이면 매치 없음 → no-op)이다.
        NormalizeLegacyOutboundStatus(connection);
    }

    private static void NormalizeLegacyOutboundStatus(SqliteConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE OutboundDetailTable SET Status = '발주확정' WHERE Status = '발송대기';
            UPDATE OutboundDetailTable SET Status = '출고확정' WHERE Status = '발송완료';
            """;
        command.ExecuteNonQuery();
    }

    private static void EnsureColumn(SqliteConnection connection, string tableName, string columnName, string columnType)
    {
        if (HasColumn(connection, tableName, columnName)) return;

        using var alterCommand = connection.CreateCommand();
        alterCommand.CommandText = $"ALTER TABLE {tableName} ADD COLUMN {columnName} {columnType}";
        alterCommand.ExecuteNonQuery();
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
    {
        using var checkCommand = connection.CreateCommand();
        checkCommand.CommandText = $"PRAGMA table_info({tableName})";
        using var reader = checkCommand.ExecuteReader();
        while (reader.Read())
        {
            // PRAGMA table_info 결과의 두 번째 컬럼(인덱스 1)이 컬럼 이름이다.
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// 옛 버전의 ChannelSkuTable(기본키 ChannelCode+Msku)을 새 스키마(기본키 ChannelCode+CskuCode)로
    /// 옮긴다. SQLite는 기존 테이블의 기본키를 ALTER로 바꿀 수 없어 이름 변경 → 새 테이블 생성 →
    /// 데이터 복사(CskuCode는 옛 Msku 값을 그대로 사용) → 옛 테이블 삭제 순으로 처리한다.
    /// 신규 DB(테이블이 아예 없음)거나 이미 마이그레이션된 DB는 아무 일도 하지 않는다.
    /// </summary>
    private static void MigrateChannelSkuTableToCskuCodeIfNeeded(SqliteConnection connection)
    {
        using var checkExistsCommand = connection.CreateCommand();
        checkExistsCommand.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name='ChannelSkuTable'";
        if (checkExistsCommand.ExecuteScalar() == null) return;

        if (HasColumn(connection, "ChannelSkuTable", "CskuCode")) return;

        var hasInvoiceDisplayName = HasColumn(connection, "ChannelSkuTable", "InvoiceDisplayName");

        using var renameCommand = connection.CreateCommand();
        renameCommand.CommandText = "ALTER TABLE ChannelSkuTable RENAME TO ChannelSkuTable_Legacy";
        renameCommand.ExecuteNonQuery();

        using var createCommand = connection.CreateCommand();
        createCommand.CommandText = """
            CREATE TABLE ChannelSkuTable (
                ChannelCode TEXT NOT NULL,
                CskuCode TEXT NOT NULL,
                Msku TEXT NOT NULL,
                SupplyPrice REAL NOT NULL,
                InvoiceDisplayName TEXT,
                PRIMARY KEY (ChannelCode, CskuCode)
            )
            """;
        createCommand.ExecuteNonQuery();

        using var copyCommand = connection.CreateCommand();
        copyCommand.CommandText = hasInvoiceDisplayName
            ? "INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice, InvoiceDisplayName) SELECT ChannelCode, Msku, Msku, SupplyPrice, InvoiceDisplayName FROM ChannelSkuTable_Legacy"
            : "INSERT INTO ChannelSkuTable (ChannelCode, CskuCode, Msku, SupplyPrice) SELECT ChannelCode, Msku, Msku, SupplyPrice FROM ChannelSkuTable_Legacy";
        copyCommand.ExecuteNonQuery();

        using var dropCommand = connection.CreateCommand();
        dropCommand.CommandText = "DROP TABLE ChannelSkuTable_Legacy";
        dropCommand.ExecuteNonQuery();
    }
}
