# PHASE 4 — MILESTONE 1 REPORT

Branch: `feature/phase4-location-evidence`

Commit SHA: supplied in the final handoff; this report is part of that commit.

Baseline: `05c99ef71bf3b6f1a1a14fd36061aba0864fffc2`. Correct checkout is the nested `FixPal_GoldenBaseline/FixPal` repository. The sibling `FixPal` checkout is older and was not changed. Baseline restore and build passed with 0 errors and 0 warnings. Restore required access to the existing user NuGet configuration. Only three runtime PNGs under untracked `App_Data/` existed at audit; none were staged. Phase 3 confirmation, rejection, proposer restrictions and conflict checks were present.

## Implemented / architecture

Retained the MVC monolith, existing nullable Latitude/Longitude storage, input validation, optional GPS/manual Leaflet picker, and City/Area fallback. Removed coordinates from the general details projection. A separate asynchronous, no-tracking query loads them only for an authorized actor, rechecking current assignment, state, and Phase 1's `RequestAgreementPolicy.AgreedRequests`. No alternative agreement source or new framework was added.

Added an Arabic Problem Location panel, interactive marker, on-demand map loading, directions URL, privacy explanation and legacy empty state. Extracted the existing integrity-pinned Leaflet loader for reuse by creation and details pages. Existing responsive map styling and RTL layout are retained.

## Authorization / exact-location privacy policy

| Viewer | Exact location |
| --- | --- |
| Request owner | Own request, including completed/cancelled history |
| Actually assigned, approved provider with current provider role | Only with a truthful accepted agreement and Accepted/InProgress request state |
| Assigned provider before agreement, Pending, Completed or Cancelled | Never; City/Area only |
| Unrelated customer/provider, anonymous | No request details access |
| Administrator inspecting another person's request | No exact location; ordinary details access retained |
| Public directory, matching/recommendation DTOs | No coordinates |

Pre-agreement view-model coordinates are null, not hidden with CSS. The location panel emits no coordinate attributes, marker or directions URL for restricted viewers. Existing no-store response behavior is retained. There is no coordinate JSON endpoint; serialization tests confirm the pre-agreement detail model contains no exact values. Public provider projections and matching results are also checked.

## Location edit policy

Location is immutable after creation, which is stricter than locking only after agreement. The application already has no location edit action; no new mutation endpoint was introduced. UI explains this before submission and to the owner on details. Existing workflow mutations do not update location. A future edit feature must enforce the agreement lock server-side.

## Map behavior / manual fallback

Browser geolocation and manual map selection remain optional. Permission denial, unsupported GPS and map loading failure leave City/Area creation usable. A map opens only after a click. The authorized details panel shows the saved marker and a standard Google Maps directions URL (no Google SDK, API key or billing integration). Without JavaScript, City/Area submission and the directions anchor remain normal HTML behavior. The existing server rejects out-of-range, nonfinite and unpaired coordinates.

Schema changes: none. Migration name: not applicable. Existing `AddPrivateRequestCoordinates` remains untouched. No shared database migration, reset or reseed was performed. Tests migrated and disposed only their unique test databases.

Legacy-data behavior: no fabricated points; missing/invalid historical coordinates produce an honest no-exact-location state for authorized viewers. Restricted viewers always receive the privacy state.

## Files changed

- `Models/ViewModels/RequestViewModels.cs`
- `Services/RequestDetailsService.cs`
- `Views/MaintenanceRequests/Create.cshtml`
- `Views/MaintenanceRequests/Details.cshtml`
- `Views/Shared/_RequestLocationInput.cshtml`
- `Views/Shared/_ProblemLocation.cshtml` (new)
- `wwwroot/js/request-location.js`
- `wwwroot/js/leaflet-loader.js` (new, extracted loader)
- `wwwroot/js/problem-location.js` (new)
- `tests/FixPal.Scheduling.Tests/AppointmentLifecycleIntegrationTests.cs` (fixture support only)
- `tests/FixPal.Scheduling.Tests/ProblemLocationTests.cs` (new)
- `tests/problem-location-js.test.cjs` (new)
- `PHASE_4_MILESTONE_1_REPORT.md` (new)

## Tests / build / regression results

- Full .NET run: 46 passed, 0 failed, 0 skipped, including 7 initial location tests and 39 existing tests. After adding administrative privacy coverage, the targeted location suite passed all 8 tests, 0 skipped. Thus 47 distinct .NET tests passed across the runs.
- JavaScript: 7 passed, 0 failed, testing production event handlers with simulated DOM, Leaflet and geolocation.
- Final application build: 0 errors, 0 warnings. `git diff --check` passed.
- Creation controller tests verify City/Area-only and optional-point persistence, and rejection of partial pairs, NaN, infinity, invalid latitude and invalid longitude. Tests apply data-annotation validation before direct controller invocation.
- SQL-backed detail-service tests verify owner, assigned provider, unrelated customer/provider, anonymous and administrative access; all request states; absent agreement decision; truthful agreement; legacy no-point state; and coordinate-free discovery.
- Actual compiled Razor location-panel rendering verifies pre-agreement HTML has no coordinates/data attributes/directions and authorized HTML does. JSON serialization is checked separately. This is not a full HTTP/browser test.
- Phase 1: existing agreement/start/completion scheduling integration checks passed. Missing customer acceptance decision cannot unlock location. Quote, evidence, review and workflow production code is unchanged; the full historical Phase 1 HTTP harness was not rerun.
- Phase 2: pre-agreement communication remains locked in the location integration test. Communication/contact/message production code is unchanged; full phone normalization, messaging and completed-chat HTTP regressions were not rerun.
- Phase 3: existing tests passed, including proposal/confirmation/rejection, self-confirm denial, blackouts/hours, time policies, overlaps/concurrent confirmations, rescheduling/history/cancellation, legacy migration and start/completion synchronization.

Reproduce:

```powershell
$env:FIXPAL_RUN_SQL_TESTS='1'
dotnet test tests/FixPal.Scheduling.Tests/FixPal.Scheduling.Tests.csproj
node --test tests/problem-location-js.test.cjs
dotnet build --no-restore
```

## Known limitations / security / privacy / integration

- Live GPS permissions, external tile availability and mobile visual appearance were not exercised in a real browser. Client failure paths were simulated; Razor was compiled and rendered in tests.
- Maps depend on the existing third-party Leaflet CDN and OSM tiles. Opening the map reveals the viewed area to the tile service; clicking directions sends the exact destination to Google Maps. The UI explains external loading/sharing, and neither happens automatically.
- Previously disclosed coordinates cannot be revoked from a provider's memory, screenshots or already-open page. Future reads stop exposing them after completion/cancellation.
- No new public coordinate endpoint or storage exposure was introduced. User-written description text and images are not automatically scrubbed for volunteered location information.
- Future location editing or new detail/API projections could bypass this policy if implemented carelessly. Keep the general projection coordinate-free and reuse the agreement source of truth.
- Potential integration conflicts: request details/create views, shared location picker and details service may overlap later UI changes. All other Phase 1/2/3 production services and schema remain unchanged.
- The existing diagnosis/AI code was observed and left untouched, as outside Phase 4 scope. Evidence ownership and provider portfolio are deferred to their explicitly approved milestones.

## OUTSIDE YOUR CURRENT COURSE SCOPE

Browser Geolocation and Leaflet require asynchronous permission/loading/failure handling to provide optional map selection. City/Area alone is the simpler fallback; external service availability is the main added risk. Conditional disclosure requires server-side authorization beyond ordinary display logic because a hidden HTML field still leaks data. The simplest safe alternative is never showing providers exact location, but that loses the requested directions benefit. SQL-backed tests and rendered Razor checks are used to verify the privacy boundary without changing architecture.

## Decisions

| Choice | Recommendation | Why | Alternative | Risk | Final Decision |
| --- | --- | --- | --- | --- | --- |
| Storage | Reuse nullable coordinates | Already suitable, avoids migration | Rename/add columns | Duplicate sources or unnecessary migration | Reused existing fields |
| Authorization | Conditional coordinate query using existing agreement policy | Prevents browser disclosure before truthful acceptance | UI hiding; separate agreement flag | UI hiding leaks; separate flags drift | Server-side query with current actor/state checks |
| Editing | Keep create-only location | Existing behavior guarantees post-agreement immutability | Add pre-agreement editing | New mutation/concurrency scope | No editing endpoint |
| Maps | Reuse Leaflet/OSM, lazy load, standard directions link | Small compatible MVP change | New map SDK/custom navigation | External availability and deliberate sharing | Existing stack plus optional directions |

Stopped after Milestone 1. Do not begin Milestone 2 until the user says `GO MILESTONE 2`.
