-- Electronic prescriptions from ClinicHub: each drug keeps its id in the sending system, so a Bundle sent again is
-- recognised and a cancellation finds the drug. rx.Prescription.ExternalId (the group identifier) already exists.
-- Written to be run again safely.

IF COL_LENGTH('rx.PrescriptionItem', 'ExternalId') IS NULL
    ALTER TABLE rx.PrescriptionItem ADD ExternalId varchar(150) NULL;
GO

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_PrescriptionItem_External' AND object_id = OBJECT_ID('rx.PrescriptionItem'))
    CREATE UNIQUE INDEX UX_PrescriptionItem_External ON rx.PrescriptionItem (ExternalId) WHERE ExternalId IS NOT NULL;
GO

-- The group identifier is "{system}|{value}", longer than the first guess.
IF EXISTS (SELECT 1 FROM sys.columns WHERE object_id = OBJECT_ID('rx.Prescription') AND name = 'ExternalId' AND max_length < 150)
BEGIN
    DROP INDEX UX_Prescription_External ON rx.Prescription;
    ALTER TABLE rx.Prescription ALTER COLUMN ExternalId varchar(150) NULL;
    CREATE UNIQUE INDEX UX_Prescription_External ON rx.Prescription (ExternalId) WHERE ExternalId IS NOT NULL;
END
GO
