# From an Iranian hospital pharmacy to an Ontario community pharmacy

**Status:** mapping study, October 2026. **Author:** Arash Keshtgar.

This maps the legacy pharmacy system I worked with in Iran ("Drugstore", the pharmacy module of the same hospital's
information system, SQL Server) onto how the work is done in an Ontario community pharmacy, and says what
**PharmaFlow** carries over, changes and leaves behind. It is the same exercise as ClinicHub's
`totalsystem-to-ontario.md` and LabFlow's move from the legacy `Laboratory` database.

> **Data rule.** The legacy database holds real patients (names, national codes, mobile numbers, birth dates on
> every outpatient and ward request). Everything here comes from schema metadata, aggregate counts and reference
> lists (dosage forms, units, dosing frequencies) read on an offline copy. No record about a patient was read, and none
> leaves that machine. PharmaFlow's demo data is synthetic.

## 1. The legacy system at a glance

| | |
|---|---|
| Scope | One hospital's pharmacy: outpatient prescriptions, ward requests for about 100 inpatient and procedure units, the main store, purchasing |
| Size | 124 tables; the largest: ward request lines 24.8 M, ward stock card 8.6 M, stock card 5.6 M, insurance price log 4.8 M, ward requests 2.9 M, outpatient prescription lines 2.0 M, outpatient prescriptions 670,000 |
| Catalog | 18,546 items in one table (`DrugDef`): drugs and medical supplies together (sutures, catheters, balloons), names mostly in Persian |
| Integrity | No foreign keys; dates are Jalali `varchar`; money is `float` |
| Insurance | Iran's basic insurers (Tamin, Salamat, armed forces) with per-drug coverage, patient share, subsidy and currency-difference columns, online approval (`BimeInternetConfirm…`) |

## 2. Workflow → tables

| Legacy | Tables | Ontario community pharmacy | PharmaFlow |
|---|---|---|---|
| Outpatient prescription | `SRequest`, `SAnswerDetail` | Prescription reception, filling, verification, dispensing, counselling | `rx.Prescription`, `rx.PrescriptionItem` (reception built; filling and dispensing next) |
| Ward requests (inpatient) | `BRequest`, `BAnswerDetail`, `Part`, `PartDrug`, `PartStockCartex` | Hospital pharmacy only | **Not carried over**: a community pharmacy has no wards |
| Main store, stock card | `Anbar`, `Cartex` | Inventory by lot and expiry | Next step (`inv` schema) |
| Purchasing, returns | `Buy`, `DrugBuy`, `Seller`, `ReturnToSeller` | Orders from a wholesaler, invoices, returns | Suppliers now; purchases next, sent to the accounting as bills |
| Catalog | `DrugDef`, `Kind`, `Type`, `Unit`, `SPS_DrugShape`, `MainGroup`, `SubGroup` | Health Canada's Drug Product Database (DIN), ingredients with ATC codes | `ref.Drug` (DIN), `ref.Ingredient`, `ref.DosageForm`, `ref.Unit`, `ref.Route` |
| Dosing instructions | `UsageKind`, `UsageInstruction`, `BimeTaamin_DrugTimesADay` (frequency-based: "every 8 hours – tab/cap") | SIG codes on the prescription, label in plain English (and French) | `ref.SigCode` with doses a day; `Domain/Sig.cs` writes the label and days' supply |
| Drug conflicts | `ConflictDrugs` (10 rows, free text) | Interaction checking at reception, by ingredient, with severity | `ref.Interaction`, `Domain/ReceptionChecks.cs` |
| Insurance | `Bime`, `BimeDrug`, `BimeTaamin_*`, `BimeSalamat_*` | Ontario Drug Benefit (ODB) and private drug plans, adjudicated online | `ref.Plan` (co-pay, dispensing fee, mark-up as settings) |
| Originality check (UID) | `OriginalityOfDrugs` | Narcotics Monitoring System for monitored drugs | `ref.Schedule.Monitored`; reception flags them |
| Prescribers | `Doctor` (with a blacklist) | College registration: CPSO, CNO, RCDSO, OCP | `core.Prescriber` |
| Cash desk | `CashPay`, `ExitOK` | Point of sale, payer split | With dispensing; sent to the accounting |

## 3. What changes

- **Identity of a product:** the Iranian IRC/G-codes and insurer codes become the **DIN**, with the ingredient (and its
  WHO ATC code) as the level where allergies and interactions are checked. The legacy catalog mixed drugs and
  supplies; PharmaFlow's catalog is drugs; supplies go through purchasing as non-drug items.
- **Schedules:** NAPRA's national schedules (I prescription, II behind the counter, III pharmacy only, unscheduled)
  and the Controlled Drugs and Substances Act classes (narcotic, controlled, targeted). Narcotics and controlled drugs
  can't be refilled; monitored drugs are recorded in the Narcotics Monitoring System when dispensed.
- **Payers:** ODB with its co-payment and dispensing fee, Limited Use products needing the prescriber's reason code,
  private plans with their own co-pay, cash. The legacy subsidy and currency-difference prices have no equivalent.
- **Safety at reception:** allergy (by ingredient and by family: penicillins, sulfonamides, NSAIDs…), interaction,
  duplicate therapy, monitored drug, Limited Use, prescription older than a year. They warn; the pharmacist reviews.
- **Patients:** the legacy system copied names and national codes onto every request; PharmaFlow keeps one patient
  record (Ontario health card, allergies, drug plan) and an access log (PHIPA).
- **Dates, money, language:** ISO dates, `decimal` dollars, English and French labels.

## 4. Status

| Step | Status |
|---|---|
| Base definitions, drug catalog, patients and allergies, prescribers | **Built** |
| Reception with checks | **Built** |
| Purchasing and inventory by lot and expiry | **Built** |
| Dispensing and refills, pricing by plan, the payer split | **Built** (filling and verification as separate steps: next) |
| Financial documents to FinancePowerApp (purchases as bills, dispensing as invoices to ODB, insurers and patients, cost of drugs) | **Built** |
| Electronic prescriptions from ClinicHub (FHIR MedicationRequest), sign-in through ClinicHub | Later |
| Importing Health Canada's Drug Product Database instead of the demo catalog | Later |
