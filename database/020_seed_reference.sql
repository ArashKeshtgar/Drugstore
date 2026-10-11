-- Base definitions every Ontario pharmacy uses: dosage forms, routes, units, schedules, SIG codes, and the payer
-- plans. Plan amounts are settings to confirm against the current ODB agreement and each insurer's contract.

SET NOCOUNT ON;

INSERT ref.DosageForm (Code, Name, NameFr) VALUES
('TAB', N'Tablet', N'Comprimé'), ('TABER', N'Tablet, extended release', N'Comprimé à libération prolongée'), ('CAP', N'Capsule', N'Capsule'),
('SOL', N'Oral solution', N'Solution orale'), ('SUSP', N'Oral suspension', N'Suspension orale'), ('INJ', N'Injection', N'Injection'),
('INH', N'Inhaler (metered dose)', N'Aérosol-doseur'), ('CRM', N'Cream', N'Crème'), ('OINT', N'Ointment', N'Onguent'),
('GTT', N'Drops', N'Gouttes'), ('PATCH', N'Transdermal patch', N'Timbre transdermique'), ('SUPP', N'Suppository', N'Suppositoire'),
('SPRAY', N'Nasal spray', N'Vaporisateur nasal'), ('PEN', N'Prefilled pen', N'Stylo prérempli');

INSERT ref.Route (Code, Name, NameFr) VALUES
('PO', N'By mouth', N'Par la bouche'), ('SL', N'Under the tongue', N'Sublingual'), ('TOP', N'On the skin', N'Topique'),
('INH', N'Inhaled', N'Inhalé'), ('IM', N'Into a muscle', N'Intramusculaire'), ('SC', N'Under the skin', N'Sous-cutané'),
('IV', N'Into a vein', N'Intraveineux'), ('OPH', N'In the eye', N'Ophtalmique'), ('OTIC', N'In the ear', N'Otique'),
('NAS', N'In the nose', N'Nasal'), ('PR', N'Rectal', N'Rectal'), ('TD', N'Through the skin (patch)', N'Transdermique');

INSERT ref.Unit (Code, Name, Kind) VALUES
('MG', N'mg', 'Strength'), ('MCG', N'mcg', 'Strength'), ('G', N'g', 'Strength'), ('MGML', N'mg/mL', 'Strength'),
('MCGH', N'mcg/hour', 'Strength'), ('UML', N'units/mL', 'Strength'), ('PCT', N'%', 'Strength'), ('MCGD', N'mcg/dose', 'Strength'),
('TAB', N'tablet', 'Dispensing'), ('CAP', N'capsule', 'Dispensing'), ('ML', N'mL', 'Dispensing'), ('GM', N'g', 'Dispensing'),
('EA', N'each', 'Dispensing'), ('INHLR', N'inhaler', 'Dispensing'), ('PATCH', N'patch', 'Dispensing'), ('PEN', N'pen', 'Dispensing');

INSERT ref.Schedule (Code, Name, RequiresPrescription, Monitored, Description) VALUES
('I',  N'Schedule I (prescription)',             1, 0, N'NAPRA Schedule I: dispensed only on a prescription.'),
('II', N'Schedule II (behind the counter)',      0, 0, N'Sold by a pharmacist, no prescription; kept where the public can''t reach it.'),
('III',N'Schedule III (pharmacy only)',          0, 0, N'Sold in the pharmacy''s self-selection area.'),
('U',  N'Unscheduled',                           0, 0, N'Sold anywhere.'),
('N',  N'Narcotic',                              1, 1, N'Controlled Drugs and Substances Act narcotic. Recorded in the Narcotics Monitoring System.'),
('C',  N'Controlled drug',                       1, 1, N'CDSA controlled drug (e.g. stimulants). Recorded in the Narcotics Monitoring System.'),
('T',  N'Targeted substance (benzodiazepine)',   1, 1, N'CDSA targeted substance. Recorded in the Narcotics Monitoring System.');

INSERT ref.SigCode (Code, Expansion, ExpansionFr, DosesPerDay) VALUES
('QD', N'once daily', N'une fois par jour', 1), ('OD', N'once daily', N'une fois par jour', 1), ('DAILY', N'once daily', N'une fois par jour', 1),
('BID', N'twice daily', N'deux fois par jour', 2), ('TID', N'three times daily', N'trois fois par jour', 3), ('QID', N'four times daily', N'quatre fois par jour', 4),
('QHS', N'at bedtime', N'au coucher', 1), ('QAM', N'every morning', N'chaque matin', 1),
('Q4H', N'every 4 hours', N'toutes les 4 heures', 6), ('Q6H', N'every 6 hours', N'toutes les 6 heures', 4), ('Q8H', N'every 8 hours', N'toutes les 8 heures', 3),
('Q12H', N'every 12 hours', N'toutes les 12 heures', 2), ('QW', N'once a week', N'une fois par semaine', 0.14),
('PRN', N'as needed', N'au besoin', NULL), ('AC', N'before meals', N'avant les repas', NULL), ('PC', N'after meals', N'après les repas', NULL),
('UD', N'as directed', N'selon les directives', NULL), ('STAT', N'immediately', N'immédiatement', NULL),
('TAB', N'tablet', N'comprimé', NULL), ('TABS', N'tablets', N'comprimés', NULL), ('CAP', N'capsule', N'capsule', NULL), ('CAPS', N'capsules', N'capsules', NULL),
('ML', N'mL', N'mL', NULL), ('PUFF', N'puff', N'inhalation', NULL), ('PUFFS', N'puffs', N'inhalations', NULL), ('GTT', N'drop', N'goutte', NULL),
('GTTS', N'drops', N'gouttes', NULL), ('APPLY', N'apply', N'appliquer', NULL), ('INSTILL', N'instill', N'instiller', NULL),
('PO', N'by mouth', N'par la bouche', NULL), ('SL', N'under the tongue', N'sous la langue', NULL), ('INH', N'by inhalation', N'par inhalation', NULL),
('TOP', N'to the skin', N'sur la peau', NULL), ('OD_EYE', N'in the right eye', N'dans l''oeil droit', NULL), ('OS', N'in the left eye', N'dans l''oeil gauche', NULL),
('OU', N'in both eyes', N'dans les deux yeux', NULL), ('X', N'for', N'pendant', NULL), ('D', N'days', N'jours', NULL), ('WK', N'weeks', N'semaines', NULL),
('WITH_FOOD', N'with food', N'avec de la nourriture', NULL), ('SOB', N'for shortness of breath', N'en cas d''essoufflement', NULL),
('PAIN', N'for pain', N'pour la douleur', NULL);

-- Common ingredients with their WHO ATC code and the allergy family they belong to.
INSERT ref.Ingredient (Name, AtcCode, AllergyClass) VALUES
(N'Amoxicillin', 'J01CA04', N'Penicillins'), (N'Cephalexin', 'J01DB01', N'Cephalosporins'), (N'Clarithromycin', 'J01FA09', N'Macrolides'),
(N'Azithromycin', 'J01FA10', N'Macrolides'), (N'Ciprofloxacin', 'J01MA02', N'Fluoroquinolones'), (N'Sulfamethoxazole and trimethoprim', 'J01EE01', N'Sulfonamides'),
(N'Nitrofurantoin', 'J01XE01', NULL), (N'Metformin', 'A10BA02', NULL), (N'Atorvastatin', 'C10AA05', N'Statins'), (N'Rosuvastatin', 'C10AA07', N'Statins'),
(N'Ramipril', 'C09AA05', N'ACE inhibitors'), (N'Amlodipine', 'C08CA01', NULL), (N'Hydrochlorothiazide', 'C03AA03', N'Sulfonamides'),
(N'Spironolactone', 'C03DA01', NULL), (N'Metoprolol', 'C07AB02', N'Beta blockers'), (N'Warfarin', 'B01AA03', NULL), (N'Apixaban', 'B01AF02', NULL),
(N'Acetylsalicylic acid', 'B01AC06', N'NSAIDs'), (N'Ibuprofen', 'M01AE01', N'NSAIDs'), (N'Naproxen', 'M01AE02', N'NSAIDs'), (N'Acetaminophen', 'N02BE01', NULL),
(N'Oxycodone', 'N02AA05', N'Opioids'), (N'Hydromorphone', 'N02AA03', N'Opioids'), (N'Tramadol', 'N02AX02', N'Opioids'), (N'Lorazepam', 'N05BA06', N'Benzodiazepines'),
(N'Sertraline', 'N06AB06', N'SSRIs'), (N'Citalopram', 'N06AB04', N'SSRIs'), (N'Levothyroxine', 'H03AA01', NULL), (N'Salbutamol', 'R03AC02', NULL),
(N'Fluticasone', 'R03BA05', N'Corticosteroids'), (N'Prednisone', 'H02AB07', N'Corticosteroids'), (N'Pantoprazole', 'A02BC02', N'Proton pump inhibitors'),
(N'Insulin glargine', 'A10AE04', NULL), (N'Methylphenidate', 'N06BA04', NULL), (N'Calcium carbonate', 'A12AA04', NULL), (N'Hydrocortisone', 'D07AA02', N'Corticosteroids');

-- Well-known interactions, checked at reception against the prescription and the patient's other active medicines.
DECLARE @i TABLE (Name nvarchar(100) PRIMARY KEY, Id int);
INSERT @i SELECT Name, IngredientId FROM ref.Ingredient;
INSERT ref.Interaction (IngredientAId, IngredientBId, Severity, Description)
SELECT CASE WHEN a.Id < b.Id THEN a.Id ELSE b.Id END, CASE WHEN a.Id < b.Id THEN b.Id ELSE a.Id END, x.Severity, x.Description
FROM (VALUES
    (N'Warfarin', N'Ibuprofen', 'Major', N'Bleeding risk: NSAIDs add to warfarin''s effect and irritate the stomach.'),
    (N'Warfarin', N'Naproxen', 'Major', N'Bleeding risk: NSAIDs add to warfarin''s effect and irritate the stomach.'),
    (N'Warfarin', N'Acetylsalicylic acid', 'Major', N'Bleeding risk.'),
    (N'Warfarin', N'Sulfamethoxazole and trimethoprim', 'Major', N'Raises the INR; monitor closely or choose another antibiotic.'),
    (N'Warfarin', N'Ciprofloxacin', 'Moderate', N'May raise the INR.'),
    (N'Apixaban', N'Acetylsalicylic acid', 'Moderate', N'Bleeding risk.'),
    (N'Atorvastatin', N'Clarithromycin', 'Major', N'Clarithromycin raises atorvastatin levels: risk of myopathy and rhabdomyolysis.'),
    (N'Oxycodone', N'Lorazepam', 'Major', N'Opioid with benzodiazepine: profound sedation and respiratory depression.'),
    (N'Hydromorphone', N'Lorazepam', 'Major', N'Opioid with benzodiazepine: profound sedation and respiratory depression.'),
    (N'Tramadol', N'Sertraline', 'Major', N'Serotonin syndrome and seizure risk.'),
    (N'Tramadol', N'Citalopram', 'Major', N'Serotonin syndrome and seizure risk.'),
    (N'Ramipril', N'Spironolactone', 'Moderate', N'Raised potassium; check electrolytes.'),
    (N'Citalopram', N'Clarithromycin', 'Moderate', N'QT prolongation.'),
    (N'Levothyroxine', N'Calcium carbonate', 'Moderate', N'Calcium reduces levothyroxine absorption: take 4 hours apart.'),
    (N'Ciprofloxacin', N'Calcium carbonate', 'Moderate', N'Calcium reduces ciprofloxacin absorption: take 2 hours before or 6 hours after.'),
    (N'Metformin', N'Prednisone', 'Minor', N'Corticosteroids raise blood glucose.')
) x(A, B, Severity, Description)
JOIN @i a ON a.Name = x.A JOIN @i b ON b.Name = x.B;

INSERT ref.[Plan] (Code, Name, PayerType, CopayAmount, CopayPercent, DispensingFee, MarkupPercent, Notes) VALUES
('ODB',     N'Ontario Drug Benefit',            'ODB',     2.00, 0,  8.83, 8,  N'Demo settings: confirm the co-payment, dispensing fee and mark-up against the current ODB agreement before use.'),
('PRIVATE', N'Private drug plan (demo)',        'Insurer', 0,    20, 11.99, 10, N'Demo plan paying 80 %; each insurer''s contract sets its own terms.'),
('CASH',    N'Cash (patient pays)',             'Cash',    0,    100, 11.99, 10, N'The patient pays the whole prescription.');
GO
