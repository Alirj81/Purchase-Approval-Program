# Purchase Approval Program

**An internal, web-based purchase request and management approval system.**

**Project Status:** Planning & Requirements Definition — Pre-Development  
**Version:** 0.1.0 (Planned MVP)  
**Development:** Not yet started

## 1. Project Overview

The Purchase Approval Program is a planned internal web application designed to simplify, formalize, and document purchase approval procedures within a company.

The system will provide authorized employees with a structured method for submitting purchase requests, while allowing management to review, assess, approve, or reject these requests through a centralized administration dashboard.

The primary objective is to replace informal purchase authorization procedures with a transparent, traceable, and accountable digital workflow.

The application is intended to be hosted on the company's own server and accessible through modern desktop and mobile browsers.

## 2. Project Objectives

The system aims to:

- Establish a standardized purchase request procedure.
- Ensure that purchases receive formal management authorization.
- Reduce reliance on informal communications and verbal approvals.
- Provide managers with sufficient time and information to assess purchasing needs.
- Maintain a traceable history of requests and decisions.
- Identify the responsible decision-maker for every approval or rejection.
- Enable employees to track their submitted requests.
- Support exporting approved requests to Excel.
- Limit unnecessary administrative complexity.
- Protect internal company information through controlled access.

**Core Principle:** The application supports management decision-making rather than automating purchasing decisions.

## 3. Planned Functionality

### 3.1 Employee Access Management

The application will use a controlled access system for authorized company personnel.

Instead of requiring traditional employee accounts with self-registration, administrators will issue individual access credentials.

Planned behavior:

- Each authorized employee receives a username and a securely generated access key.
- The username and access key provide access to the purchase request form.
- Access keys remain valid indefinitely unless an administrator revokes, disables, or regenerates them.
- Administrators can add, disable, or remove employee access.
- Revoking or regenerating a credential must invalidate associated active sessions.
- Browser sessions will have their own expiration rules independently of access key validity.

Access credentials will be treated as sensitive authentication information, with secure storage, server-side validation, and access controls.

### 3.2 Purchase Request Submission

Authorized employees will be able to submit purchase requests through a responsive web form.

The planned request information includes:

- Employee identification
- Department or relevant staff information
- Requested item or product
- Requested quantity
- Estimated unit price
- Purchase justification or description
- Priority level
- Submission date and time (automatically recorded)

The system will enforce form validation and a maximum of 10 requests per authorized employee per day.

Each successfully submitted request will receive a unique reference number.

### 3.3 Request Tracking

Every purchase request will have a unique tracking identifier and a private tracking link.

Employees will be able to check their request's current status without contacting management directly.

Planned request statuses:

| Status | Description |
|---|---|
| Pending | Awaiting management review |
| Approved | Formally authorized by management |
| Rejected | Declined by management |
| Cancelled / Expired | No longer active without approval |

Tracking links must be sufficiently protected against unauthorized access or enumeration.

### 3.4 Management Approval Dashboard

An authenticated administration interface will enable authorized managers and company owners to evaluate submitted purchase requests.

The dashboard will provide:

- A centralized view of submitted requests
- Request details and purchase justifications
- Submission timestamps and employee information
- Request status and priority
- Approval and rejection actions
- Recorded reasons for rejected requests
- Identification of the responsible decision-maker
- Timestamps for management decisions

Managers will independently assess the necessity of a purchase, including stock availability and operational requirements.

**Approval decisions will not be automated.**

The application will record the formal decision and its associated information.

### 3.5 Excel Export

Authorized administrators will be able to export approved purchase requests into Excel files.

This feature is intended to assist with internal purchasing procedures, documentation, and subsequent administrative work.

Excel exports will be restricted to appropriately authorized administrative users.

### 3.6 Data Retention

The system is planned to use a limited retention policy to reduce unnecessary storage of historical request information.

- Pending requests remain available until resolved, cancelled, or expired.
- Completed requests are retained for 90 days following their final decision.
- Eligible historical records are removed through a scheduled cleanup mechanism.

The final implementation will consider applicable organizational record-retention requirements.

## 4. Planned Technology Stack

The initial development roadmap proposes the following technologies:

| Component | Proposed Technology |
|---|---|
| Primary language | C# |
| Backend framework | ASP.NET Core 10 |
| Web architecture | Razor Pages |
| Frontend | HTML, CSS, JavaScript |
| Database | PostgreSQL |
| Database access | Entity Framework Core |
| Excel generation | ClosedXML |
| Testing | xUnit |
| Version control | Git / GitHub |
| Deployment | Company-hosted server with HTTPS |

These technologies represent the initial architectural direction and may be refined during development.

## 5. Intended System Workflow

**Step 1 — Authentication**

An authorized employee accesses the system using their assigned username and access key.

**Step 2 — Request Creation**

The employee completes the purchasing form, explains the business need, and submits the request.

**Step 3 — Request Registration**

The application validates and stores the request, assigns a unique reference number, and generates a private status-tracking link.

**Step 4 — Management Review**

An authorized manager accesses the administration dashboard and independently evaluates the purchasing request.

**Step 5 — Formal Decision**

The manager approves or rejects the request. The decision, responsible manager, timestamp, and any required explanation are recorded.

**Step 6 — Status Tracking**

The employee can check the decision using the request's tracking information.

**Step 7 — Export and Retention**

Approved requests can be exported by authorized administrators. Finalized requests remain stored according to the retention policy.

## 6. Security Considerations

Security is a fundamental requirement because the application will handle internal company information and formal management decisions.

The implementation will prioritize:

- Secure generation and storage of access credentials
- Encrypted communication through HTTPS
- Server-side validation of all submitted data
- Authenticated management and administration interfaces
- Role-based authorization
- Session expiration and immediate credential revocation
- Protection against unauthorized request access
- Secure database operations
- Request throttling and abuse prevention
- Audit records for management decisions
- Protection against common web vulnerabilities

Authentication and authorization mechanisms will be implemented and tested before deployment.

## 7. Development Roadmap

The project will follow an incremental development approach.

### Phase 1 — Project Foundation
- [ ] Initialize the ASP.NET Core solution
- [ ] Establish the repository structure
- [ ] Configure the development environment
- [ ] Set up database connectivity

### Phase 2 — Data Models and Access Control
- [ ] Design database entities and relationships
- [ ] Implement administrator authentication
- [ ] Implement employee credential generation
- [ ] Implement access key validation and revocation
- [ ] Establish authorization rules

### Phase 3 — Employee Request System
- [ ] Develop the purchase request form
- [ ] Implement server-side validation
- [ ] Enforce daily submission limits
- [ ] Generate unique request references
- [ ] Implement secure request tracking

### Phase 4 — Management Dashboard
- [ ] Develop the request management interface
- [ ] Implement approval and rejection workflows
- [ ] Record decisions and associated metadata
- [ ] Implement request filtering and status management

### Phase 5 — Export and Lifecycle Management
- [ ] Implement Excel export
- [ ] Implement request retention policies
- [ ] Implement scheduled data cleanup
- [ ] Finalize audit and error-handling behavior

### Phase 6 — Testing and Deployment
- [ ] Write unit and integration tests
- [ ] Test authorization and security behavior
- [ ] Validate mobile and desktop responsiveness
- [ ] Prepare the company server environment
- [ ] Configure HTTPS and deployment
- [ ] Conduct final testing before internal release

*All development tasks are currently pending.*

## 8. Project Scope and Limitations

The first version will focus on purchase requests, formal managerial decisions, and administrative traceability.

The following capabilities are outside the initial scope:

- Automated purchasing decisions
- Automated inventory verification
- Direct payment processing
- Replacement of existing accounting software
- Complete inventory management
- Automated purchasing or supplier ordering

Potential future improvements may include product autocomplete, duplicate-request warnings, notifications, and additional reporting functionality.

These enhancements will be evaluated after the core workflow is functional.

## 9. Current Development Status

**The project is currently in its documentation and planning stage.**

At this point:

- The initial business problem has been identified.
- The core functional requirements have been outlined.
- The intended approval workflow has been defined.
- Initial authentication and security requirements have been established.
- A preliminary technology stack has been selected.
- A phased development roadmap has been prepared.

No application features have been implemented, and no production-ready software is available.

This repository will serve as the central location for source code, technical documentation, development history, and future releases.

## 10. Project Vision

The Purchase Approval Program is intended to demonstrate how a relatively simple internal application can improve operational accountability.

Its value lies not in replacing human judgment, but in ensuring that organizational decisions are documented, traceable, and accessible to the appropriate people.

The long-term goal is a reliable, maintainable, and secure purchasing authorization workflow that can evolve alongside the organization's operational requirements.

---

**Project:** Purchase Approval Program  
**Initial Documentation:** October 2026  
**Status:** Pre-Development  
**License:** Not yet specified
