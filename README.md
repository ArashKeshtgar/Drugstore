# PharmaFlow

A community pharmacy for an Ontario clinic group, part of the [ClinicHub](https://github.com/ArashKeshtgar/ClinicHub)
platform. It is the Ontario version of a legacy hospital pharmacy system ("Drugstore"); what is carried over and what
changes is in [docs/drugstore-to-ontario.md](docs/drugstore-to-ontario.md).

Stack: ASP.NET Core 10 minimal API + EF Core over SQL scripts, React 19 + Vite + TypeScript, SQL Server.

## What it does

- **Base definitions:** dosage forms, routes, units, schedules (NAPRA I/II/III/unscheduled and the CDSA narcotic,
  controlled and targeted classes, flagged for the Narcotics Monitoring System), SIG codes with doses a day,
  ingredients with ATC codes and allergy families, interactions with severity, manufacturers, suppliers, payer plans
  (ODB, private, cash).
- **Drug catalog:** one product per DIN, with ingredient, strength, form, route, schedule, pack size, dispensing unit,
  cost, ODB Formulary and Limited Use flags, and an interchangeable group for generic substitution.
- **Patients and prescribers:** Ontario health card (check digit), allergies (by ingredient, family, or as the patient
  said), drug plan; prescribers by college (CPSO, CNO, RCDSO, OCP). Opening a patient is logged (PHIPA).
- **Reception:** a prescription (paper, fax, phone, electronic) entered drug by drug: what was written, the product,
  SIG (expanded into label text, with the days' supply worked out), quantity, refills (none for narcotics and
  controlled drugs), no-substitution. Each drug is checked against the patient's allergies, the other drugs on the
  prescription and recent medicines (interactions, duplicate therapy), and flagged when monitored, Limited Use or more
  than a year old. The pharmacist matches unmatched drugs, reviews the findings, holds or cancels.
- **Stock and dispensing:** supplier invoices received into lots; a drug ready to fill is dispensed (or refilled)
  from the lots that expire first, priced by the patient's plan, and the money goes to the group's accounting.

The demo catalog's DINs and costs are made up (`IsDemo`), and the plan amounts are settings to confirm against the
current ODB agreement and each insurer's contract. Patients and prescribers are synthetic.

## Run it

Requires SQL Server and `sqlcmd`.

```powershell
./database/deploy.ps1 -Recreate                # PharmaFlow database with the demo data (-NoDemo for none)
dotnet run --project src/PharmaFlow.Api        # http://localhost:5500
cd web; npm install; npm run dev               # http://localhost:5501
dotnet test                                    # builds its own database from database/*.sql
```

Electronic prescriptions need ClinicHub running: `ClinicHub:Authority` (its address, the issuer of its tokens) and, in
development, `ClinicHub:MetadataAddress`, are in `appsettings.Development.json`. ClinicHub sends to `{this address}/fhir`
(`Integrations:PharmaFlow:FhirBaseUrl`), directly or through IntegrationHub's `pharmaflow` route. An existing database
gets the new column with `./database/deploy.ps1`.

Sign-in is a development stub (everyone is the developer pharmacist, with every permission; the header `X-Dev-Permissions:
reception stock` narrows it) unless PharmaFlow is connected to ClinicHub. To sign in through ClinicHub, with ClinicHub
running and its `pharmaflow` client configured (Development has one), start the API with

```powershell
$env:ClinicHub__ClientSecret='dev-only-pharmaflow-secret'; dotnet run --project src/PharmaFlow.Api
```

and open the web on http://localhost:5501 (the address registered at ClinicHub). The demo user `pharmacist` has the
Pharmacist role; `admin` has everything.

## Status

- [x] Base definitions, drug catalog, patients with allergies, prescribers
- [x] Prescription reception with allergy, interaction, duplicate-therapy, monitored-drug, Limited Use and age checks
- [x] Purchasing (supplier invoices into lots with expiry and cost) and stock on hand
- [x] Dispensing and refills: oldest-expiring lots first, never expired stock; price = acquisition cost + the plan's
      mark-up + dispensing fee; the patient's co-payment taken at the counter, the plan's share owed by the plan
- [x] Financial documents to FinancePowerApp: supplier invoices as bills (drugs zero-rated, supplies with HST claimed
      back), each dispense as invoices to the plan and the patient, the patient's payment, and the cost of drugs out of
      inventory. `Finance:BaseUrl` (and in development `Finance:DevKey`)
- [ ] Filling and verification as separate steps, labels, Narcotics Monitoring System submission
- [x] Electronic prescriptions from ClinicHub: a FHIR Bundle to `/fhir` (token from ClinicHub, audience `pharmaflow`,
      scope `prescriptions`) becomes one `eRx` prescription at reception; a resend is recognised; a drug is withdrawn
      until it is filled, after which the pharmacy answers 409. `ClinicHub:Authority` turns it on
- [x] Sign-in through ClinicHub: OpenID Connect, one session cookie re-checked with ClinicHub every few minutes. Its
      roles give `reception`, `dispense`, `stock` and `admin` (which implies the rest); reads need any of them. With
      `ClinicHub:ClientSecret` set the API starts outside development too, and the development stub is gone
- [ ] Health Canada Drug Product Database import
