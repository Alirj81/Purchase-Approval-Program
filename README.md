# Purchase Approval Program

A simple web application for creating and managing company payment requests.

This is a personal portfolio project and my first complete ASP.NET Core application. Its goal is
to demonstrate a simple workflow for recording, approving, and tracking payment requests.

## Main Features

- Administrator and accountant login
- Employee accounts with access keys
- Employee access management
- Payment request database models
- Approval and payment status records
- PostgreSQL database support
- English web pages

Some parts of the application are still under development.

## Technologies

- C#
- ASP.NET Core Razor Pages
- Entity Framework Core
- ASP.NET Core Identity
- PostgreSQL
- Bootstrap
- xUnit

## Project Structure

```text
src/PurchaseApproval.Web/          Main web application
tests/PurchaseApproval.Web.Tests/  Automated tests
compose.yaml                       Local PostgreSQL setup
PurchaseApproval.slnx              .NET solution file
```

## Requirements

- .NET 10 SDK
- Docker Desktop, or a PostgreSQL 17 server

## Setup

1. Restore the project packages:

   ```powershell
   dotnet restore
   dotnet tool restore
   ```

2. Create a `.env` file from `.env.example` and set a local PostgreSQL password.

3. Start PostgreSQL:

   ```powershell
   docker compose up -d
   ```

4. Add the database connection string with .NET user secrets:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=purchase_approval_dev;Username=purchase_approval_app;Password=YOUR_PASSWORD" --project src/PurchaseApproval.Web
   ```

5. Create the database tables:

   ```powershell
   dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
   ```

6. Run the application:

   ```powershell
   dotnet run --project src/PurchaseApproval.Web
   ```

## Tests

Run the automated tests with:

```powershell
dotnet test
```

## Status

<<<<<<< HEAD
### Build

`dotnet build`

### Run Tests

`dotnet test`

### Run the Web Application

`dotnet run --project src/PurchaseApproval.Web --launch-profile https`

The development HTTPS profile is configured for:

`https://localhost:7054`

The HTTP profile uses:

`http://localhost:5083`

The current application only provides an initial web foundation. It should not be treated as a functioning payment approval system.

## 10. Development Progress

### Phase 1 — Solution Foundation

- [x] Create the GitHub repository
- [x] Initialize the ASP.NET Core 10 project
- [x] Establish the .NET solution structure
- [x] Add the xUnit test project
- [x] Configure PostgreSQL support
- [x] Configure Entity Framework Core
- [x] Configure ASP.NET Core Identity
- [x] Define administrator and accountant roles
- [x] Establish role-based authorization policies

### Phase 2 — Domain & Database

- [x] Define employee and privileged user models
- [x] Define payment request models
- [x] Define expense entry models
- [x] Define management approval records
- [x] Define financial transfer records
- [x] Define transaction audit records
- [x] Define attachment storage metadata
- [x] Define item catalogue model
- [x] Configure initial database relationships and constraints
- [x] Generate the initial database migration

### Phase 3 — Authentication & Employee Management

- [ ] Implement administrator authentication pages
- [ ] Implement accountant authentication pages
- [ ] Implement employee access-key generation
- [ ] Implement employee login and session handling
- [ ] Implement credential revocation and regeneration
- [ ] Implement automatic employee profile population
- [ ] Implement role-management interfaces

### Phase 4 — Payment Requests

- [ ] Develop the Persian RTL submission form
- [ ] Support adding/removing multiple expense rows
- [ ] Implement automatic request totals
- [ ] Enforce the maximum permitted request amount in application logic
- [ ] Implement attachment upload validation and storage
- [ ] Implement submission validation and rate limits
- [ ] Generate unique request numbers and secure tracking links
- [ ] Implement request persistence and status tracking

### Phase 5 — Management Approval

- [ ] Develop administrator dashboard
- [ ] Implement request review screens
- [ ] Implement approval and rejection operations
- [ ] Record decision attribution and timestamps
- [ ] Implement administrator-created requests
- [ ] Implement searchable request history

### Phase 6 — Accounting & Execution

- [ ] Develop accountant dashboard
- [ ] Implement transfer status management
- [ ] Implement transaction date/time entry
- [ ] Implement payment method and reference fields
- [ ] Support administrator-executed transactions
- [ ] Highlight unsuccessful transactions
- [ ] Implement manual closure of unsuccessful transactions
- [ ] Persist transaction correction and audit history

### Phase 7 — Documents & Reporting

- [ ] Generate printable approval/rejection receipts
- [ ] Include responsible administrator attribution
- [ ] Include execution details in updated records
- [ ] Implement Excel export
- [ ] Implement date-range accounting reports
- [ ] Develop the administrator history dashboard

### Phase 8 — Quality Assurance & Deployment

- [ ] Expand unit and integration test coverage
- [ ] Test authentication and permission boundaries
- [ ] Test concurrent financial record updates
- [ ] Verify file-upload security
- [ ] Test complete request-to-transfer workflows
- [ ] Finalize responsive Persian interface
- [ ] Review historical record-retention requirements
- [ ] Configure production security and backups
- [ ] Deploy to the company-managed server

## 11. Planned User Interface

The intended application will prioritize simplicity for nontechnical personnel.

Design requirements include:

- Persian-first interface
- Right-to-left layout
- Persian/Jalali date presentation
- Mobile-first responsive pages
- Clear navigation between employee, administrator, and accountant areas
- Minimal form input where information is already known
- Prominent request and payment statuses
- Appropriate warning indicators for unsuccessful transactions

The interface should remain practical on both office computers and mobile browsers.

## 12. Scope & Limitations

The application is intended as an internal payment authorization and tracking system.

It is not intended to:

- Initiate actual bank transfers
- Automatically approve financial requests
- Replace Sepidar or other accounting software
- Automatically verify company inventory
- Provide a complete accounting ledger
- Operate as a general-purpose external payment platform

Additional integrations and reporting capabilities may be considered after the core application is complete.

Historical financial information must be retained according to an agreed company retention policy and applicable legal requirements. Automated deletion rules have not yet been finalized.

## 13. Project Status

**The repository is under active development.**

The foundational project structure, identity configuration, authorization policies, financial domain models, and initial database migration have been established.

The next major stage is implementing the authentication and application-service workflows that connect the existing data model to working Razor Pages interfaces.

No production release is currently available.

## 14. License & Usage

No open-source license is currently provided.

This software is being developed for internal company use. Public availability of source code, if applicable, does not automatically grant permission for redistribution or commercial reuse.

Ownership and distribution permissions remain subject to applicable agreements.

---

**Project:** Purchase Approval Program  
**Organization:** Novin Abzian  
**Repository:** https://github.com/Alirj81/Purchase-Approval-Program  
**Documentation Updated:** October 2026  
**Development Status:** Active Development — Backend Foundation Established
=======
This project is under development and is not ready for production use.
>>>>>>> 2a7cd20 (payment request form added to dashboard with the requirements and check details for now, later to submit and work on the saving schema)
