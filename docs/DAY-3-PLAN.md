# Day 3 Plan — Employee Payment Request Submission

Date: 2026-10-17

## Goal

Turn the existing employee request form from a validation-only prototype into a complete,
secure submission workflow. At the end of Day 3, an authenticated employee can create a request
with expense rows and supporting files, receive a unique reference and private tracking link,
and review their submitted requests.

## Remaining work

1. **Submission rules and persistence**
   - Add a focused submission service so server-owned fields cannot be posted by a browser.
   - Require 1–50 expense rows with positive whole-rial amounts.
   - Recalculate the total on the server and enforce the existing 10,000,000,000-rial limit.
   - Snapshot the current employee profile and save the request and rows atomically.

2. **Reference and tracking security**
   - Generate a non-sequential public request reference.
   - Generate a high-entropy tracking token, store only its SHA-256 hash, and compare hashes in
     constant time.
   - Add confirmation and minimal public status pages without exposing payment or employee data.

3. **Attachments**
   - Accept at most five PDF/JPEG/PNG files, each no larger than 5 MiB.
   - Verify file signatures instead of trusting browser-provided extensions or content types.
   - Store the original display name, canonical content type, size, hash, and bytes.

4. **Employee experience**
   - Support adding and removing expense rows in the browser while retaining server validation.
   - Display a live estimated total; keep the server-calculated total authoritative.
   - Add a signed-in employee request-history page and link it from the dashboard.
   - Apply a per-employee submission rate limit.

5. **Quality and handoff**
   - Add unit tests for totals, limits, token verification, file validation, and entity creation.
   - Run build and test with warnings treated as errors.
   - Remove committed README merge markers and record the delivered Day 3 behavior and remaining
     risks in a completion report.

## Definition of done

- Valid submissions persist a pending request, rows, and valid attachments in one database save.
- Invalid, oversized, spoofed, or over-limit submissions do not persist.
- The clear tracking token is returned only after creation and is never stored.
- Employees can see only requests linked to their authenticated employee ID.
- Public tracking requires both the request reference and its private token.
- The solution builds and all automated tests pass.
