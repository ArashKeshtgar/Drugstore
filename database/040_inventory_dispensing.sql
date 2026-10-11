-- Purchasing and stock by lot and expiry, dispensing with the plan's price split, and what the accounting has been sent.
--   inv      purchases from suppliers, stock lots
--   rx       dispenses (each fill or refill) and the lots they came from
--   billing  financial documents FinancePowerApp has accepted

CREATE SCHEMA inv;
GO
CREATE SCHEMA billing;
GO

CREATE TABLE inv.Purchase (
    PurchaseId     int IDENTITY(1,1) CONSTRAINT PK_Purchase PRIMARY KEY,
    SupplierId     int            NOT NULL CONSTRAINT FK_Purchase_Supplier REFERENCES ref.Supplier(SupplierId),
    InvoiceNumber  varchar(40)    NOT NULL,
    InvoiceDate    date           NOT NULL,
    ReceivedAt     datetime2(0)   NOT NULL,
    -- Drugs are zero-rated; other supplies on the same invoice carry HST.
    DrugsAmount    decimal(12,2)  NOT NULL CONSTRAINT CK_Purchase_Drugs CHECK (DrugsAmount >= 0),
    SuppliesAmount decimal(12,2)  NOT NULL CONSTRAINT DF_Purchase_Supplies DEFAULT 0 CONSTRAINT CK_Purchase_Supplies CHECK (SuppliesAmount >= 0),
    SuppliesHst    decimal(12,2)  NOT NULL CONSTRAINT DF_Purchase_Hst DEFAULT 0 CONSTRAINT CK_Purchase_Hst CHECK (SuppliesHst >= 0),
    ReceivedBy     nvarchar(100)  NOT NULL,
    CONSTRAINT UQ_Purchase_Invoice UNIQUE (SupplierId, InvoiceNumber)
);

CREATE TABLE inv.StockLot (
    LotId             int IDENTITY(1,1) CONSTRAINT PK_StockLot PRIMARY KEY,
    DrugId            int            NOT NULL CONSTRAINT FK_StockLot_Drug REFERENCES ref.Drug(DrugId),
    LotNumber         varchar(30)    NOT NULL,
    ExpiryDate        date           NOT NULL,
    QuantityReceived  decimal(12,2)  NOT NULL CONSTRAINT CK_StockLot_Received CHECK (QuantityReceived > 0),
    QuantityOnHand    decimal(12,2)  NOT NULL,
    UnitCost          decimal(10,4)  NOT NULL CONSTRAINT CK_StockLot_Cost CHECK (UnitCost >= 0),
    PurchaseId        int            NULL CONSTRAINT FK_StockLot_Purchase REFERENCES inv.Purchase(PurchaseId),
    ReceivedAt        datetime2(0)   NOT NULL,
    CONSTRAINT CK_StockLot_OnHand CHECK (QuantityOnHand >= 0 AND QuantityOnHand <= QuantityReceived)
);
CREATE INDEX IX_StockLot_Drug ON inv.StockLot (DrugId, ExpiryDate) INCLUDE (QuantityOnHand);

CREATE TABLE rx.Dispense (
    DispenseId     int IDENTITY(1,1) CONSTRAINT PK_Dispense PRIMARY KEY,
    ItemId         int            NOT NULL CONSTRAINT FK_Dispense_Item REFERENCES rx.PrescriptionItem(ItemId),
    DrugId         int            NOT NULL CONSTRAINT FK_Dispense_Drug REFERENCES ref.Drug(DrugId),
    IsRefill       bit            NOT NULL,
    Quantity       decimal(10,2)  NOT NULL CONSTRAINT CK_Dispense_Qty CHECK (Quantity > 0),
    DispensedAt    datetime2(0)   NOT NULL,
    DispensedBy    nvarchar(100)  NOT NULL,
    PlanId         int            NULL CONSTRAINT FK_Dispense_Plan REFERENCES ref.[Plan](PlanId),
    -- The price: acquisition cost of the lots used, the plan's mark-up and dispensing fee.
    DrugCost       decimal(10,2)  NOT NULL,
    Markup         decimal(10,2)  NOT NULL,
    DispensingFee  decimal(10,2)  NOT NULL,
    Total          decimal(10,2)  NOT NULL,
    PlanPays       decimal(10,2)  NOT NULL,
    PatientPays    decimal(10,2)  NOT NULL,
    PaymentMethod  varchar(10)    NULL CONSTRAINT CK_Dispense_Method CHECK (PaymentMethod IN ('Cash','Debit','Credit','Account')),
    CONSTRAINT CK_Dispense_Price CHECK (Total = DrugCost + Markup + DispensingFee AND PlanPays + PatientPays = Total AND PlanPays >= 0 AND PatientPays >= 0),
    CONSTRAINT CK_Dispense_Paid CHECK (PatientPays = 0 OR PaymentMethod IS NOT NULL)
);
CREATE INDEX IX_Dispense_Item ON rx.Dispense (ItemId);

CREATE TABLE rx.DispenseLot (
    DispenseId  int            NOT NULL CONSTRAINT FK_DispenseLot_Dispense REFERENCES rx.Dispense(DispenseId),
    LotId       int            NOT NULL CONSTRAINT FK_DispenseLot_Lot REFERENCES inv.StockLot(LotId),
    Quantity    decimal(10,2)  NOT NULL CONSTRAINT CK_DispenseLot_Qty CHECK (Quantity > 0),
    UnitCost    decimal(10,4)  NOT NULL,
    CONSTRAINT PK_DispenseLot PRIMARY KEY (DispenseId, LotId)
);

CREATE TABLE billing.FinanceExport (
    ExternalId  varchar(200) NOT NULL CONSTRAINT PK_FinanceExport PRIMARY KEY,
    SentAt      datetime2(0) NOT NULL CONSTRAINT DF_FinanceExport_SentAt DEFAULT SYSUTCDATETIME()
);
GO
