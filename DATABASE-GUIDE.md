# Local Database Guide

This guide explains how to inspect and manage the local PostgreSQL database used by the Purchase
Approval Program. It is for development and learning. Do not run direct data-changing commands
against a production database unless there is an approved backup, change process, and review.

## 1. What is running locally

The project uses PostgreSQL 17 in Docker. The database service is named `database`, its container
is `purchase-approval-db`, and the development database is `purchase_approval_dev`.

Run all PowerShell commands in the project root:

```powershell
cd "C:\Users\arjam\Documents\Novin Abzian\Purchase Approval Program"
```

Start the database and confirm it is healthy:

```powershell
docker compose up -d
docker compose ps
```

The `STATUS` column should include `healthy` before you start the web application.

## 2. Two safe ways to manage data

### Preferred: use the web application

Use the application for normal tasks such as creating employees, changing access status, submitting
requests, and approving requests. Application code validates input, applies authorization, records
snapshots, and protects credentials correctly.

Start the application:

```powershell
dotnet run --project src/PurchaseApproval.Web
```

Then browse to `http://localhost:5083`.

### Advanced: use PostgreSQL directly

Use SQL when learning, inspecting a problem, making a controlled local-data correction, or checking
the result of an application action. Direct SQL bypasses application rules, so start with `SELECT`
queries and use a transaction before changing data.

## 3. Open the PostgreSQL command prompt

The following command opens an interactive `psql` prompt inside the Docker database container:

```powershell
docker compose exec database psql -U purchase_approval_app -d purchase_approval_dev
```

At the `purchase_approval_dev=#` prompt, type SQL commands followed by a semicolon (`;`). Exit with:

```sql
\q
```

You can also run a single command from PowerShell:

```powershell
docker compose exec -T database psql -U purchase_approval_app -d purchase_approval_dev -c "SELECT now();"
```

## 4. PostgreSQL and `psql` basics

### Important naming rule in this project

Entity Framework created table and column names with capital letters, for example `ApplicationUsers`
and `FullName`. PostgreSQL folds unquoted names to lowercase, so use double quotes around project
table and column names:

```sql
SELECT "Id", "UserName", "FullName"
FROM "ApplicationUsers";
```

`psql` commands start with a backslash and do not need a semicolon. Useful commands include:

```sql
\dt                         -- List tables.
\d "Employees"              -- Describe the Employees table, columns, indexes, and constraints.
\d "PaymentRequests"        -- Describe the requests table.
\x on                        -- Display wide rows vertically; use \x off to restore normal output.
\timing on                   -- Show how long each query takes.
\q                          -- Exit psql.
```

## 5. Project schema overview

The most important tables are:

| Table | Purpose |
| --- | --- |
| `ApplicationUsers` | ASP.NET Identity users, including administrators and accountants. |
| `AspNetRoles`, `AspNetUserRoles` | Identity roles and their assignment to users. |
| `Employees` | Employees who sign in using a generated access key. |
| `PaymentRequests` | Header record for each submitted payment request. |
| `PaymentRequestLines` | Expense rows belonging to a payment request. |
| `PaymentRequestAttachments` | Supporting-file metadata and bytes. |
| `ApprovalDecisions` | The one final approval or rejection decision per request. |
| `TransferOutcomes`, `TransferAuditEntries` | External payment outcomes and their immutable audit snapshots. |
| `ItemCatalogueEntries` | Reusable item names and usage counts. |

The entity definitions are in `src/PurchaseApproval.Web/Models`, the mapping and constraints are
in `src/PurchaseApproval.Web/Data/ApplicationDbContext.cs`, and database migrations are in
`src/PurchaseApproval.Web/Data/Migrations`.

## 6. Read-only queries to learn the data

### List application users and their roles

```sql
SELECT
    u."Id",
    u."UserName",
    u."FullName",
    u."IsActive",
    r."Name" AS "Role"
FROM "ApplicationUsers" AS u
LEFT JOIN "AspNetUserRoles" AS ur ON ur."UserId" = u."Id"
LEFT JOIN "AspNetRoles" AS r ON r."Id" = ur."RoleId"
ORDER BY u."Id";
```

### List employees without exposing credential hashes

```sql
SELECT
    "Id",
    "Username",
    "FullName",
    "Department",
    "Branch",
    "JobTitle",
    "AccessStatus",
    "CreatedAtUtc"
FROM "Employees"
ORDER BY "Id";
```

Do not select, copy, or edit `AccessKeyHash` or `PasswordHash`. They are one-way password hashes,
not recoverable passwords or access keys.

### List payment requests and their current state

```sql
SELECT
    "Id",
    "ReferenceNumber",
    "RequesterFullNameSnapshot",
    "PayeeName",
    "TotalAmountRials",
    "Status",
    "SubmittedAtUtc"
FROM "PaymentRequests"
ORDER BY "SubmittedAtUtc" DESC;
```

`PaymentRequestStatus` values are `1 = Pending`, `2 = Approved`, and `3 = Rejected`.

### Review one request and its expense rows

Replace `1` with the request ID you want to inspect:

```sql
SELECT
    r."ReferenceNumber",
    r."RequesterFullNameSnapshot",
    r."PayeeName",
    r."Justification",
    r."TotalAmountRials",
    r."Status",
    l."DisplayOrder",
    l."Description",
    l."AmountRials",
    l."HasInvoice",
    l."Remarks"
FROM "PaymentRequests" AS r
JOIN "PaymentRequestLines" AS l ON l."PaymentRequestId" = r."Id"
WHERE r."Id" = 1
ORDER BY l."DisplayOrder";
```

### Review decisions and the administrator who made them

```sql
SELECT
    r."ReferenceNumber",
    d."DecisionType",
    d."DecidedByFullNameSnapshot",
    d."RejectionReason",
    d."DecidedAtUtc"
FROM "ApprovalDecisions" AS d
JOIN "PaymentRequests" AS r ON r."Id" = d."PaymentRequestId"
ORDER BY d."DecidedAtUtc" DESC;
```

`ApprovalDecisionType` values are `1 = Approved` and `2 = Rejected`.

## 7. Safe change workflow

Before changing data directly:

1. Make a backup.
2. Run a `SELECT` using the same `WHERE` condition to confirm exactly which rows will change.
3. Start a transaction with `BEGIN`.
4. Run the `UPDATE`, `INSERT`, or `DELETE`.
5. Review the result with `SELECT`.
6. Use `COMMIT` only when correct; otherwise use `ROLLBACK`.

Example: correct an employee's department in the local development database.

```sql
-- First inspect the exact employee.
SELECT "Id", "FullName", "Department"
FROM "Employees"
WHERE "Id" = 1;

BEGIN;

UPDATE "Employees"
SET "Department" = 'Operations'
WHERE "Id" = 1;

-- Verify before making the transaction permanent.
SELECT "Id", "FullName", "Department"
FROM "Employees"
WHERE "Id" = 1;

COMMIT;
-- Use ROLLBACK instead of COMMIT if the result is wrong.
```

If the update must not affect more than one row, use a unique key such as `"Id"` or a known unique
username. Avoid broad updates such as `UPDATE "Employees" SET "Department" = 'Operations';`.

## 8. SQL syntax cheat sheet

```sql
-- Choose columns and rows.
SELECT "Id", "FullName"
FROM "Employees"
WHERE "AccessStatus" = 1
ORDER BY "FullName";

-- Count matching rows.
SELECT COUNT(*)
FROM "PaymentRequests"
WHERE "Status" = 1;

-- Add a row. Only do this when all application and database rules are understood.
INSERT INTO "ItemCatalogueEntries" ("Name", "NormalizedName", "IsActive", "UsageCount")
VALUES ('Printer paper', 'PRINTER PAPER', TRUE, 0);

-- Change selected rows.
UPDATE "ItemCatalogueEntries"
SET "IsActive" = FALSE
WHERE "Id" = 1;

-- Delete selected rows. This is permanent unless you have a backup.
DELETE FROM "ItemCatalogueEntries"
WHERE "Id" = 1;
```

Always include a precise `WHERE` clause for `UPDATE` and `DELETE`. Test the filter with `SELECT`
first.

## 9. Work with Entity Framework migrations

Application schema changes should normally be made through C# models and Entity Framework
migrations, not by manually editing the database.

After changing a model or `ApplicationDbContext`:

```powershell
dotnet ef migrations add DescribeYourChange --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
```

Inspect pending migrations before applying them:

```powershell
dotnet ef migrations list --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
```

Use a clear migration name, such as `AddEmployeeEmergencyContact`. Review the generated migration
file before applying it. Do not edit an already-applied migration in a shared environment.

## 10. Backup and restore a local database

Create a backup file in the project folder:

```powershell
docker compose exec -T database pg_dump -U purchase_approval_app -d purchase_approval_dev > local-development-backup.sql
```

The resulting backup can contain personal and financial test data. Keep it out of source control and
delete it when it is no longer needed.

Restoring overwrites data and should only be done when you understand the consequences. For a local
development reset, prefer recreating test records through the application rather than overwriting a
database you still need.

## 11. Common problems

### `Failed to connect to 127.0.0.1:5432`

PostgreSQL is not running. Start it with `docker compose up -d`, then run `docker compose ps`.

### `address already in use` on port `5083`

Another copy of the web application is already running. Stop that terminal process with `Ctrl+C`, or
run the new instance on another port:

```powershell
dotnet run --project src/PurchaseApproval.Web --urls "http://localhost:5090"
```

### `relation does not exist`

The schema is not current. Apply migrations:

```powershell
dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
```

### `column does not exist` or names become lowercase

Use double quotes around the project's PascalCase table and column names, for example
`"PaymentRequests"` and `"SubmittedAtUtc"`.

## 12. Rules worth remembering

- Never commit `.env`, user secrets, database backups, passwords, or access keys.
- Never try to recover or replace a password by editing `PasswordHash` or `AccessKeyHash`.
- Create normal users and employees through the application where possible.
- Let the application create approval decisions; direct edits can bypass audit and concurrency rules.
- Prefer migrations for schema changes and SQL transactions for carefully scoped local-data repairs.
- Back up before any destructive action.
