# Purchase Approval Program

An ASP.NET Core Razor Pages application for creating, approving, executing, and tracking internal
company payment requests.

The project is under active development and is not ready for production use.

## Current capabilities

- Administrator and accountant authentication with ASP.NET Core Identity
- Separate access-key authentication for employees
- Administrator employee creation, access rotation, disabling, reactivation, and revocation
- Employee profile auto-population from the authenticated database record
- Employee and administrator payment requests with multiple expense rows and authoritative totals
- PDF, JPEG, and PNG attachments with size and file-signature validation
- Pending-request persistence with requester snapshots and durable duplicate-submit protection
- Unique request references and private token-based status links
- Employee request history
- Administrator general, own-submission, and per-requester histories
- Explicitly confirmed approval and optional-explanation rejection with stale-review protection
- Accountant execution queue and transfer history with administrator outcome visibility
- Cash, bank-transfer, cheque, and other execution records with actor/time snapshots
- Separate approval and transfer states, unsuccessful-item flags, audit records, and concurrency checks

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

## First accountant

Set `BootstrapAccountant:Username`, `BootstrapAccountant:Password`, and
`BootstrapAccountant:FullName` using configuration or user secrets, then run:

```powershell
dotnet run --project src/PurchaseApproval.Web -- bootstrap-accountant
```

The command creates one Identity account in the Accountant role and refuses to overwrite an
existing username. Remove the bootstrap password from configuration after use.

## Verification

```powershell
dotnet build PurchaseApproval.slnx --configuration Release --warnaserror
dotnet test PurchaseApproval.slnx --configuration Release
```

## Days 2–4 verification coverage

The following decision-rule cases are automated and must remain green:

- Approving a pending request changes its status to `Approved`, records the server time, and
  snapshots the administrator's ID and trimmed full name.
- Approval ignores text from the rejection-only field.
- Rejecting a pending request changes its status to `Rejected`; an explanation is optional and
  trimmed when supplied.
- Rejection fails when its reason exceeds the 2,000-character database limit.
- A request already approved or rejected cannot receive another decision.
- Unknown decision values and invalid administrator details are rejected.
- Exactly 10,000,000,000 IRR is allowed and a larger total is rejected.
- Transfer rules keep the request `Approved`, snapshot the executor, preserve reported occurrence
  time and offset separately from server recording time, and audit each recorded outcome.
- Accountants may record transferred or unsuccessful outcomes; administrators may record only
  successful transfers they personally performed.
- Method-specific details and duplicate/final-outcome protection are validated server-side.

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
- Accountants can reach execution pages but are denied administrator review and employee-key pages.
- Transferred requests leave the open queue and appear in history; unsuccessful requests remain
  red-flagged and open.

The latest audit, verified evidence, deferred policies, and exact run results are recorded in
`docs/DAY-4-AUDIT-AND-IMPLEMENTATION.md`.

## What I learned

This project has helped me get more comfortable with ASP.NET Core Razor Pages, dependency
injection, Entity Framework relationships, Identity authentication, and PostgreSQL migrations.
I also learned that validation needs to happen on the server, that access keys should be stored as
hashes, and that request data should keep a snapshot of the account information used at the time
of submission. Approval and execution workflows demonstrate why business status, actor snapshots,
reported occurrence time, server recording time, and optimistic-concurrency tokens must remain
separate. The next planned milestone is Excel output and reporting.

## Delivery progress

- [x] Solution, PostgreSQL, Entity Framework, and Identity foundation
- [x] Financial domain model and initial migration
- [x] Administrator/accountant login and authorization policies
- [x] Employee credential and access management
- [x] Employee request submission, attachments, tracking, and history
- [x] Administrator request dashboard and approval/rejection workflow
- [x] Confirmed Day 4 execution recording, queue/history, and audit workflow
- [ ] Policy-dependent unsuccessful closure and reported-time correction operations
- [ ] Excel outputs and reporting (Day 5)
- [ ] Persian RTL/mobile refinement and integrated workflow testing (Day 6)
- [ ] Integration/security testing and production deployment

## Scope boundaries

This application records internal authorization and execution. It does not initiate bank
transfers, automatically approve requests, replace accounting software, or provide a general
accounting ledger.

Historical retention, production backup policy, and deployment hardening remain to be agreed
before release. Unsuccessful-item closure permissions, completed-transfer retention, Sepidar
reference handoff, and permitted reported-time corrections are intentionally not implemented until
the business rules are decided.
