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
- Administrator request queue with status filters and full request review
- Atomic approval/rejection decisions with actor snapshots and stale-review protection
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

## Day 4 test plan

The following decision-rule cases are automated and must remain green:

- Approving a pending request changes its status to `Approved`, records the server time, and
  snapshots the administrator's ID and trimmed full name.
- Approval ignores text from the rejection-only field.
- Rejecting a pending request changes its status to `Rejected` and stores a trimmed reason.
- Rejection fails for a null, empty, or whitespace-only reason.
- Rejection fails when its reason exceeds the 2,000-character database limit.
- A request already approved or rejected cannot receive another decision.
- Unknown decision values and invalid administrator details are rejected.

These PostgreSQL and browser integration cases should also pass before production deployment:

- Administrators can open the queue and detail page; employees, accountants, and anonymous users
  cannot approve, reject, or download review attachments.
- The queue defaults to pending requests, filters all statuses correctly, displays accurate totals,
  and orders the newest submissions first.
- The detail page displays the requester snapshot, payee, reason, authoritative total, ordered
  expense rows, and attachment metadata without loading attachment bytes into the page query.
- An authorized attachment download returns the correct file, while a missing attachment or an
  attachment belonging to another request returns `404`.
- Approval and rejection each save the request status and its single decision in one transaction.
- Two administrators deciding from the same version result in one saved decision and one clear
  stale-review message, never two decisions or a silently overwritten decision.
- An inactive administrator cannot decide a request even if an older sign-in cookie still exists.
- Employee history and private tracking pages show the new final status after a decision.
- Anti-forgery validation rejects forged decision posts, and invalid rejection input preserves the
  review page with a useful validation message.

## What I learned

This project has helped me get more comfortable with ASP.NET Core Razor Pages, dependency
injection, Entity Framework relationships, Identity authentication, and PostgreSQL migrations.
I also learned that validation needs to happen on the server, that access keys should be stored as
hashes, and that request data should keep a snapshot of the employee information used at the time
of submission. The approval workflow also demonstrates why final decisions need an actor snapshot
and an optimistic-concurrency check. The next things I want to improve are PostgreSQL integration
testing, the accountant transfer workflow, and end-to-end browser testing.

## Delivery progress

- [x] Solution, PostgreSQL, Entity Framework, and Identity foundation
- [x] Financial domain model and initial migration
- [x] Administrator/accountant login and authorization policies
- [x] Employee credential and access management
- [x] Employee request submission, attachments, tracking, and history
- [x] Administrator request dashboard and approval/rejection workflow
- [ ] Accountant transfer execution and audit workflow
- [ ] Printable records, exports, reporting, and Persian/Jalali interface
- [ ] Integration/security testing and production deployment

## Scope boundaries

This application records internal authorization and execution. It does not initiate bank
transfers, automatically approve requests, replace accounting software, or provide a general
accounting ledger.

Historical retention, production backup policy, and deployment hardening remain to be agreed
before release.
