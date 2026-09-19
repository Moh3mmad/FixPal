# PHASE 4 — MILESTONE 2 REPORT

Branch: `feature/phase4-location-evidence`

Commit SHA: supplied in the final handoff; this report belongs to that commit.

Baseline: `c179a3e151a97f14fbccdf7d860faace9eca78e1`. Branch and HEAD verified before editing. Only existing `App_Data/` runtime files were untracked. Baseline build passed with 0 errors and 0 warnings. No merge, rebase or branch switch.

## Implemented / evidence model

Extended the existing RequestEvidence system with server-enforced semantic ownership and lifecycle checks. Existing request ID, uploader ID, UTC upload time, kind, storage key, content type and size remain. One Boolean `OwnershipChecked` distinguishes uploads accepted under the new rules. Its false default preserves uncertainty for historical rows. `DisplayKind` is a nonmapped computed property that displays unchecked or unrecognized evidence as General. Original historical Kind values are preserved.

Request Details now includes a prominent Arabic `🖼 صور العمل` card with Before/customer and After/provider sections, counts, private image previews, empty states and an evidence-page action. The evidence page groups Before, After and General/Legacy images and exposes only the uploader's eligible action. Images remain optional. Existing responsive Bootstrap columns stack on smaller screens; no gallery framework or social features were added.

## Before authorization

Only the authenticated owner acting as the request customer, not the assigned provider. Request must be Pending or Accepted, with no work-start or completion timestamp. It is allowed before agreement; agreement is not a prerequisite for recording the original problem. An account that also has a provider role can still be the customer of a different provider's request; ownership is determined by its actual request relationship.

## After authorization

Only the actual assigned provider, not the customer, with current provider eligibility for that request and a truthful Phase 1 accepted agreement. Request must be InProgress, have a work-start timestamp and no completion timestamp. Neither assignment, quote existence nor an appointment proposal substitutes for agreement.

## Lifecycle / identity rules

Both kinds are optional; start and completion services were not changed. Wrong-side, General, undefined-kind, unrelated and ineligible uploads are rejected server-side. Uploader identity is read from the authenticated principal. The action accepts no uploader, customer, provider or ownership-check parameters; posted identity fields cannot assign ownership.

The shared `RequestMutationService` obtains the same request-row update lock used by workflow writes. After image validation/storage, the upload rechecks access, ownership, eligibility and lifecycle inside that transaction before inserting metadata. This closes the work-start/completion race. Failed/uncommitted writes remove their newly saved private file. The existing 20-image cap is checked inside the lock.

## Historical evidence handling / schema / migration

The previous endpoint allowed either participant to select Before or After, so its labels alone do not prove the new semantics. Historical rows display as General/Legacy; uploader, timestamp, original kind and storage metadata remain untouched. Existing private images remain readable through the same authorized endpoint.

Schema changes: one non-null `bit` column, `RequestEvidence.OwnershipChecked`, default false.

Migration: `20260918085007_AddRequestEvidenceOwnership`.

The generated Up migration only adds that column; no historical UPDATE, dropped table, reseed or reset. Snapshot diff contains only the corresponding Boolean property. `dotnet ef migrations has-pending-model-changes --no-build` reported no model changes. A disposable database test migrated from the prior schema with a historical evidence row and verified original metadata plus the false ownership default.

The existing application database has **not** been migrated. Apply the additive migration through the normal database-update process before running this application version against it. No application startup migration/reseed behavior was introduced.

## Storage / image security / public-private separation

`IPrivateMediaStorage`, `LocalPrivateMediaStorage` and `ImageUploadValidation` are unchanged. Media stays under private `App_Data/RequestEvidence`, outside `wwwroot`; binaries are not stored in SQL Server. Access to images still requires existing request authorization, with no-store response behavior.

Preserved safeguards: real PNG/JPEG format detection and decoding, 5 MiB input/output bound, bounded stream reading, dimension/pixel limits, re-encoding without EXIF/GPS/trailing payloads, generated safe storage keys, and 6 MiB HTTP/form limits. Existing validation derives the stored MIME type from decoded image format rather than trusting the browser's claimed MIME or extension. Fake/truncated PNGs and oversized/empty images are rejected in tests using the real storage adapter.

Public provider controllers, DTOs and views were not changed and contain no evidence integration. There is no automatic publication or portfolio reuse. Administrative read access follows the existing request policy; no new admin mutation privilege was added. Provider Portfolio is not implemented.

## Files changed

- `Controllers/RequestEvidenceController.cs`
- `Models/RequestEvidence.cs`
- `Models/ViewModels/EvidenceViewModel.cs`
- `Models/ViewModels/RequestViewModels.cs`
- `Services/RequestEvidencePolicy.cs` (new)
- `Services/RequestDetailsService.cs`
- `Program.cs` (policy registration)
- `Views/MaintenanceRequests/Details.cshtml`
- `Views/RequestEvidence/Index.cshtml`
- `Views/Shared/_RequestEvidenceSummary.cshtml` (new)
- `Data/Migrations/20260918085007_AddRequestEvidenceOwnership.cs` and `.Designer.cs` (new)
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `tests/FixPal.Scheduling.Tests/EvidenceOwnershipTests.cs` (new)
- `tests/FixPal.Scheduling.Tests/AppointmentLifecycleIntegrationTests.cs` (fixture registration)
- `FixPal.csproj` (exclude nested test content from web build)
- `PHASE_4_MILESTONE_2_REPORT.md` (new)

The project-file exclusion fixes recursive copying of nested test build output discovered during repeated verification. It was necessary for a stable warning-free build; no dependencies changed.

## Tests / build

Final full .NET run: **55 passed, 0 failed, 0 skipped**. Includes 8 evidence integration tests, 8 Milestone 1 tests and 39 existing scheduling/model/time tests. JavaScript location regressions: **7 passed**. Final `dotnet build --no-restore`: **0 errors, 0 warnings**. `git diff --check` passed.

Evidence tests cover successful customer Before and provider After; wrong-side and unrelated actor denials; anonymous upload challenge; posted identity fields ignored; lifecycle restrictions and truthful agreement; actual private image reads; public DTO separation; work-start during image processing with cleanup; completion without either photo; fake/truncated/empty/oversized images; unsafe storage keys; 20-image cap; historical readability and General counts; and migration preservation.

Phase 1 regression: truthful agreement and work transitions exercised; start/completion succeed without evidence, and existing workflow/scheduling gate tests pass. Image validation/storage code is unchanged and exercised. The complete historical quote-negotiation/review HTTP harness was not rerun.

Phase 2 regression: tests verify active contact disclosure and sending, completed read-only message history, blocked completed sending and unrelated sender denial. Milestone 1 tests retain the pre-agreement locked-communication check. No contact or messaging production code changed. Full phone-validation and browser HTTP coverage was not rerun.

Phase 3 regression: all existing tests pass, including proposal/confirmation, proposer restrictions, conflicts, blackouts/hours, time handling, rescheduling/history, cancellation and workflow synchronization.

Milestone 1 regression: all 8 SQL-backed/model/rendered-location checks and all 7 JavaScript checks pass. Exact-coordinate authorization code and map code are unchanged.

Reproduce:

```powershell
$env:FIXPAL_RUN_SQL_TESTS='1'
dotnet test tests/FixPal.Scheduling.Tests/FixPal.Scheduling.Tests.csproj --no-restore
node --test tests/problem-location-js.test.cjs
dotnet build --no-restore
dotnet ef migrations has-pending-model-changes --no-build
```

## Known limitations / potential integration risks

- Tests invoke real MVC actions/services against isolated SQL databases and real temporary private storage; they do not exercise multipart model binding, antiforgery or HTTP size rejection through a running browser/server. Existing global antiforgery and action size/rate limits remain unchanged.
- Razor views compile successfully, but the new evidence cards were not visually checked in a live mobile browser.
- `OwnershipChecked` proves authorization/lifecycle checks at upload, not authenticity of the photographed scene or verified public work.
- Older clients with both upload choices receive server rejection for the disallowed choice. Shared views/controllers may need ordinary conflict resolution during future integration.
- Applying the migration is required before running the new code against the existing application database. Its data was not modified during this milestone.
- Private storage and SQL writes are not a distributed transaction. Handled failures clean up files; abrupt process failure between file creation and metadata commit can still leave an orphan, as with the existing adapter. No infrastructure expansion was added.

## Recommendation / Why / Alternative / Risk / Final Decision

Recommendation: retain one evidence model, one private storage path and one authorization policy; distinguish new checked uploads from uncertain history with one additive flag.

Why: minimal scope, truthful history and reuse of Phase 1 security and concurrency protections.

Alternative: infer historical kinds from current account roles, rewrite old kinds, or introduce a separate evidence table/service stack.

Risk: current roles cannot prove past uploader context or lifecycle; rewriting loses original metadata; parallel systems duplicate security rules. The flag requires a small migration and consistent use of DisplayKind for presentation.

Final Decision: additive flag, conservative General presentation, shared request locking, and unchanged private image adapter. Stop after Milestone 2; wait for `GO MILESTONE 3`.

OUTSIDE YOUR CURRENT COURSE SCOPE: row-lock revalidation prevents concurrent workflow changes from invalidating upload permission. Image decoding/re-encoding and private file serving prevent trusting client file claims or exposing uploads as static content. These existing mechanisms were reused; a simpler check only before saving would leave a lifecycle race. No new infrastructure was introduced.
