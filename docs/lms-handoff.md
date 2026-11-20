# Classroom-centric LMS

## Service boundaries

| Service | Implemented today |
|---|---|
| Identity | Authentication, accounts and roles for administrators, teachers and learners. |
| Classroom | Centers, rooms, direct class enrollment, teacher assignment, recurring schedules, generated sessions, Meet integration, conflict checks and attendance. Class creation accepts code, name, description and dates. ClassEnrollment controls participation. |
| Assessment | Question bank, practice sets, tests, attempts, grading and results. |
| Content | Existing study features, R2 storage, signed uploads, file metadata, size/type/signature validation and owner checks. Admin media uploads are exposed at /content/api/v1/uploads/media with presign, confirm and bulk operations. Confirmed media is retained for future use. |
| Payment | Admin-issued class obligations in whole VND, SePay receipts, cash, waivers, balances, transaction statements and bank exception reconciliation. |

The API Gateway retains its service-prefix routes. The independent vocabulary, dictation, grammar, video and news features retain their existing contracts. Legacy course catalog, publishing, pricing, promotions, modules, lesson editing, course access/enrollment and Content checkout endpoints have been retired. Payment no longer calls Content or runs the course fulfillment worker. Historical Content course/lesson count reports remain available on their existing analytics contract.

## Tuition API

Use the gateway prefix /payment. The table gives paths relative to that prefix.

| Method and path | Caller | Contract |
|---|---|---|
| POST /api/v1/tuition-orders | Admin | Supply paymentOrderId, classId, studentId, amountOwed and optionally classEnrollmentId. Reuse the order ID on retry. Reusing an ID with different terms returns a conflict. |
| GET /api/v1/tuition-orders/{id} | Admin or owning student | Returns the obligation, current amount paid, amount waived, remaining balance, transfer reference, bank details and entry history. Other students receive 404. |
| POST /api/v1/tuition-orders/{id}/entries | Admin | Supply entryId, kind (Cash or Waiver) and amount. Reuse the same entry ID on retry. Each entry records the acting admin. |
| POST /api/v1/webhooks/sepay | SePay | Uses the existing Apikey authentication, configured receiving account, transfer direction, timestamp and amount checks. The transfer memo must identify one LMS payment reference. |
| GET /api/v1/admin/payment-reconciliation/transactions | Admin | Paginated bank transactions requiring review, including unmatched, late, invalid, ambiguous and excess transfers. |

For example, an obligation of 1,000,000 VND with a 300,000 VND transfer, 500,000 VND cash receipt and 200,000 VND waiver has 800,000 VND paid and zero outstanding. A positive partial transfer credits the order immediately. An incoming transfer exceeding the remaining balance goes to review without automatic allocation. The webhook credits each provider transaction ID at most once. The receipt, applied ledger entry and balance update share a transaction with an order row lock. LMS bank references start with TL followed by 16 hexadecimal characters. Configure SePay to preserve the full reference.

**Pay before enrollment.** An Admin issues an obligation for the student and class. The student reads the statement and pays. Once the backend statement shows settlement, an Admin can enroll the student through Classroom. Automated enrollment provisioning and a student self-service order request remain follow-up work.

**Enroll before payment.** An Admin can enroll the student immediately and independently issue the obligation with that enrollment ID. A balance can remain outstanding while the student participates. Changing a payment balance has no effect on class access.

The Admin selects the fee and supplies the IDs. There is no authoritative fee schedule or cross-service check binding the optional enrollment to the supplied student and class yet. Zero balance includes waivers. The statement keeps waiver and payment totals separate.

## Schema and environment

Payment migration 20261119162057_AddClassTuition adds TuitionOrders, TuitionEntries and a nullable tuition-order link on PaymentTransactions. Course-era orders, references, coupons, transactions, Content tables and all earlier migrations remain intact. The migration does not infer classes from old purchases. Resolve outstanding legacy checkout and fulfillment obligations in the original system before switching any real payment callbacks.

Use the LMS .env.example for the Compose project, published ports, five database names and JWT issuer/audience. Custom application images use the toeicspace-lms namespace. Named volumes and networks are project-scoped. Provision fresh credentials, separate R2 storage, SePay callback/account, OAuth client and signing keys. The opt-in migration helper accepts Payment. Its design-time context uses the Payment infrastructure project. Apply migrations only to the intended LMS database after reviewing the forward SQL. See the [extraction checklist](lms-phase-1.md) for Hangfire, local user secrets, gateway and OAuth setup.

## Developer follow-up

- **Class Learning Materials** — add reusable material metadata and many-to-many class links, then authorize reads against Classroom enrollment. Reuse validated uploads and the R2 store. Review retention and historical object references before enabling deletion.
- **Class Assignments** — link existing Assessment tests and practice sets to classes and enforce participation when starting attempts.
- **Advanced Tuition Management** — introduce validated class fees and student/class lookup before self-service orders. Define a trusted, idempotent Classroom provisioning contract and durable retries for settlement. The current enrollment endpoint requires an Admin JWT. Define refunds, excess-transfer allocation, corrections, audit policy and provisioning failure reconciliation with the center.
- **Notification Management** — define delivery and retry rules for enrollment, tuition and schedule changes.
- **Analytics and Reports** — define class assignment progress and tuition reports, and agree retention/retirement of historical course-count and lesson-count reports.

## Verification

The Payment workflow test runs against a fresh in-memory SQLite database. It covers duplicate delivery, partial transfer, invalid account, excess amount, cash replay, waiver, settlement and owner-only reads. SQLite does not exercise MySQL row locks. The concurrent delivery test uses separate MySQL contexts and is opt-in through LMS_TUITION_TEST_DB, which must identify a dedicated database named lms_test_*. It applies Payment migrations to that test database. Run the Classroom test project separately because the inherited solution omits it.

The verified .NET 10 solution rebuild completed with six inherited Content dictionary nullable warnings and zero errors. The separate Classroom test project built cleanly. Across eight xUnit assemblies, 1212 tests passed, 0 failed and 30 skipped. VSTest could not bind its local socket in this environment, so the same compiled assemblies ran with the xUnit in-process executor. No isolated MySQL endpoint was supplied. The forward Payment SQL was generated offline and EF reported no pending model changes. Full and Payment-only Compose configuration resolved without starting services.
