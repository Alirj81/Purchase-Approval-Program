# Purchase Approval Program

An ASP.NET Core Razor Pages application for creating, approving, executing, and tracking internal
company payment requests.

The project is under active development and is not ready for production use.

## Current capabilities

- Administrator and accountant authentication with ASP.NET Core Identity
- Separate access-key authentication for employees
- Administrator employee creation, access rotation, disabling, reactivation, and revocation
- Employee profile auto-population from the authenticated database record
- Employee payment requests with multiple expense rows and authoritative server totals
- PDF, JPEG, and PNG attachments with size and file-signature validation
- Pending-request persistence with requester snapshots
- Unique request references and private token-based status links
- Employee request history
- PostgreSQL schema for approval, transfer, audit, attachment, and catalogue records

## Technology

- .NET 10 and C#
- ASP.NET Core Razor Pages and Identity
- Entity Framework Core with PostgreSQL
- Bootstrap and JavaScript
- xUnit

## Project structure

```text
src/PurchaseApproval.Web/          Web application
tests/PurchaseApproval.Web.Tests/  Automated tests
docs/                              Plans and implementation reports
compose.yaml                       Local PostgreSQL service
PurchaseApproval.slnx              .NET solution
```

## Local setup

Requirements: .NET 10 SDK and Docker Desktop (or PostgreSQL 17).

1. Restore tools and packages:

   ```powershell
   dotnet restore
   dotnet tool restore
   ```

2. Copy `.env.example` to `.env`, then set a local database password.

3. Start PostgreSQL:

   ```powershell
   docker compose up -d
   ```

4. Store the connection string outside source control:

   ```powershell
   dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=purchase_approval_dev;Username=purchase_approval_app;Password=YOUR_PASSWORD" --project src/PurchaseApproval.Web
   ```

5. Apply the schema and run the app:

   ```powershell
   dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
   dotnet run --project src/PurchaseApproval.Web --launch-profile https
   ```

The HTTPS development address is `https://localhost:7054`; HTTP uses
`http://localhost:5083`.

## First administrator

Set the bootstrap values using configuration or user secrets, then run the explicit one-time
command:

```powershell
dotnet run --project src/PurchaseApproval.Web -- bootstrap-admin
```

The bootstrap operation is idempotent and does not update an existing account. Remove the
bootstrap password from configuration after use.

## Verification

```powershell
dotnet build PurchaseApproval.slnx --configuration Release --warnaserror
dotnet test PurchaseApproval.slnx --configuration Release
```

## What I learned

This project has helped me get more comfortable with ASP.NET Core Razor Pages, dependency
injection, Entity Framework relationships, Identity authentication, and PostgreSQL migrations.
I also learned that validation needs to happen on the server, that access keys should be stored as
hashes, and that request data should keep a snapshot of the employee information used at the time
of submission. The next things I want to improve are integration testing, concurrency handling,
and the administrator approval workflow.

## Delivery progress

- [x] Solution, PostgreSQL, Entity Framework, and Identity foundation
- [x] Financial domain model and initial migration
- [x] Administrator/accountant login and authorization policies
- [x] Employee credential and access management
- [x] Employee request submission, attachments, tracking, and history
- [ ] Administrator request dashboard and approval/rejection workflow
- [ ] Accountant transfer execution and audit workflow
- [ ] Printable records, exports, reporting, and Persian/Jalali interface
- [ ] Integration/security testing and production deployment

## Scope boundaries

This application records internal authorization and execution. It does not initiate bank
transfers, automatically approve requests, replace accounting software, or provide a general
accounting ledger.

Historical retention, production backup policy, and deployment hardening remain to be agreed
before release.
