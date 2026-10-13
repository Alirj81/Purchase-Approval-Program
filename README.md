# Purchase Approval Program

**Internal Payment Request, Approval & Transaction Tracking System**

A self-hosted web application for managing company payment requests, formal management approvals, accountant execution records, and financial accountability.

**Organization:** Novin Abzian  
**Technology:** C# / ASP.NET Core 10 / PostgreSQL  
**Development Stage:** Backend Foundation & Database Implementation  
**Status:** Active Development — Not Production Ready

---

## 1. Project Overview

The Purchase Approval Program is an internal web application being developed to formalize and simplify how outgoing company payments are requested, reviewed, authorized, and recorded.

The system is intended to replace informal payment authorization procedures with a structured digital workflow that connects employees, company management, and accounting personnel.

Although initially conceived as a purchase approval tool, the application's scope has expanded to cover **any outgoing company payment**, including purchases, services, reimbursements, advances, and other authorized expenses.

Its primary objective is to ensure that every payment has a documented business justification, a responsible requester, an identifiable management decision, and a traceable financial execution record.

The application will operate on the company's own infrastructure and provide a Persian-language, right-to-left interface accessible through desktop and mobile browsers.

### Core Principles

- Human-controlled financial authorization
- Clear separation between approval and payment execution
- Minimal complexity for employees
- Role-based access and responsibility
- Complete traceability of financial decisions
- Accurate historical records
- Secure internal deployment
- Practical accounting exports and printable documents

**Important:** The software will document payment authorization and execution. It will not independently authorize payments, transfer money, or replace accounting software.

## 2. User Roles

The system is designed around three primary user roles.

### Employee

Employees are authorized personnel who can submit payment requests.

Planned capabilities:

- Access the application using administrator-issued credentials
- Submit requests with multiple expense entries
- Have personal information automatically populated from their registered employee profile
- Provide payment justifications and supporting documents
- Track the status of previously submitted requests
- View management decisions

Employees will not need traditional self-registration. Access will be controlled centrally.

### Administrator / Manager

Administrators represent authorized company management and have responsibility for formal approval decisions.

Planned capabilities:

- Create, manage, disable, and revoke employee access credentials
- Manage authorized administrative and accounting users
- Review pending payment requests
- Approve or reject entire requests
- Record optional rejection explanations
- Submit payment requests themselves
- View request and transaction history
- Track pending or unsuccessful financial transfers
- Confirm completed payments when management performs the transaction directly
- Review and close unsuccessful transaction records
- Access administrative reports and exports

Administrators may be authorized to execute payments, but **approval authority remains separate from the act of executing a transaction**.

### Accountant

Accountants are responsible for recording the financial execution of approved requests.

Planned capabilities:

- View approved requests awaiting payment
- Mark transactions as transferred or unsuccessful
- Record payment methods and reference details
- Enter or confirm the actual transaction date and time
- Record unsuccessful transaction explanations
- Maintain visibility of outstanding payment records
- Review financial execution history
- Access authorized accounting reports and exports

Accountants do not have management approval or rejection authority.

## 3. Payment Request Workflow

The business process consists of four main stages.

### Stage 1 — Request Submission

An authorized employee or administrator creates a payment request.

The request includes:

- Requester information
- Payment recipient's name
- Payment justification
- One or more expense entries
- Amounts in Iranian rials (IRR)
- Optional recipient account/card information
- Optional supporting invoices or documents

Each expense entry contains a description, amount, invoice indicator, and optional remarks.

The application will automatically calculate the total amount from the entered expense rows.

**The maximum permitted total for one request is 10,000,000,000 IRR.**

Recipient bank details may be omitted, but the interface should warn the requester that this information may be needed for payment execution.

A unique request reference and private tracking identifier will be generated when a request is submitted.

Employee submissions are intended to be limited to 10 requests per day.

### Stage 2 — Management Review

New requests enter the administrator approval queue.

An authorized administrator examines the entire request, including its financial amount, justification, recipient, and supporting information.

The administrator can either:

- **Approve:** Authorize the complete request for payment.
- **Reject:** Decline the complete request, optionally recording an explanation.

Partial approval of individual expense rows is outside the current scope.

Every decision must preserve:

- Request reference number
- Decision type
- Responsible administrator's identity
- Administrator's name at the time of the decision
- Server-recorded decision timestamp
- Rejection reason, when provided

The request decision and its financial execution status are maintained independently.

### Stage 3 — Payment Execution

Following approval, the request becomes available for execution by an accountant or authorized administrator.

The financial execution record supports three statuses:

| Status | Meaning | Record Behavior |
|---|---|---|
| Awaiting Transfer | The approved payment has not yet been completed | Remains open |
| Transferred | Payment has been successfully completed | Automatically closed |
| Unsuccessful | A payment attempt was unsuccessful | Flagged for attention; can be manually closed |

The planned interface will highlight unsuccessful transactions with a red warning indicator.

Successful transactions should record:

- Identity of the accountant or administrator
- Name and role at the time of execution
- Payment method
- Actual transaction date and time
- Server-recorded confirmation timestamp
- Relevant transaction reference information

The date and time entry will default to the current time on the user's device, while allowing manual correction when a transaction occurred earlier.

The application must preserve the distinction between the **reported transaction time** and the **time the server recorded the action**.

Supported payment methods in the data model:

- Cash
- Bank transfer
- Cheque
- Other

Additional information, including bank details, cheque details, and external transaction references, can be recorded where appropriate.

### Stage 4 — Record Closure & History

Successfully transferred payments are closed automatically in the planned workflow.

Unsuccessful transfers remain visible until an authorized administrator or accountant resolves or manually closes the record.

Manual closure must preserve the responsible user's identity, time of closure, and any provided explanation.

Transaction corrections and status changes should produce audit entries rather than silently overwriting historical activity.

Administrators will be able to review the complete financial lifecycle of each request.

## 4. Request and Financial Statuses

The application separates management authorization from financial execution.

**Approval statuses:**

- `Pending`
- `Approved`
- `Rejected`

**Transfer statuses:**

- `AwaitingTransfer`
- `Transferred`
- `Unsuccessful`

For example, a request may be approved by management while its transaction remains awaiting transfer.

This distinction is essential for tracking authorized payments that have not yet been financially executed.

## 5. Reporting and Document Generation

The planned application will provide Excel-based exports for company documentation and accounting procedures.

### Individual Request Documents

A request's exported confirmation record should contain:

- Unique request reference
- Requester and recipient information
- Full expense breakdown
- Total authorized amount
- Management approval or rejection decision
- Approving/rejecting administrator's name
- Decision date and time
- Rejection explanation, if provided
- Payment execution status
- Accountant or administrator responsible for execution
- Actual transaction date and time
- Relevant payment references

A formatted administrative signature or approval attribution may be included. Any cryptographic digital-signature functionality would require separate implementation and verification.

### Accounting Reports

The accounting interface is intended to support:

- Date-range filtering
- Lists of approved requests
- Pending financial executions
- Successful transactions
- Unsuccessful transactions
- Excel export of relevant financial records

Reporting will preserve information needed to distinguish a management authorization from a completed payment.

## 6. Security & Access Management

The application uses a two-part authentication architecture.

### Employee Access

Employees will receive administrator-generated usernames and access keys.

The employee model already defines support for:

- User profiles
- Hashed access keys
- Credential versioning
- Active, disabled, and revoked access states
- Authentication timestamps
- Credential lifecycle records

The associated login, key generation, and revocation workflows remain to be implemented.

### Administrator & Accountant Authentication

Privileged accounts use the ASP.NET Core Identity foundation.

Current configuration includes:

- Identity-based user management
- Password complexity requirements
- Account lockout settings
- Entity Framework Core identity storage
- Role-based authorization policies

The application currently defines these authorization policies:

| Policy | Authorized Roles |
|---|---|
| `CanDecideRequests` | Administrator |
| `CanExecutePayments` | Administrator, Accountant |

Additional planned security measures include:

- HTTPS-only production access
- Secure session handling
- Server-side input validation
- Request rate limiting
- Protection against unauthorized record access
- Secure file upload validation
- Anti-forgery protection
- Audit logging
- Secure storage of secrets and credentials

**Authorization configuration is not equivalent to a completed or security-tested authentication workflow.**

## 7. Current Technical Architecture

The project uses an ASP.NET Core Razor Pages architecture supported by PostgreSQL and Entity Framework Core.

| Component | Technology | Current State |
|---|---|---|
| Language | C# | In use |
| Framework | .NET 10 / ASP.NET Core | Initialized |
| Web architecture | Razor Pages | Initialized |
| Database | PostgreSQL | Integrated through EF Core |
| ORM | Entity Framework Core 10 | Configured |
| Database provider | Npgsql | Configured |
| Identity | ASP.NET Core Identity | Foundation configured |
| Authorization | Role-based policies | Configured |
| Testing | xUnit | Initial tests added |
| Database migrations | EF Core Migrations | Initial migration created |
| Excel generation | ClosedXML (proposed) | Not implemented |
| User interface | Persian RTL responsive design | Planned |
| Hosting | Company-managed internal server | Not deployed |

### Current Solution Structure

Repository: `Alirj81/Purchase-Approval-Program`

Main components:

- `PurchaseApproval.slnx` — .NET solution
- `src/PurchaseApproval.Web/` — ASP.NET Core web application
- `src/PurchaseApproval.Web/Models/` — Domain and identity models
- `src/PurchaseApproval.Web/Data/` — Database context and migrations
- `src/PurchaseApproval.Web/Security/` — Application roles and authorization policies
- `src/PurchaseApproval.Web/Pages/` — Razor Pages
- `tests/PurchaseApproval.Web.Tests/` — xUnit test project
- `dotnet-tools.json` — Local .NET tooling configuration
- `.env.example` — Example PostgreSQL environment setting

## 8. Implemented Data Foundation

The project has progressed beyond its initial documentation stage.

The repository now includes domain models and a first Entity Framework Core database migration.

### Defined Models

| Model | Purpose |
|---|---|
| `ApplicationUser` | Administrator/accountant identity and profile |
| `Employee` | Employee profiles and access credential metadata |
| `PaymentRequest` | Payment request and requester information |
| `PaymentRequestLine` | Individual expense entries |
| `PaymentRequestAttachment` | Supporting files and metadata |
| `ApprovalDecision` | Management decisions and attribution |
| `TransferOutcome` | Financial execution and closure information |
| `TransferAuditEntry` | History of transaction-related actions |
| `ItemCatalogueEntry` | Reusable expense/item name suggestions |

### Database Design

The current database configuration includes structural constraints for:

- Unique payment request references
- Unique hashed tracking identifiers
- Positive expense amounts
- Maximum request total of 10 billion rials
- Payment request ownership by exactly one requester account type
- Request and transfer relationships
- Restricted deletion of referenced user records
- Ordered expense entries
- File attachments limited to 5 MiB each
- Identity and audit relationships
- Optimistic concurrency fields for selected financial records

Requester details and responsible user names are preserved as snapshots so that subsequent profile changes do not automatically alter the identities recorded in historical payment records.

These are database and model-level foundations; the complete business workflows and user interfaces are still under development.

## 9. Development Setup

### Prerequisites

- .NET 10 SDK
- PostgreSQL
- Git
- Visual Studio, VS Code, or another compatible .NET development environment

### Clone the Repository

`git clone https://github.com/Alirj81/Purchase-Approval-Program.git`

`cd Purchase-Approval-Program`

### Restore Dependencies

`dotnet restore`

`dotnet tool restore`

### Configure the Database

A local PostgreSQL instance and a valid `DefaultConnection` configuration are required.

The connection string should be supplied through .NET user secrets or environment variables rather than committed into source control.

For example, the application can read the environment variable:

`ConnectionStrings__DefaultConnection`

Alternatively, use the .NET user-secrets configuration key:

`ConnectionStrings:DefaultConnection`

The provided `.env.example` is an example configuration file. A `.env` file is not automatically loaded by the application unless the execution environment or additional tooling explicitly supports it.

### Apply the Initial Database Migration

`dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web`

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
