# Purchase Approval Program

**Internal payment request, approval, and transaction tracking system for Novin Abzian**

A self-hosted ASP.NET Core web application under development to help a company record outgoing payment requests, obtain documented management decisions, and track payment execution.

The project covers more than purchasing: the intended workflow also includes service payments, reimbursements, advances, and other money leaving the company. It is being developed as a practical internal tool and a hands-on C# / ASP.NET Core learning project.

> **Development status (October 2026):** Authentication, employee access management, the initial database schema, and an employee payment-request form with basic validation are implemented. **The form does not yet save or submit requests.** Approval, accounting, and reporting workflows are not operational. This application is **not production-ready**.

## Intended workflow

1. An administrator creates an employee profile and issues an access key.
2. The employee signs in, reviews their prefilled identity details, and prepares a payment request explaining who should be paid and why.
3. **Planned:** The employee submits one or more expense lines, relevant payment details, and supporting documents.
4. **Planned:** Management reviews the request and approves or rejects it, with the decision and responsible administrator recorded.
5. **Planned:** An accountant or authorized administrator records the outcome of the actual payment.
6. **Planned:** The system provides traceable status history, printable receipts, and accounting-friendly exports.

The application is designed to **document authorization and execution**, not to initiate bank transfers or automatically approve spending.

## Implemented features

### Application foundation
- ASP.NET Core 10 Razor Pages application and .NET solution
- Entity Framework Core with PostgreSQL 17 support
- ASP.NET Core Identity for privileged accounts and role-based authorization policies
- Separate cookie-based sessions for privileged users and employees
- Initial Entity Framework Core migration (`InitialCreate`)
- xUnit test project, with tests for employee access and request-page behavior

### Authentication and employee management
- Administrator/accountant credential sign-in page (`/account/login`) backed by ASP.NET Core Identity
- Administrator bootstrap command for creating the first administrator account
- Administrator-only employee management pages for creating and listing employees
- Employee profile data: name, username, phone, department, branch, and job title
- Cryptographically generated employee access keys; only password hashes are stored
- One-time display of new or regenerated keys
- Employee key rotation, disablement, and revocation, including session invalidation
- Employee sign-in using an access key (`/`), dashboard (`/employee`), and sign-out
- Access checks that reject inactive, revoked, or outdated employee sessions

### Payment request foundation
- Database models for requests, expense lines, attachments, approval decisions, transfer outcomes, transfer audit records, and an item catalogue
- Database relationships and constraints, including a request-total limit of **10,000,000,000 Iranian rials**
- Initial employee payment-request page (`/employee/requests/create`)
- Read-only requester profile loaded from the database, rather than accepted from submitted form fields
- Basic server-side validation for payee name, payment reason, and an optional account/card number
- A **Check details** action that confirms valid input **without creating a payment request**

**Important distinction:** The database can represent payment requests and their related records, but a complete request-saving, approval, or payment-execution service has **not** been implemented.

## Technology stack

| Area | Technology |
| --- | --- |
| Language | C# |
| Framework | ASP.NET Core 10, Razor Pages |
| Authentication | ASP.NET Core Identity; custom employee access-key authentication |
| Data access | Entity Framework Core 10, Npgsql |
| Database | PostgreSQL 17 |
| UI foundation | Bootstrap, HTML, CSS, Razor |
| Tests | xUnit, .NET Test SDK |
| Local infrastructure | Docker Compose |

## Repository structure

```text
src/PurchaseApproval.Web/           ASP.NET Core web application
  Data/                              DbContext and migrations
  Models/                            Domain and identity models
  Pages/                             Razor Pages for account, admin, and employee areas
  Security/                          Authentication and authorization helpers
  Services/                          Employee keys, profiles, role setup, admin bootstrap
tests/PurchaseApproval.Web.Tests/   Automated tests
compose.yaml                        Local PostgreSQL container
.env.example                        Example database password environment variable
PurchaseApproval.slnx               .NET solution
```

## Local development setup

### Prerequisites
- **.NET 10 SDK**
- **Docker Desktop** (or your own PostgreSQL 17 instance)
- The **dotnet-ef** CLI tool compatible with EF Core 10
- Git

The following commands use PowerShell and assume you are in the repository root.

### 1. Clone and restore

```powershell
git clone https://github.com/Alirj81/Purchase-Approval-Program.git
cd Purchase-Approval-Program
dotnet restore
```

If `dotnet ef` is not installed, install a compatible EF Core 10 CLI tool. A local tool manifest is not currently part of this repository, so `dotnet tool restore` is not required.

### 2. Configure PostgreSQL

```powershell
Copy-Item .env.example .env
```

Edit `.env` and replace the example `POSTGRES_PASSWORD` with a development password. This password must match the one in the connection string below. Do not commit `.env` or actual secrets.

Start the database:

```powershell
docker compose up -d
```

### 3. Configure the connection string

```powershell
dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=localhost;Port=5432;Database=purchase_approval_dev;Username=purchase_approval_app;Password=YOUR_PASSWORD" --project src/PurchaseApproval.Web
```

Replace `YOUR_PASSWORD` with the password from `.env`. User secrets are for local development and are not a production secret-management solution.

### 4. Apply the database migration

```powershell
dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
```

The current repository contains an initial migration named `20261013211800_InitialCreate`.

### 5. Create the first administrator

Set development bootstrap credentials. Choose a strong password (at least 12 characters, including uppercase, lowercase, a number, and a symbol).

```powershell
dotnet user-secrets set "BootstrapAdmin:Username" "admin" --project src/PurchaseApproval.Web
dotnet user-secrets set "BootstrapAdmin:FullName" "Development Administrator" --project src/PurchaseApproval.Web
dotnet user-secrets set "BootstrapAdmin:Password" "REPLACE_WITH_STRONG_PASSWORD" --project src/PurchaseApproval.Web

dotnet run --project src/PurchaseApproval.Web -- bootstrap-admin
```

The bootstrap command creates the administrator only when the chosen username does not already exist. Remove the temporary bootstrap values from user secrets when they are no longer needed.

### 6. Run the web application

```powershell
dotnet run --project src/PurchaseApproval.Web --launch-profile https
```

Development addresses:

- HTTPS: `https://localhost:7054`
- HTTP: `http://localhost:5083`

If your development HTTPS certificate is not trusted, use `dotnet dev-certs https --trust`.

Sign in as an administrator at `/account/login`. The employee-management page is `/Admin/Employees`. Administrators can create an employee and issue an access key; employees use that key on the home page to access their dashboard and the **Check details** form.

## Build and tests

```powershell
dotnet build
dotnet test
```

The repository includes automated tests. Passing build and test results should be verified locally or in CI; their presence alone does not establish production readiness.

## Development roadmap

The checklist tracks implementation in the repository, not merely the existence of a data model.

### Phase 1 — Foundation and database
- [x] .NET 10 solution and Razor Pages project
- [x] PostgreSQL / Entity Framework Core configuration
- [x] ASP.NET Core Identity and authorization policies
- [x] Financial domain models, relationships, and initial migration
- [x] Automated test project

### Phase 2 — Authentication and employee access
- [x] Privileged-user sign-in/sign-out and administrator bootstrap
- [x] Employee creation and profile storage
- [x] Access-key generation and employee sign-in
- [x] Key regeneration, disablement, revocation, and session invalidation
- [x] Read-only employee profile lookup for the request form
- [ ] Privileged-account provisioning and role-management UI beyond initial bootstrap

### Phase 3 — Payment requests
- [x] Basic employee request page and input validation
- [ ] Add/remove multiple expense rows and calculate totals
- [ ] Enforce payment limits in the submission workflow (database constraint already exists)
- [ ] Secure upload of invoices and attachments
- [ ] Save requests, snapshot requester information, and generate unique references/tracking links
- [ ] Enforce request-submission rate limits and implement status tracking
- [ ] Complete Persian, right-to-left request interface

### Phase 4 — Management approval
- [ ] Administrator review dashboard and request details
- [ ] Approve/reject actions with reasons, timestamps, and decision attribution
- [ ] Administrator-created requests
- [ ] Searchable request history

### Phase 5 — Accounting and payment execution
- [ ] Accountant workflow and transfer outcome recording
- [ ] Payment method, reference, amount, and transaction date/time fields
- [ ] Failed-transfer handling, manual closure, and audit history
- [ ] Concurrency and permission safeguards for financial changes

### Phase 6 — Documents, reports, and deployment
- [ ] Printable approval/rejection receipts and Excel exports
- [ ] Date-range accounting reports
- [ ] Responsive Persian RTL interface with Jalali date display
- [ ] End-to-end, security, and integration testing
- [ ] Retention policy, production configuration, backups, and internal-server deployment

## Planned interface

The final interface will be **Persian-first**, right-to-left, responsive on mobile and desktop, and simple for nontechnical users. It should avoid asking employees to re-enter information already stored by their administrator. Current development pages are primarily **English** and do not yet represent the final user experience.

## Scope and limitations

This system is intended for internal **outgoing payment authorization and tracking**, including purchases and other legitimate business payments.

It is **not** intended to initiate bank transfers, replace Sepidar or another accounting platform, automatically verify warehouse inventory, automatically approve requests, or function as a complete accounting ledger.

Financial record-retention rules and production security requirements must be agreed upon before company deployment.

## License and usage

No open-source license is currently included. Public access to this repository does not itself grant permission to redistribute or commercially reuse its contents. Usage and ownership are subject to applicable agreements.

---

**Organization:** Novin Abzian  
**Repository:** https://github.com/Alirj81/Purchase-Approval-Program  
**Status:** Active development — authentication and request-form foundation  
**README updated:** October 17, 2026
