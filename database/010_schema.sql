-- PharmaFlow: a community pharmacy for an Ontario clinic group, the Ontario version of the legacy "Drugstore"
-- hospital pharmacy (see docs/drugstore-to-ontario.md). Schemas:
--   ref   base definitions: dosage forms, routes, units, NAPRA / controlled-substance schedules, SIG codes,
--         ingredients and their interactions, manufacturers, suppliers, payer plans; the drug catalog (DIN)
--   core  patients (with allergies) and prescribers
--   rx    prescriptions as received (reception), one row per drug ordered
--   audit who opened which patient (PHIPA)

CREATE SCHEMA ref;
GO
CREATE SCHEMA core;
GO
CREATE SCHEMA rx;
GO
CREATE SCHEMA audit;
GO

-- ---------------------------------------------------------------- base definitions

CREATE TABLE ref.DosageForm (
    Code      varchar(10)  NOT NULL CONSTRAINT PK_DosageForm PRIMARY KEY,
    Name      nvarchar(60) NOT NULL,
    NameFr    nvarchar(60) NULL,
    IsActive  bit          NOT NULL CONSTRAINT DF_DosageForm_Active DEFAULT 1
);

CREATE TABLE ref.Route (
    Code      varchar(10)  NOT NULL CONSTRAINT PK_Route PRIMARY KEY,
    Name      nvarchar(60) NOT NULL,
    NameFr    nvarchar(60) NULL,
    IsActive  bit          NOT NULL CONSTRAINT DF_Route_Active DEFAULT 1
);

-- Strength units (mg, mcg, mg/mL) and dispensing units (tablet, mL, inhaler).
CREATE TABLE ref.Unit (
    Code      varchar(10)  NOT NULL CONSTRAINT PK_Unit PRIMARY KEY,
    Name      nvarchar(40) NOT NULL,
    Kind      varchar(10)  NOT NULL CONSTRAINT CK_Unit_Kind CHECK (Kind IN ('Strength','Dispensing')),
    IsActive  bit          NOT NULL CONSTRAINT DF_Unit_Active DEFAULT 1
);

-- NAPRA's national drug schedules and the Controlled Drugs and Substances Act classes.
CREATE TABLE ref.Schedule (
    Code                 varchar(4)    NOT NULL CONSTRAINT PK_Schedule PRIMARY KEY,
    Name                 nvarchar(80)  NOT NULL,
    RequiresPrescription bit           NOT NULL,
    -- Recorded in Ontario's Narcotics Monitoring System when dispensed.
    Monitored            bit           NOT NULL CONSTRAINT DF_Schedule_Monitored DEFAULT 0,
    Description          nvarchar(300) NULL
);

-- Abbreviations prescribers write ("1 TAB PO BID") and what goes on the label.
CREATE TABLE ref.SigCode (
    Code        varchar(12)   NOT NULL CONSTRAINT PK_SigCode PRIMARY KEY,
    Expansion   nvarchar(80)  NOT NULL,
    ExpansionFr nvarchar(80)  NULL,
    -- For frequencies: how many times a day, to work out days' supply.
    DosesPerDay decimal(5,2)  NULL,
    IsActive    bit           NOT NULL CONSTRAINT DF_SigCode_Active DEFAULT 1
);

-- The active ingredient (generic name): allergies and interactions are checked at this level, not per product.
CREATE TABLE ref.Ingredient (
    IngredientId  int IDENTITY(1,1) CONSTRAINT PK_Ingredient PRIMARY KEY,
    Name          nvarchar(100) NOT NULL CONSTRAINT UQ_Ingredient_Name UNIQUE,
    AtcCode       varchar(8)    NULL,
    -- A family an allergy can name: "Penicillins", "Sulfonamides", "NSAIDs", "Opioids".
    AllergyClass  nvarchar(40)  NULL,
    IsActive      bit           NOT NULL CONSTRAINT DF_Ingredient_Active DEFAULT 1
);

CREATE TABLE ref.Interaction (
    IngredientAId int NOT NULL CONSTRAINT FK_Interaction_A REFERENCES ref.Ingredient(IngredientId),
    IngredientBId int NOT NULL CONSTRAINT FK_Interaction_B REFERENCES ref.Ingredient(IngredientId),
    Severity      varchar(10)   NOT NULL CONSTRAINT CK_Interaction_Severity CHECK (Severity IN ('Major','Moderate','Minor')),
    Description   nvarchar(400) NOT NULL,
    CONSTRAINT PK_Interaction PRIMARY KEY (IngredientAId, IngredientBId),
    CONSTRAINT CK_Interaction_Order CHECK (IngredientAId < IngredientBId)
);

CREATE TABLE ref.Manufacturer (
    ManufacturerId int IDENTITY(1,1) CONSTRAINT PK_Manufacturer PRIMARY KEY,
    Name           nvarchar(120) NOT NULL CONSTRAINT UQ_Manufacturer_Name UNIQUE,
    Country        char(2)       NULL,
    IsActive       bit           NOT NULL CONSTRAINT DF_Manufacturer_Active DEFAULT 1
);

CREATE TABLE ref.Supplier (
    SupplierId    int IDENTITY(1,1) CONSTRAINT PK_Supplier PRIMARY KEY,
    Code          varchar(20)   NOT NULL CONSTRAINT UQ_Supplier_Code UNIQUE,
    Name          nvarchar(120) NOT NULL,
    Phone         varchar(25)   NULL,
    Email         nvarchar(120) NULL,
    AccountNumber varchar(30)   NULL,
    IsActive      bit           NOT NULL CONSTRAINT DF_Supplier_Active DEFAULT 1
);

-- Who pays: the Ontario Drug Benefit program, a private drug plan, or the patient (cash). The amounts are the
-- pharmacy's settings, to be confirmed against the plan's agreement.
CREATE TABLE ref.[Plan] (
    PlanId           int IDENTITY(1,1) CONSTRAINT PK_Plan PRIMARY KEY,
    Code             varchar(20)   NOT NULL CONSTRAINT UQ_Plan_Code UNIQUE,
    Name             nvarchar(120) NOT NULL,
    PayerType        varchar(10)   NOT NULL CONSTRAINT CK_Plan_Payer CHECK (PayerType IN ('ODB','Insurer','Cash','Other')),
    CopayAmount      decimal(8,2)  NOT NULL CONSTRAINT DF_Plan_Copay DEFAULT 0,
    CopayPercent     decimal(5,2)  NOT NULL CONSTRAINT DF_Plan_CopayPct DEFAULT 0,
    DispensingFee    decimal(8,2)  NOT NULL CONSTRAINT DF_Plan_Fee DEFAULT 0,
    MarkupPercent    decimal(5,2)  NOT NULL CONSTRAINT DF_Plan_Markup DEFAULT 0,
    Notes            nvarchar(300) NULL,
    IsActive         bit           NOT NULL CONSTRAINT DF_Plan_Active DEFAULT 1,
    CONSTRAINT CK_Plan_Amounts CHECK (CopayAmount >= 0 AND CopayPercent BETWEEN 0 AND 100 AND DispensingFee >= 0 AND MarkupPercent BETWEEN 0 AND 100)
);

-- One product: a DIN (Health Canada's Drug Identification Number, 8 digits) for an ingredient, strength and form.
CREATE TABLE ref.Drug (
    DrugId            int IDENTITY(1,1) CONSTRAINT PK_Drug PRIMARY KEY,
    Din               char(8)        NOT NULL CONSTRAINT UQ_Drug_Din UNIQUE CONSTRAINT CK_Drug_Din CHECK (Din NOT LIKE '%[^0-9]%'),
    BrandName         nvarchar(100)  NOT NULL,
    IngredientId      int            NOT NULL CONSTRAINT FK_Drug_Ingredient REFERENCES ref.Ingredient(IngredientId),
    Strength          decimal(10,3)  NULL,
    StrengthUnit      varchar(10)    NULL CONSTRAINT FK_Drug_StrengthUnit REFERENCES ref.Unit(Code),
    FormCode          varchar(10)    NOT NULL CONSTRAINT FK_Drug_Form REFERENCES ref.DosageForm(Code),
    RouteCode         varchar(10)    NOT NULL CONSTRAINT FK_Drug_Route REFERENCES ref.Route(Code),
    ScheduleCode      varchar(4)     NOT NULL CONSTRAINT FK_Drug_Schedule REFERENCES ref.Schedule(Code),
    ManufacturerId    int            NULL CONSTRAINT FK_Drug_Manufacturer REFERENCES ref.Manufacturer(ManufacturerId),
    PackSize          int            NOT NULL CONSTRAINT DF_Drug_Pack DEFAULT 100 CONSTRAINT CK_Drug_Pack CHECK (PackSize > 0),
    DispensingUnit    varchar(10)    NOT NULL CONSTRAINT FK_Drug_DispensingUnit REFERENCES ref.Unit(Code),
    -- Acquisition cost per dispensing unit (tablet, mL), what the pharmacy pays.
    UnitCost          decimal(10,4)  NOT NULL CONSTRAINT DF_Drug_Cost DEFAULT 0 CONSTRAINT CK_Drug_Cost CHECK (UnitCost >= 0),
    -- Listed on the Ontario Drug Benefit Formulary; LU = a Limited Use code the prescriber must give.
    OdbBenefit        bit            NOT NULL CONSTRAINT DF_Drug_Odb DEFAULT 0,
    OdbLimitedUse     bit            NOT NULL CONSTRAINT DF_Drug_LU DEFAULT 0,
    -- Products in the same group are interchangeable (generic substitution) unless the prescriber wrote "no substitution".
    InterchangeGroup  varchar(20)    NULL,
    IsActive          bit            NOT NULL CONSTRAINT DF_Drug_Active DEFAULT 1,
    -- Demo catalog: made-up DINs and costs, never to be dispensed from.
    IsDemo            bit            NOT NULL CONSTRAINT DF_Drug_Demo DEFAULT 0,
    CreatedAt         datetime2(0)   NOT NULL CONSTRAINT DF_Drug_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAt         datetime2(0)   NOT NULL CONSTRAINT DF_Drug_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_Drug_Strength CHECK ((Strength IS NULL AND StrengthUnit IS NULL) OR (Strength > 0 AND StrengthUnit IS NOT NULL))
);
CREATE INDEX IX_Drug_Ingredient ON ref.Drug (IngredientId);
CREATE INDEX IX_Drug_Brand ON ref.Drug (BrandName);
GO

-- ---------------------------------------------------------------- core

CREATE TABLE core.Patient (
    PatientId         int IDENTITY(1,1) CONSTRAINT PK_Patient PRIMARY KEY,
    HealthCardNumber  char(10)       NULL CONSTRAINT CK_Patient_Hcn CHECK (HealthCardNumber IS NULL OR HealthCardNumber NOT LIKE '%[^0-9]%'),
    HealthCardVersion varchar(2)     NULL,
    FirstName         nvarchar(60)   NOT NULL,
    LastName          nvarchar(60)   NOT NULL,
    DateOfBirth       date           NOT NULL,
    Sex               char(1)        NOT NULL CONSTRAINT DF_Patient_Sex DEFAULT 'U' CONSTRAINT CK_Patient_Sex CHECK (Sex IN ('M','F','X','U')),
    Phone             varchar(25)    NULL,
    Email             nvarchar(120)  NULL,
    AddressLine1      nvarchar(200)  NULL,
    City              nvarchar(80)   NULL,
    PostalCode        varchar(7)     NULL,
    DefaultPlanId     int            NULL CONSTRAINT FK_Patient_Plan REFERENCES ref.[Plan](PlanId),
    PlanMemberId      varchar(30)    NULL,
    Notes             nvarchar(500)  NULL,
    CreatedAt         datetime2(0)   NOT NULL CONSTRAINT DF_Patient_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAt         datetime2(0)   NOT NULL CONSTRAINT DF_Patient_Updated DEFAULT SYSUTCDATETIME(),
    RowVer            rowversion
);
CREATE UNIQUE INDEX UX_Patient_Hcn ON core.Patient (HealthCardNumber) WHERE HealthCardNumber IS NOT NULL;
CREATE INDEX IX_Patient_Name ON core.Patient (LastName, FirstName, DateOfBirth);

-- An allergy names an ingredient, a family of them, or (when neither is known) just what the patient said.
CREATE TABLE core.PatientAllergy (
    AllergyId     int IDENTITY(1,1) CONSTRAINT PK_PatientAllergy PRIMARY KEY,
    PatientId     int            NOT NULL CONSTRAINT FK_PatientAllergy_Patient REFERENCES core.Patient(PatientId),
    IngredientId  int            NULL CONSTRAINT FK_PatientAllergy_Ingredient REFERENCES ref.Ingredient(IngredientId),
    AllergyClass  nvarchar(40)   NULL,
    Description   nvarchar(120)  NULL,
    Reaction      nvarchar(120)  NULL,
    Severity      varchar(10)    NOT NULL CONSTRAINT DF_PatientAllergy_Sev DEFAULT 'Unknown' CONSTRAINT CK_PatientAllergy_Sev CHECK (Severity IN ('Mild','Moderate','Severe','Unknown')),
    RecordedAt    datetime2(0)   NOT NULL CONSTRAINT DF_PatientAllergy_Recorded DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_PatientAllergy_What CHECK (IngredientId IS NOT NULL OR AllergyClass IS NOT NULL OR Description IS NOT NULL)
);
CREATE INDEX IX_PatientAllergy_Patient ON core.PatientAllergy (PatientId);

-- Who may prescribe: physicians (CPSO), nurse practitioners (CNO), dentists (RCDSO), pharmacists (OCP, for the
-- minor ailments Ontario pharmacists may prescribe for).
CREATE TABLE core.Prescriber (
    PrescriberId  int IDENTITY(1,1) CONSTRAINT PK_Prescriber PRIMARY KEY,
    LicenceBody   varchar(6)     NOT NULL CONSTRAINT CK_Prescriber_Body CHECK (LicenceBody IN ('CPSO','CNO','RCDSO','OCP')),
    LicenceNumber varchar(10)    NOT NULL,
    FirstName     nvarchar(60)   NOT NULL,
    LastName      nvarchar(60)   NOT NULL,
    Phone         varchar(25)    NULL,
    Fax           varchar(25)    NULL,
    Clinic        nvarchar(120)  NULL,
    IsActive      bit            NOT NULL CONSTRAINT DF_Prescriber_Active DEFAULT 1,
    CONSTRAINT UQ_Prescriber_Licence UNIQUE (LicenceBody, LicenceNumber)
);
GO

-- ---------------------------------------------------------------- rx: reception

CREATE SEQUENCE rx.RxNumber AS int START WITH 100001;

CREATE TABLE rx.Prescription (
    PrescriptionId  int IDENTITY(1,1) CONSTRAINT PK_Prescription PRIMARY KEY,
    RxNumber        varchar(12)    NOT NULL CONSTRAINT UQ_Prescription_RxNumber UNIQUE,
    PatientId       int            NOT NULL CONSTRAINT FK_Prescription_Patient REFERENCES core.Patient(PatientId),
    PrescriberId    int            NOT NULL CONSTRAINT FK_Prescription_Prescriber REFERENCES core.Prescriber(PrescriberId),
    WrittenDate     date           NOT NULL,
    ReceivedAt      datetime2(0)   NOT NULL,
    Channel         varchar(6)     NOT NULL CONSTRAINT CK_Prescription_Channel CHECK (Channel IN ('Paper','Fax','Phone','eRx')),
    PlanId          int            NULL CONSTRAINT FK_Prescription_Plan REFERENCES ref.[Plan](PlanId),
    [Status]        varchar(10)    NOT NULL CONSTRAINT CK_Prescription_Status CHECK ([Status] IN ('Received','InProgress','Completed','OnHold','Cancelled')),
    Notes           nvarchar(500)  NULL,
    -- An electronic prescription's id in the sending system (ClinicHub), so a resent one is recognised.
    ExternalId      varchar(100)   NULL,
    ReceivedBy      nvarchar(100)  NOT NULL,
    UpdatedAt       datetime2(0)   NOT NULL,
    RowVer          rowversion
);
CREATE UNIQUE INDEX UX_Prescription_External ON rx.Prescription (ExternalId) WHERE ExternalId IS NOT NULL;
CREATE INDEX IX_Prescription_Patient ON rx.Prescription (PatientId, ReceivedAt);
CREATE INDEX IX_Prescription_Status ON rx.Prescription ([Status], ReceivedAt);

CREATE TABLE rx.PrescriptionItem (
    ItemId             int IDENTITY(1,1) CONSTRAINT PK_PrescriptionItem PRIMARY KEY,
    PrescriptionId     int            NOT NULL CONSTRAINT FK_PrescriptionItem_Prescription REFERENCES rx.Prescription(PrescriptionId),
    LineNumber         tinyint        NOT NULL,
    DrugId             int            NULL CONSTRAINT FK_PrescriptionItem_Drug REFERENCES ref.Drug(DrugId),
    -- What the prescriber wrote, kept even when matched to a product.
    DrugText           nvarchar(200)  NOT NULL,
    Sig                nvarchar(300)  NOT NULL,
    SigExpanded        nvarchar(500)  NOT NULL,
    Quantity           decimal(10,2)  NOT NULL CONSTRAINT CK_PrescriptionItem_Qty CHECK (Quantity > 0),
    DaysSupply         smallint       NULL CONSTRAINT CK_PrescriptionItem_Days CHECK (DaysSupply IS NULL OR DaysSupply BETWEEN 1 AND 365),
    RefillsAuthorized  tinyint        NOT NULL CONSTRAINT DF_PrescriptionItem_Refills DEFAULT 0,
    RefillsRemaining   tinyint        NOT NULL CONSTRAINT DF_PrescriptionItem_Remaining DEFAULT 0,
    NoSubstitution     bit            NOT NULL CONSTRAINT DF_PrescriptionItem_NoSub DEFAULT 0,
    [Status]           varchar(12)    NOT NULL CONSTRAINT CK_PrescriptionItem_Status CHECK ([Status] IN ('Received','ReadyToFill','Filled','Verified','Dispensed','OnHold','Cancelled')),
    -- What reception found (allergy, interaction, duplicate therapy, monitored drug), as JSON, and who reviewed it.
    WarningsJson       nvarchar(max)  NOT NULL CONSTRAINT DF_PrescriptionItem_Warnings DEFAULT '[]',
    WarningsReviewedBy nvarchar(100)  NULL,
    WarningsReviewedAt datetime2(0)   NULL,
    CancelReason       nvarchar(200)  NULL,
    CONSTRAINT UQ_PrescriptionItem UNIQUE (PrescriptionId, LineNumber),
    CONSTRAINT CK_PrescriptionItem_Refills CHECK (RefillsRemaining <= RefillsAuthorized),
    CONSTRAINT CK_PrescriptionItem_Ready CHECK ([Status] IN ('Received','OnHold','Cancelled') OR DrugId IS NOT NULL)
);
CREATE INDEX IX_PrescriptionItem_Drug ON rx.PrescriptionItem (DrugId);
GO

-- ---------------------------------------------------------------- audit

CREATE TABLE audit.AccessLog (
    AuditId     bigint IDENTITY(1,1) CONSTRAINT PK_AccessLog PRIMARY KEY,
    OccurredAt  datetime2(3)  NOT NULL CONSTRAINT DF_AccessLog_At DEFAULT SYSUTCDATETIME(),
    UserName    nvarchar(100) NOT NULL,
    Action      varchar(10)   NOT NULL CONSTRAINT CK_AccessLog_Action CHECK (Action IN ('View','Create','Update','Print')),
    PatientId   int           NULL,
    Context     nvarchar(120) NOT NULL
);
CREATE INDEX IX_AccessLog_Patient ON audit.AccessLog (PatientId, OccurredAt);
GO
