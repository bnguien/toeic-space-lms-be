# TOEICSpace LMS extraction

Historical reference from the extraction baseline. See the [current LMS contracts and operational handoff](lms-handoff.md).


This backend serves TOEIC training centers. A Class is the operational unit. Phase 1 retains the original .NET 10 implementation, solution membership, migrations and client contracts. The API Gateway routes requests to five business services. The solution also includes shared building blocks and the inherited web project.

| Service | Target ownership | Evidence and migration work |
|---|---|---|
| Identity | Admin, Teacher and Student accounts, roles, authentication and account lifecycle | Reuse Identity and its user identifiers across services. Classroom decides participation. |
| Classroom | Centers, rooms, classes, instructors, enrollments, recurring schedules, sessions and attendance | Class and CreateClassCommand already allow course-free creation. Preserve teacher/room conflict checks and student conflict warnings. ClassEnrollment owns class access. |
| Assessment | Question bank, practice sets, tests, attempts, grades and feedback | Add class assignments linking ClassId to existing assessments and check participation on access. AnalyticsCatalog includes the Content metric course_lesson_progress. Review reporting consumers before retirement. |
| Content | Reusable PDFs, videos, documents, storage metadata and access | CourseModule, Lesson, LessonResource and LessonVideo tie the existing material structure to courses. Introduce independently identified resources and a unique ClassId/resource link. Authorize downloads through active class participation, including when a resource is shared by several classes. |
| Payment | Class tuition obligations, pricing, installments, cash, waivers, balances, SePay and reconciliation | PurchaseOrder currently requires CourseId and UserId and carries checkout, slot reservation, promotion, coupon and fulfillment state. Preserve historical order semantics. Introduce tuition orders with PaymentOrderId, ClassId, StudentId and optional ClassEnrollmentId. |

## Existing Course contracts and cutover

Content exposes catalog, modules, lessons, uploads, promotions, enrollments and internal checkout through PublicCoursesController, CourseModulesController, CourseLessonsController, LessonUploadsController, CoursePromotionsController, EnrollmentsController and InternalCheckoutController. CourseEnrollment grants legacy course access. Payment's OrdersController, SePayWebhooksController and reconciliation flow depend on course purchase and fulfillment. The inherited [purchase contracts](course-purchases.md) describe capacity locks, reservation expiry, signed URLs and frontend response shapes. DictationCourse is a separate learning feature and requires its own review.

1. Inventory frontend and external consumers of gateway routes, course progress, signed media URLs, pricing, checkout and enrollments. Record ownership, contract tests and supported client versions before changing routes.
2. Introduce additive resource links, class assignments, tuition orders and a transaction ledger. Preserve legacy IDs, order snapshots and enrollment history. Backfill only reviewed class/resource mappings. Hold ambiguous mappings for center review.
3. For pay-before-enrollment, create the order without an enrollment. Verify SePay confirmation on the backend, deduplicate the provider transaction, then persist a retryable provisioning request when the tuition policy is satisfied. Make activation idempotent by student and class. Frontend success is informational.
4. For enroll-before-payment, Admin activates ClassEnrollment. Payment independently tracks the obligation and records cash, installments, adjustments and waivers. Active enrollment can coexist with an unpaid balance. Payment history must distinguish a waiver from money received.
5. Verify replay, retry, concurrent orders, balances, access, shared resources and scheduling. Reconcile migrated counts and financial totals against snapshots. Switch versioned clients incrementally and retain a rollback path to legacy reads. Retire catalog, course-specific modules, checkout and old tables only after consumers and historical retention have been addressed.

## Environment isolation before first launch

- Start from the LMS .env.example. Its Compose project name scopes default container, network and volume names. Its published ports are offset from upstream by 10000 and its five database names use the LMS namespace. Verify port availability on the development host. Keep internal container ports unchanged.
- Provision separate MySQL volumes and credentials for each service, including the Identity and Classroom Hangfire stores. Point the migration profile and any EF or seed commands exclusively at these databases. Use separate Redis and RabbitMQ resources.
- Supply fresh JWT, OTP, internal API and bootstrap credentials. Review inherited UserSecretsId values, key-generation scripts, RunLocal.py, launch profiles, Postman URLs and gateway development upstreams before local execution. Set LMS-only JWT issuer/audience, CORS origins, gateway host and downstream URLs.
- Register a development SePay account/webhook and key at the LMS gateway. Register a separate Google Meet OAuth client, redirect URI and token encryption key. Allocate an LMS R2 bucket and credentials, retained-media origin and SMTP sandbox. Never share stored teacher refresh tokens or production payment callbacks.
- The inherited CI workflow is archived at reference/ci.yml.disabled. Review registry destinations and deployment settings before enabling automation. Local scripts and legacy deployment guides assume upstream infrastructure.

## Independent Git setup

From the LMS directory in a shell with writable Git metadata, run the following. Create an empty GitHub repository named toeic-space-lms-be under the intended owner first. Replace OWNER with that account. Review the staged files before committing.

```sh
git init -b main
git add .
git diff --cached --stat
git commit -m "Extract TOEICSpace LMS backend baseline"
git remote add origin git@github.com:OWNER/toeic-space-lms-be.git
git remote -v
```
