/*
  MS SQL (T-SQL) equivalent of the container production-items table built in Demo.aspx.cs
  (GenerateProductionItemsTable / LoadContainer).

  Columns match the UI:
    Auftragsnummer, Lieferscheinnummer, Kommissionsnummer, Kommissionsname,
    Artikelcode, Artikelname, Menge, Gewicht, Volumen, Anzahl Collis

  Assumptions (adjust names to your real schema):
    - dbo.ProductionItemShippingContainer  : container header (Id, Sendungsnummer, Status, ...)
    - dbo.ProductionItem                   : lines linked via ContainerId
    - dbo.ProductionItemScan               : scanned barcode strings per production item
    - SAP OITM (or a view)                 : ItemCode, WeightForContainer, Volume*, U_Modell*
    - Preliminary delivery-note mapping    : SalesOrderNumber -> PreliminaryDeliveryNoteNumber

  Pillow/pad detection in C# also uses MattressModelRecognition.GetProductType(ArticleName).
  That heuristic is only partially mirrored below via U_Modell / name patterns.
*/

DECLARE @ContainerId INT = /* container Id */ 0;

;WITH SapItems AS (
    SELECT
        T0.ItemCode,
        CAST(T0.WeightForContainer AS DECIMAL(18, 6)) AS WeightForContainer,
        CAST(T0.VolumeForContainer AS DECIMAL(18, 6)) AS VolumeForContainer,
        CAST(T0.VolumeRolledForContainer AS DECIMAL(18, 6)) AS VolumeRolledForContainer,
        ISNULL(T0.U_Modell, N'') AS U_Modell,
        ISNULL(T0.U_Modell2, N'') AS U_Modell2
    FROM OITM T0
),
DeliveryNotes AS (
    -- Replace with your real preliminary delivery-note source
    SELECT
        DN.SalesOrderNumber,
        DN.PreliminaryDeliveryNoteNumber
    FROM dbo.SapPreliminaryDeliveryNote DN
),
ColliCounts AS (
    SELECT
        S.ProductionItemId,
        SUM(
            CASE
                WHEN S.ScannedBarcodes IS NULL OR S.ScannedBarcodes = N'' THEN 0
                ELSE LEN(S.ScannedBarcodes) - LEN(REPLACE(S.ScannedBarcodes, N'.', N''))
            END
        ) AS ColliNumber
    FROM dbo.ProductionItemScan S
    GROUP BY S.ProductionItemId
),
LineCalc AS (
    SELECT
        PI.SalesOrderNumber,
        DN.PreliminaryDeliveryNoteNumber,
        PI.CommissionNumber,
        PI.CommissionName,
        PI.ArticleCode,
        PI.ArticleName,
        PI.ArticleQuantity,
        PI.Pallet,
        CASE
            WHEN LOWER(SI.U_Modell) LIKE N'%schulter%'
              OR LOWER(SI.U_Modell) LIKE N'%kopf%'
              OR LOWER(SI.U_Modell2) LIKE N'%schulter%'
              OR LOWER(SI.U_Modell2) LIKE N'%kopf%'
              OR LOWER(PI.ArticleName) LIKE N'%kissen%'
              OR LOWER(PI.ArticleName) LIKE N'%auflage%'
            THEN 1
            ELSE 0
        END AS IsPillowOrPad,
        CAST(ISNULL(SI.WeightForContainer, 0) * PI.ArticleQuantity / 1000.0 AS DECIMAL(18, 2)) AS WeightTotal,
        CASE
            WHEN PI.Pallet IS NOT NULL AND PI.Pallet LIKE N'%Roll%'
                THEN ISNULL(SI.VolumeRolledForContainer, 0)
            ELSE ISNULL(SI.VolumeForContainer, 0)
        END AS VolumePerItem,
        ISNULL(CC.ColliNumber, 0) AS ColliNumberRaw
    FROM dbo.ProductionItem PI
    LEFT JOIN SapItems SI
        ON SI.ItemCode = PI.ArticleCode
    LEFT JOIN DeliveryNotes DN
        ON DN.SalesOrderNumber = PI.SalesOrderNumber
    LEFT JOIN ColliCounts CC
        ON CC.ProductionItemId = PI.Id
    WHERE PI.ContainerId = @ContainerId
)
SELECT
    LC.SalesOrderNumber AS Auftragsnummer,
    LC.PreliminaryDeliveryNoteNumber AS Lieferscheinnummer,
    LC.CommissionNumber AS Kommissionsnummer,
    LC.CommissionName AS Kommissionsname,
    LC.ArticleCode AS Artikelcode,
    LC.ArticleName AS Artikelname,
    LC.ArticleQuantity AS Menge,
    LC.WeightTotal AS Gewicht,
    CONCAT(
        FORMAT(
            CASE
                WHEN LC.IsPillowOrPad = 1 THEN 0
                ELSE LC.VolumePerItem * LC.ArticleQuantity / 1000000.0
            END,
            'N2'
        ),
        CASE
            WHEN LC.Pallet IS NOT NULL AND LC.Pallet LIKE N'%Roll%' THEN N'R'
            ELSE N''
        END
    ) AS Volumen, -- matches C# ToString("F2") + optional "R"
    CASE
        WHEN LC.IsPillowOrPad = 1 THEN 0
        ELSE LC.ColliNumberRaw
    END AS [Anzahl Collis]
FROM LineCalc LC
ORDER BY LC.SalesOrderNumber DESC;

-- Optional summary row (same totals as the C# summaryRow)
;WITH SapItems AS (
    SELECT
        T0.ItemCode,
        CAST(T0.WeightForContainer AS DECIMAL(18, 6)) AS WeightForContainer,
        CAST(T0.VolumeForContainer AS DECIMAL(18, 6)) AS VolumeForContainer,
        CAST(T0.VolumeRolledForContainer AS DECIMAL(18, 6)) AS VolumeRolledForContainer,
        ISNULL(T0.U_Modell, N'') AS U_Modell,
        ISNULL(T0.U_Modell2, N'') AS U_Modell2
    FROM OITM T0
),
ColliCounts AS (
    SELECT
        S.ProductionItemId,
        SUM(
            CASE
                WHEN S.ScannedBarcodes IS NULL OR S.ScannedBarcodes = N'' THEN 0
                ELSE LEN(S.ScannedBarcodes) - LEN(REPLACE(S.ScannedBarcodes, N'.', N''))
            END
        ) AS ColliNumber
    FROM dbo.ProductionItemScan S
    GROUP BY S.ProductionItemId
),
LineCalc AS (
    SELECT
        PI.ArticleQuantity,
        CASE
            WHEN LOWER(SI.U_Modell) LIKE N'%schulter%'
              OR LOWER(SI.U_Modell) LIKE N'%kopf%'
              OR LOWER(SI.U_Modell2) LIKE N'%schulter%'
              OR LOWER(SI.U_Modell2) LIKE N'%kopf%'
              OR LOWER(PI.ArticleName) LIKE N'%kissen%'
              OR LOWER(PI.ArticleName) LIKE N'%auflage%'
            THEN 1
            ELSE 0
        END AS IsPillowOrPad,
        CAST(ISNULL(SI.WeightForContainer, 0) * PI.ArticleQuantity / 1000.0 AS DECIMAL(18, 2)) AS WeightTotal,
        CASE
            WHEN PI.Pallet IS NOT NULL AND PI.Pallet LIKE N'%Roll%'
                THEN ISNULL(SI.VolumeRolledForContainer, 0)
            ELSE ISNULL(SI.VolumeForContainer, 0)
        END AS VolumePerItem,
        ISNULL(CC.ColliNumber, 0) AS ColliNumberRaw
    FROM dbo.ProductionItem PI
    LEFT JOIN SapItems SI
        ON SI.ItemCode = PI.ArticleCode
    LEFT JOIN ColliCounts CC
        ON CC.ProductionItemId = PI.Id
    WHERE PI.ContainerId = @ContainerId
)
SELECT
    SUM(LC.ArticleQuantity) AS MengeTotal,
    SUM(LC.WeightTotal) AS GewichtTotal,
    SUM(
        CASE
            WHEN LC.IsPillowOrPad = 1 THEN 0
            ELSE CAST(LC.VolumePerItem * LC.ArticleQuantity / 1000000.0 AS DECIMAL(18, 2))
        END
    ) AS VolumenTotal,
    SUM(
        CASE
            WHEN LC.IsPillowOrPad = 1 THEN 0
            ELSE LC.ColliNumberRaw
        END
    ) AS CollisTotal
FROM LineCalc LC;
