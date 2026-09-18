# PHASE 4 — MILESTONE 3 REPORT

Branch: `feature/phase4-location-evidence`

Commit SHA: supplied in the final handoff; this report is included in that commit.

Baseline: `9d42fc7b0dfe95c24faf70e9a2b97b4ceb50a2fe`. Branch/HEAD verified before editing; only pre-existing `App_Data/` uploads were untracked. Baseline build: 0 errors, 0 warnings. No branch switch, merge or rebase.

## Implemented / portfolio domain model

Added intentionally self-published provider portfolio creation, management, archive and public display. Arabic RTL cards show image, title and optional description. `أعمالي` is accessible from the provider account menu and workspace, with `+ إضافة عمل`. Public profiles have `أعمال المزود` and an honest empty state. No metadata editing, restore, hard delete, social interaction or portfolio-to-request linking workflow was added.

`ProviderPortfolioItem` stores Id, ProviderProfileId, required Title (120 characters), optional Description (500 characters), generated StorageKey, decoded ContentType, CreatedAtUtc and IsArchived. Its only domain relationship is to ProviderProfile. It has no request/evidence foreign key or image-copy behavior. Images are not stored in SQL.

## Schema / migration

Migration: `20260918103535_AddProviderPortfolio`.

Up creates one table, a restrictive provider foreign key and an index on ProviderProfileId/IsArchived/CreatedAtUtc/Id. It changes no existing rows or tables. Snapshot changes contain only the new entity and relationship. The preceding `20260918085007_AddRequestEvidenceOwnership` migration and designer remain byte-for-byte unchanged. Historical migrations were not rewritten.

Migration verification: full chain applied in disposable LocalDB databases. A dedicated test upgrades from Milestone 2 to the new migration and verifies the added table and migration order. The existing historical-evidence migration test also upgrades through the full chain and verifies preserved Kind, uploader, timestamp, storage key and false OwnershipChecked. EF reports no pending model changes.

The application/shared database was not updated. Apply pending additive Milestone 2/3 migrations before running this version against that database. No reset, shared-data reseed or deployment was performed.

## Ownership authorization / provider eligibility

Owner identity is resolved from the authenticated user through `RequestAccessService.ProviderIdAsync` and `ProviderEligibility.Active`. Publishing requires the existing provider role, approved individual profile, active city and current role membership. No new eligibility flag exists. Ordinary availability such as Unavailable does not erase or hide portfolio data; that field is not a suspension mechanism.

Create accepts only title, description and a separately uploaded image. Posted ProviderProfileId/ProviderId/UserId/OwnerId fields cannot determine ownership. Eligibility is rechecked after image processing within a serializable write transaction. Archive uses a conditional update restricted to the authenticated eligible owner's item. There is no admin-specific portfolio privilege or metadata-edit endpoint. Customers, anonymous actors and unrelated providers cannot mutate another provider's items.

Public portfolio listing/media also checks canonical current eligibility. Loss of approval/eligibility suppresses public portfolio access while retaining records/files. An eligible owner can inspect archived items through management and its authorized image route.

## Media/storage architecture / image validation

`IPortfolioMediaStorage` and its small local adapter use `App_Data/ProviderPortfolio`. Files are served only by `ProviderPortfolio/Image`, which looks up a portfolio row and checks visibility. The folder is not mounted as static content, allowing direct image requests to respect archive state. Image responses are no-store.

The adapter reuses the unchanged `ImageUploadValidation.ReadAsync`: real PNG/JPEG detection/decoding, 5 MiB bounds, bounded stream reads, dimension/pixel limits and re-encoding without EXIF/GPS/trailing payloads. It never trusts an upload filename or claimed MIME type to select a path or response type. Generated keys are strictly constrained; path traversal and keys with trailing newlines are rejected. Upload HTTP/form limits remain 6 MiB. Failed creation cleans up its newly saved file.

## Public/private media separation / RequestEvidence isolation

`App_Data/RequestEvidence` remains private and separate. Neither portfolio storage nor portfolio endpoints open request evidence files or query RequestEvidence as portfolio content. Before, After and General/Legacy records are not promoted, copied or reclassified. RequestEvidence model/controller/policy/storage and OwnershipChecked semantics are unchanged from the approved baseline.

The public view model contains safe portfolio metadata and item IDs, not storage keys, uploader contact information, request coordinates or private evidence records. Tests place real private evidence in its own folder and confirm that portfolio storage cannot open the same key.

## Archive / public profile / verified activity

Archive sets IsArchived without deleting the row or image. Public listing and anonymous direct image access exclude archived rows. The owner's management view retains them. No restore action is included in this MVP.

Public portfolio pages contain six items per page; management contains twelve. Queries are asynchronous and no-tracking for reads, with no per-card database query. Existing reviews keep independent pagination.

Portfolio cards are labeled `منشور بواسطة المزود` (published by the provider), with no Verified badge. A separate `نشاط الخدمات عبر FixPal` section counts only requests with truthful accepted agreement, Completed state/completion timestamp, and accepted final price. It exposes a count, never request images. Status-only legacy completion does not inflate this count. Completion still requires neither portfolio nor evidence.

## Files changed

- `Controllers/ProviderPortfolioController.cs` (new)
- `Controllers/ProvidersController.cs`
- `Models/ProviderPortfolioItem.cs` (new)
- `Models/ViewModels/ProviderPortfolioViewModels.cs` (new)
- `Models/ViewModels/ProviderDirectoryViewModel.cs`
- `Services/PortfolioMediaStorage.cs` (new)
- `Data/ApplicationDbContext.cs`
- `Data/Migrations/20260918103535_AddProviderPortfolio.cs` and `.Designer.cs` (new)
- `Data/Migrations/ApplicationDbContextModelSnapshot.cs`
- `Program.cs` (portfolio storage registration)
- `Views/ProviderPortfolio/Create.cshtml` and `Index.cshtml` (new)
- `Views/Shared/_ProviderPortfolio.cshtml` (new)
- `Views/Providers/Details.cshtml`
- `Views/ProviderRequests/Index.cshtml`
- `Views/Shared/_LoginPartial.cshtml`
- `tests/FixPal.Scheduling.Tests/ProviderPortfolioTests.cs` (new)
- `PHASE_4_MILESTONE_3_REPORT.md` (new)

## Tests / build / regressions

.NET: **62 passed, 0 failed, 0 skipped** (7 portfolio tests plus all 55 existing tests). JavaScript location regressions: **7 passed**. Application build: **0 errors, 0 warnings**. `git diff --check` clean; EF model/snapshot consistency check passed.

Portfolio coverage: approved owner creation; ignored spoofed ownership fields; customer/anonymous/unapproved creation denial; unrelated archive denial; correct-profile-only publication; real image acceptance; fake/truncated/oversized/empty rejection; bounded metadata; safe keys; archive hiding both card and image URL; owner archived history and retained files; self-published labels in rendered Razor; separate private storage; no automatic publication of Before/After/Legacy; truthful completed count; completion without portfolio; and disposable migration order/application.

Milestone 1 regression: all 8 location tests and 7 JavaScript tests pass, including pre-agreement rendered/serialized privacy, actor authorization, active-state restrictions, fallback and discovery protection. Location production logic is unchanged.

Milestone 2 regression: all 8 evidence integration tests pass, including ownership, private reads, historical representation, image validation, image cap, lifecycle recheck and optional evidence. Evidence production files and the Milestone 2 migration are unchanged.

Phase 1 regression: existing workflow/agreement/start/completion checks pass; portfolio tests independently complete a request without portfolio or evidence. Image security uses the existing validator. The exhaustive historical quote-negotiation/review HTTP harness was not rerun.

Phase 2 regression: existing communication tests pass for contact disclosure, sending, unrelated sender denial, completed read-only history and blocked completed sending. No contact/chat production changes. Exhaustive phone-validation/browser coverage was not rerun.

Phase 3 regression: all existing scheduling/model/time tests pass, including proposer restrictions, confirmation, conflict handling, blackout/hours, reschedule/cancel/history and workflow synchronization.

Reproduce:

```powershell
$env:FIXPAL_RUN_SQL_TESTS='1'
dotnet test tests/FixPal.Scheduling.Tests/FixPal.Scheduling.Tests.csproj --no-restore
node --test tests/problem-location-js.test.cjs
dotnet build --no-restore
dotnet ef migrations has-pending-model-changes --no-build
```

## Known limitations / security risks / privacy risks / integration conflicts

- Tests use real MVC actions, isolated SQL databases, real image storage/validation and rendered public portfolio Razor. They do not exercise browser multipart binding, antiforgery or HTTP size rejection end-to-end. Existing global antiforgery and action role/rate/size controls remain. Live mobile visual inspection was not performed.
- Intentional public uploads can contain sensitive content voluntarily supplied by a provider. The form explains public visibility and asks providers to avoid customer data. No automatic content moderation or publication-consent workflow was added.
- Archive prevents future public reads; it cannot recall downloaded images or an already-open/in-flight response. No-store reduces retained browser caches.
- The local filesystem and database are not one distributed transaction. Handled failures clean up uploads; process termination between file creation and metadata commit can leave an orphan. Existing local-disk capacity/backup concerns remain; no cloud migration or global quota system was introduced.
- Pending migrations must be applied before the new code uses the application database. Public provider view/controller and account menu changes may overlap later UI work. Later portfolio enhancements must preserve independent storage and visibility checks.

## OUTSIDE YOUR CURRENT COURSE SCOPE

Visibility-controlled public media serving is needed so archived images do not remain accessible as static files. A static public directory is simpler but weakens archive behavior. Reusing real decoding/re-encoding avoids trusting browser MIME/filenames. Rechecking eligibility within a database transaction prevents publication after an eligibility change during image processing. These are small extensions to the existing MVC/EF/security patterns, not new infrastructure.

Recommendation: keep portfolio self-published, separately stored and archive-aware; expose only a separate factual completed-service count.

Why: preserves private customer evidence and truthful provenance while delivering a minimal usable provider showcase.

Alternative: reuse request images or publicly mount the evidence directory; alternatively publish portfolio files as permanent static assets.

Risk: evidence reuse leaks customer content; static assets remain retrievable after archive. Separate storage adds a small adapter and visibility endpoint but reuses all complex validation.

Final Decision: separate additive table, shared image validator, separate local media folder, server-derived ownership, archive-aware image endpoint and explicitly self-published cards. Stop for final Phase 4 review. No merge, Final MVP Polish or final end-to-end Phase 4 review has been started.
