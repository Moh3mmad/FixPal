# Phase 3 scheduling handoff

## Architecture

Phase 3 adds a provider calendar, recurring weekly working periods, soft-removable blackout periods, and request-linked appointments. `ProviderProfile.Availability` remains an independent matching signal. `MaintenanceRequest` remains authoritative for actual work state; appointment state records scheduling occupancy and history only.

Services use `ApplicationDbContext` directly. MVC controllers only bind input, call services, map controlled results, and redirect. Request-linked writes run inside `RequestMutationService` so the request row and scheduling changes share one SQL Server serializable transaction.

## Phase 3 commits

- `77e080d` Phase 3: add scheduling domain and time policy
- `0a0d5a0` Phase 3: map scheduling entities in EF Core
- `033b410` Phase 3: add provider calendar service
- `b526b92` Phase 3: add provider blackout service
- `916653c` Phase 3: add initial appointment scheduling service
- `69abba9` Phase 3: add appointment reschedule and cancellation
- `0951a66` Phase 3: sync appointments with request workflow
- `823cd52` Phase 3: add scheduling MVC endpoints
- `a6a30de` Phase 3: add scheduling UI
- `48fecd3` Phase 3: add scheduling database migration
- `107a5ef` Phase 3: add scheduling tests

## Migration

Migration `20260917150633_AddPhase3Scheduling` creates `ProviderCalendars`, `ProviderWorkingPeriods`, `ProviderBlackouts`, and `Appointments`. It includes restrictive foreign keys, UTC and lifecycle check constraints, rowversion columns for calendars and appointments, the filtered unique active-request index, the replacement-history index, and provider interval indexes.

Follow-up migration `20260917193231_AddAppointmentConfirmationLifecycle` adds explicit decision audit fields and proposal/rejection states. It converts legacy status-1 rows to proposals so an earlier unilateral booking is never represented as mutually confirmed.

The model snapshot is a shared integration surface. Reconcile it by regenerating or carefully merging from the final combined model after Phase 2 and Phase 3 migrations are both present; do not choose one branch's snapshot wholesale.

## MVC routes and UI

- `GET /ProviderCalendar/Index`
- `POST /ProviderCalendar/Create`
- `POST /ProviderCalendar/Update`
- `POST /ProviderCalendar/CreateBlackout`
- `POST /ProviderCalendar/RemoveBlackout`
- `POST /Appointments/Schedule`
- `POST /Appointments/Reschedule`
- `POST /Appointments/Confirm`
- `POST /Appointments/Reject`
- `POST /Appointments/Cancel`

The Arabic RTL provider page manages timezone, enabled state, weekly periods, blackouts, and blackout history. Private request details render an appointment component that shows agreement readiness, calendar readiness, active appointment actions, and terminal history. Unsafe actions retain global antiforgery validation and the existing `writes` rate-limit policy.

## Locking and concurrency

Scheduling and rescheduling use this order:

1. `MaintenanceRequest` row lock held by `RequestMutationService`.
2. Request access, accepted agreement, and provider eligibility reads.
3. `ProviderCalendar` `UPDLOCK, HOLDLOCK`.
4. Working-period, blackout, request appointment, and provider occupancy reads.
5. Appointment write and transaction commit.

Cancellation keeps the request lock and calendar lock but intentionally does not require current provider eligibility, agreement revalidation, or an enabled calendar. Calendar-only mutations lock `ProviderCalendar` and never acquire request locks. Half-open intervals `[start, end)` allow adjacent bookings. Only confirmed and in-progress appointments occupy provider time. A proposal does not reserve time; confirmation repeats calendar and conflict validation while holding the provider calendar lock.

Rescheduling creates a proposed replacement while the existing confirmed appointment remains active. Acceptance confirms the replacement and supersedes the old appointment atomically. Rejection or withdrawal preserves the original confirmation and all historical rows.

Request start and completion retain all existing workflow checks. When a calendar and active appointment exist, start changes `Confirmed` to `InProgress`, and completion changes `InProgress` to `Completed`, in the same request transaction. A proposed appointment cannot be used as a confirmed visit. Missing calendars or appointments preserve legacy workflow behavior.

## Timezone policy

Appointment and blackout instants are stored as zero-offset `DateTimeOffset` values. Browser forms submit minute-precision local civil `DateTime` values. Services resolve them with the provider calendar's persisted `TimeZoneId` using `TimeZoneInfo`, reject non-`Unspecified` kinds, reject ambiguous or nonexistent DST times, and require appointments to fit one working period on one local date. Each appointment stores the timezone identifier used when it was scheduled.

## Verification

`tests/FixPal.Scheduling.Tests` contains deterministic time-policy and EF model-contract tests plus isolated SQL Server lifecycle scenarios for authorization, agreement gating, proposal decisions, rescheduling, blackout/working-hour revalidation, overlap, and concurrent confirmation. SQL scenarios require `FIXPAL_RUN_SQL_TESTS=1` and always use a unique `FixPal_Phase3_Tests_*` database.

The full application build succeeded. Migrations were applied only to isolated database `FixPal_Phase3_Final_20260917_181058`. The application started against that same isolated database, completed its permitted seed startup, and returned HTTP 200 for `/`. The normal/shared database was not used.

## Phase 2 integration audit

After fetching `origin/develop/final-mvp`, its change since the common base is only `Areas/Identity/Pages/Account/Manage/Index.cshtml`. There is currently no directly overlapping changed file.

These remain high-risk shared surfaces if Phase 2 advances before integration:

- `Program.cs`
- `Views/MaintenanceRequests/Details.cshtml`
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `Services/RequestWorkflowService.cs`
- `FixPal.csproj`

Phone normalization, account contact handling, contact visibility, request messages, and communication UX were not redesigned by Phase 3.

## Recommended integration order

1. Integrate the latest Phase 2 branch first.
2. Apply the Phase 3 commits in the order listed above.
3. Preserve Phase 2 communication/contact markup when reconciling request details and retain only the appointment component insertion from Phase 3.
4. Reconcile the final EF snapshot from the combined model while retaining both branches' migration files.
5. Build and run the scheduling tests.
6. Apply migrations and smoke-test against a new disposable database before using any shared environment.

## Deferred work

- Browser-level visual and interaction verification for authenticated customer and provider accounts.
- Field-specific localized validation messages; CP9 intentionally uses generic user-facing errors.
- Broader transactional integration tests for concurrent SQL Server booking attempts.
- Rich calendar visualization, reminders, recurring exceptions, and external calendar integration.
