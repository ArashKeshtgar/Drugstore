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

Sign-in is a development-only stub for now (everyone is the developer pharmacist); the API refuses to start outside
development until sign-in through ClinicHub is added.

## Status

- [x] Base definitions, drug catalog, patients with allergies, prescribers
- [x] Prescription reception with allergy, interaction, duplicate-therapy, monitored-drug, Limited Use and age checks
- [ ] Purchasing and inventory by lot and expiry
- [ ] Filling, verification, dispensing, pricing by plan, payer split, labels
- [ ] Financial documents to FinancePowerApp (the group's accounting)
- [ ] Electronic prescriptions from ClinicHub; sign-in through ClinicHub
- [ ] Health Canada Drug Product Database import
