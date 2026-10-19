# Purchase Approval Programme

An ASP.NET Core web app for handling internal payment requests.

I started this project to give employees, managers and accounting staff one place to follow a request. Instead of passing a form around and asking who approved it, the request keeps its decision and payment history together.

This is also a learning project for me. I’m using it to practise C#, backend development, database design, authentication and testing while working on a practical business problem.

## How it works

1. An employee signs in with a username and an administrator-issued access key.
2. They enter what the payment is for, add expense rows and optionally attach supporting files.
3. An administrator reviews the request and approves or rejects it. Administrators can also submit their own requests.
4. After an approved payment is attempted outside the app, an accountant or administrator records whether it was transferred or unsuccessful.
5. The request stays in the history so it can be checked later.

The app records payment outcomes. It does not send money or connect to a bank or accounting system.

## What’s included

- Separate access for employees, administrators and accountants.
- Request history for employees and a searchable company history for administrators.
- Approval confirmation and visible self-approval when an administrator approves their own request.
- Payment records for cash, bank transfer, cheque and other methods.
- English Excel exports for individual decisions and consolidated approved requests.
- Persian and English interfaces, with Persian as the default.
- Tracking links that show limited status information.

Amounts currently use Iranian rials (IRR). Changing the interface language does not change the currency.

Payment outcome and closure are kept separate. A successful transfer record closes automatically after 24 hours; an unsuccessful one closes after 48 hours. Closing an item removes it from the open execution queue, but keeps its history. It does not mean that a payment succeeded.

## Built with

- C# and ASP.NET Core Razor Pages
- Entity Framework Core
- PostgreSQL
- Docker Compose for the local database
- Automated unit, database integration and browser tests

I kept the application server-rendered with Razor Pages rather than adding a separate frontend application.

## Running locally

You need the .NET SDK version used by the project, Docker with Compose, and the project’s local database and authentication configuration.

From the repository root, start the development database:

```bash
docker compose up -d
```

After configuring the development database connection, apply the migrations:

```bash
dotnet ef database update --project src/PurchaseApproval.Web --startup-project src/PurchaseApproval.Web
```

Then start the web app:

```bash
dotnet run --project src/PurchaseApproval.Web
```

Use the address printed in the terminal. The initial administrator account needs to be created through the project’s bootstrap procedure; there is no public registration page or shared default login.

Keep credentials, database backups and real payment information out of Git. Use a separate disposable database for integration and browser tests.

## Current state

The main request, approval, payment-recording and reporting flows have been implemented and tested locally. There is still work to do on deployment scripts, backup and restore checks, and testing on the intended Windows Server/IIS setup and physical devices.

I’m still developing this project. Local test results do not mean it is ready to handle real company payments yet.

Technical changes are recorded in [the changelog](docs/CHANGELOG.md).
