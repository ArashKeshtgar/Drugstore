-- From ClinicHub with each electronic prescription (IntegrationHub's flows 6 and 7): the clinic's MRN of the patient, so
-- that a merge at the clinic can be followed here, and the prescription's confidentiality label (a consent directive:
-- R restricted, V very restricted). Written to be run again safely.

IF COL_LENGTH('rx.Prescription', 'SourceMrn') IS NULL
    ALTER TABLE rx.Prescription ADD SourceMrn varchar(60) NULL;
GO

IF COL_LENGTH('rx.Prescription', 'Confidentiality') IS NULL
    ALTER TABLE rx.Prescription ADD Confidentiality char(1) NULL
        CONSTRAINT CK_Prescription_Confidentiality CHECK (Confidentiality IN ('R', 'V'));
GO
