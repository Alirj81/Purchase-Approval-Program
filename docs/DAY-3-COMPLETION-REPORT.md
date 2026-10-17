# Day 3 Completion Report — Employee Payment Request Submission

Date: 2026-10-17

## Delivered

- Replaced the validation-only employee form with real request persistence.
- Added 1–50 dynamic expense rows with descriptions, whole-rial amounts, invoice flags, remarks,
  browser total feedback, and an authoritative checked server calculation.
- Enforced the 10,000,000,000-rial application and database limit.
- Snapshotted the current authenticated employee profile at submission time and prevented browser
  fields from controlling ownership, status, totals, timestamps, references, or tokens.
- Added up to five attachments per request, limited to 5 MiB each. PDF, JPEG, and PNG signatures
  are checked from bytes; browser MIME declarations are not trusted.
- Added random request references and 256-bit private tracking tokens. Only a SHA-256 token hash is
  stored and verification uses a constant-time comparison.
- Added a one-time confirmation view, a minimal private public-status link, and authenticated
  employee request history. Public status intentionally excludes requester, payee, bank, line, and
  attachment details.
- Added a five-requests-per-ten-minutes limiter to the employee creation endpoint.
- Removed committed merge-conflict markers from the README and updated project status.

## Verification

- Release build with warnings as errors: passed.
- Automated tests: 32 passed, 0 failed, 0 skipped.
- New tests cover total calculation, limit enforcement, line validation, file-signature detection,
  spoofed attachment rejection, token verification, token uniqueness, and reference uniqueness.

## Known follow-up work

- Run full browser and PostgreSQL integration tests against a disposable database.
- Decide whether attachment malware scanning is required before production deployment.
- The current endpoint limiter is in-process; use a distributed strategy if the application is
  deployed across multiple server instances.
- Translate and convert the submission experience to the planned Persian RTL/Jalali interface.
- Day 4 should begin the administrator request queue, detail review, and atomic approval/rejection
  workflow.
