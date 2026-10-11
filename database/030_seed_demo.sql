-- Demo: a small catalog with made-up DINs and costs (IsDemo = 1, never to be dispensed from), synthetic patients and
-- prescribers. Skipped by deploy.ps1 -NoDemo.

SET NOCOUNT ON;

INSERT ref.Manufacturer (Name, Country) VALUES (N'Demo Generics Inc.', 'CA'), (N'Northern Pharma (demo)', 'CA');
INSERT ref.Supplier (Code, Name, Phone, Email, AccountNumber) VALUES
('WHOLESALE-A', N'Wholesaler A (demo)', '(905) 555-0110', 'orders@wholesaler-a.example', 'A-0042'),
('WHOLESALE-B', N'Wholesaler B (demo)', '(416) 555-0120', 'orders@wholesaler-b.example', 'B-7731');

DECLARE @gen int = (SELECT ManufacturerId FROM ref.Manufacturer WHERE Name = N'Demo Generics Inc.');
DECLARE @north int = (SELECT ManufacturerId FROM ref.Manufacturer WHERE Name = N'Northern Pharma (demo)');

INSERT ref.Drug (Din, BrandName, IngredientId, Strength, StrengthUnit, FormCode, RouteCode, ScheduleCode, ManufacturerId, PackSize, DispensingUnit, UnitCost, OdbBenefit, OdbLimitedUse, InterchangeGroup, IsDemo)
SELECT d.Din, d.Brand, i.IngredientId, d.Strength, d.SUnit, d.Form, d.Route, d.Sched, CASE d.M WHEN 'G' THEN @gen ELSE @north END, d.Pack, d.DUnit, d.Cost, d.Odb, d.Lu, d.Grp, 1
FROM (VALUES
    ('99000011', N'Amoxicillin (demo)',          N'Amoxicillin',      500,   'MG',   'CAP',  'PO',  'I',  'G', 100, 'CAP',   0.1100, 1, 0, 'AMOX500'),
    ('99000012', N'Amoxi-North (demo)',          N'Amoxicillin',      500,   'MG',   'CAP',  'PO',  'I',  'N', 100, 'CAP',   0.1500, 1, 0, 'AMOX500'),
    ('99000021', N'Cephalexin (demo)',           N'Cephalexin',       500,   'MG',   'CAP',  'PO',  'I',  'G', 100, 'CAP',   0.2200, 1, 0, 'CEPH500'),
    ('99000031', N'Clarithromycin (demo)',       N'Clarithromycin',   500,   'MG',   'TAB',  'PO',  'I',  'G', 60,  'TAB',   0.6500, 1, 0, 'CLAR500'),
    ('99000041', N'Ciprofloxacin (demo)',        N'Ciprofloxacin',    500,   'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.3000, 1, 0, 'CIPR500'),
    ('99000051', N'Nitrofurantoin (demo)',       N'Nitrofurantoin',   100,   'MG',   'CAP',  'PO',  'I',  'G', 100, 'CAP',   0.4200, 1, 0, 'NITR100'),
    ('99000061', N'Metformin (demo)',            N'Metformin',        500,   'MG',   'TAB',  'PO',  'I',  'G', 500, 'TAB',   0.0300, 1, 0, 'METF500'),
    ('99000071', N'Atorvastatin (demo)',         N'Atorvastatin',     20,    'MG',   'TAB',  'PO',  'I',  'G', 90,  'TAB',   0.1300, 1, 0, 'ATOR20'),
    ('99000081', N'Rosuvastatin (demo)',         N'Rosuvastatin',     10,    'MG',   'TAB',  'PO',  'I',  'G', 90,  'TAB',   0.1200, 1, 0, 'ROSU10'),
    ('99000091', N'Ramipril (demo)',             N'Ramipril',         5,     'MG',   'CAP',  'PO',  'I',  'G', 100, 'CAP',   0.0900, 1, 0, 'RAMI5'),
    ('99000101', N'Amlodipine (demo)',           N'Amlodipine',       5,     'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.0700, 1, 0, 'AMLO5'),
    ('99000111', N'Hydrochlorothiazide (demo)',  N'Hydrochlorothiazide', 25, 'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.0400, 1, 0, 'HCTZ25'),
    ('99000121', N'Spironolactone (demo)',       N'Spironolactone',   25,    'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.1000, 1, 0, 'SPIR25'),
    ('99000131', N'Metoprolol (demo)',           N'Metoprolol',       50,    'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.0600, 1, 0, 'METO50'),
    ('99000141', N'Warfarin (demo)',             N'Warfarin',         5,     'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.0800, 1, 0, 'WARF5'),
    ('99000151', N'Apixaban (demo)',             N'Apixaban',         5,     'MG',   'TAB',  'PO',  'I',  'N', 60,  'TAB',   1.2000, 1, 1, 'APIX5'),
    ('99000161', N'ASA 81 (demo)',               N'Acetylsalicylic acid', 81, 'MG',  'TAB',  'PO',  'U',  'G', 120, 'TAB',   0.0200, 1, 0, 'ASA81'),
    ('99000171', N'Ibuprofen (demo)',            N'Ibuprofen',        400,   'MG',   'TAB',  'PO',  'III','G', 100, 'TAB',   0.0500, 0, 0, 'IBUP400'),
    ('99000181', N'Naproxen (demo)',             N'Naproxen',         500,   'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.1100, 1, 0, 'NAPR500'),
    ('99000191', N'Acetaminophen (demo)',        N'Acetaminophen',    500,   'MG',   'TAB',  'PO',  'U',  'G', 100, 'TAB',   0.0200, 0, 0, 'ACET500'),
    ('99000201', N'Oxycodone (demo)',            N'Oxycodone',        5,     'MG',   'TAB',  'PO',  'N',  'N', 100, 'TAB',   0.2500, 1, 0, 'OXYC5'),
    ('99000211', N'Hydromorphone (demo)',        N'Hydromorphone',    2,     'MG',   'TAB',  'PO',  'N',  'N', 100, 'TAB',   0.1800, 1, 0, 'HYDR2'),
    ('99000221', N'Tramadol (demo)',             N'Tramadol',         50,    'MG',   'TAB',  'PO',  'N',  'G', 100, 'TAB',   0.3500, 0, 0, 'TRAM50'),
    ('99000231', N'Lorazepam (demo)',            N'Lorazepam',        1,     'MG',   'TAB',  'SL',  'T',  'G', 100, 'TAB',   0.0500, 1, 0, 'LORA1'),
    ('99000241', N'Sertraline (demo)',           N'Sertraline',       50,    'MG',   'CAP',  'PO',  'I',  'G', 100, 'CAP',   0.2000, 1, 0, 'SERT50'),
    ('99000251', N'Citalopram (demo)',           N'Citalopram',       20,    'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.1000, 1, 0, 'CITA20'),
    ('99000261', N'Levothyroxine (demo)',        N'Levothyroxine',    100,   'MCG',  'TAB',  'PO',  'I',  'N', 90,  'TAB',   0.1000, 1, 0, 'LEVO100'),
    ('99000271', N'Salbutamol inhaler (demo)',   N'Salbutamol',       100,   'MCGD', 'INH',  'INH', 'I',  'N', 1,   'INHLR', 6.5000, 1, 0, 'SALB100'),
    ('99000281', N'Fluticasone inhaler (demo)',  N'Fluticasone',      125,   'MCGD', 'INH',  'INH', 'I',  'N', 1,   'INHLR', 45.0000, 1, 0, 'FLUT125'),
    ('99000291', N'Prednisone (demo)',           N'Prednisone',       5,     'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.0400, 1, 0, 'PRED5'),
    ('99000301', N'Pantoprazole (demo)',         N'Pantoprazole',     40,    'MG',   'TAB',  'PO',  'I',  'G', 100, 'TAB',   0.1500, 1, 0, 'PANT40'),
    ('99000311', N'Insulin glargine pen (demo)', N'Insulin glargine', 100,   'UML',  'PEN',  'SC',  'I',  'N', 5,   'PEN',   18.0000, 1, 1, 'GLAR100'),
    ('99000321', N'Methylphenidate ER (demo)',   N'Methylphenidate',  18,    'MG',   'TABER','PO',  'C',  'N', 100, 'TAB',   1.1000, 1, 1, 'MPH18'),
    ('99000331', N'Calcium carbonate (demo)',    N'Calcium carbonate',500,   'MG',   'TAB',  'PO',  'U',  'G', 120, 'TAB',   0.0300, 0, 0, 'CALC500'),
    ('99000341', N'Hydrocortisone 1% (demo)',    N'Hydrocortisone',   1,     'PCT',  'CRM',  'TOP', 'III','G', 30,  'GM',    0.2000, 0, 0, 'HYDC1')
) d(Din, Brand, Ingredient, Strength, SUnit, Form, Route, Sched, M, Pack, DUnit, Cost, Odb, Lu, Grp)
JOIN ref.Ingredient i ON i.Name = d.Ingredient;

DECLARE @odb int = (SELECT PlanId FROM ref.[Plan] WHERE Code = 'ODB');
DECLARE @private int = (SELECT PlanId FROM ref.[Plan] WHERE Code = 'PRIVATE');

-- Synthetic patients (made-up names and health cards that pass the check digit).
INSERT core.Patient (HealthCardNumber, HealthCardVersion, FirstName, LastName, DateOfBirth, Sex, Phone, City, PostalCode, DefaultPlanId, PlanMemberId) VALUES
('9876543217', 'AB', N'Margaret', N'Oduya',   '1948-04-12', 'F', '(416) 555-0201', N'Toronto',   'M5V 2T6', @odb, NULL),
('5550123458', 'CD', N'Daniel',   N'Kowalczyk','1979-09-30', 'M', '(647) 555-0202', N'Etobicoke', 'M8V 1A1', @private, 'PLN-55102'),
('1111111116', NULL, N'Priya',    N'Anand',    '1992-01-17', 'F', '(905) 555-0203', N'Mississauga','L5B 3C2', NULL, NULL);

DECLARE @warfarin int = (SELECT IngredientId FROM ref.Ingredient WHERE Name = N'Warfarin');
INSERT core.PatientAllergy (PatientId, IngredientId, AllergyClass, Description, Reaction, Severity)
SELECT p.PatientId, NULL, N'Penicillins', NULL, N'Hives', 'Moderate' FROM core.Patient p WHERE p.LastName = N'Oduya'
UNION ALL
SELECT p.PatientId, NULL, N'Sulfonamides', NULL, N'Rash', 'Mild' FROM core.Patient p WHERE p.LastName = N'Anand';

INSERT core.Prescriber (LicenceBody, LicenceNumber, FirstName, LastName, Phone, Fax, Clinic) VALUES
('CPSO', '99001', N'Nadia', N'Patel', '(416) 555-0142', '(416) 555-0143', N'Harbourview Medical Centre (demo)'),
('CPSO', '99002', N'Hannah', N'Okoye', '(416) 555-0187', '(416) 555-0188', N'Lakeshore Family Health (demo)'),
('CNO',  '99003', N'Grace', N'Mensah', '(416) 555-0142', NULL, N'Harbourview Medical Centre (demo)');
GO
