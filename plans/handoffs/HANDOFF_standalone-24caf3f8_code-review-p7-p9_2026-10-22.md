# Full-code review continued (P7, P8, P4 leftovers, most of P9) — 10 local review commits, NOT pushed/deployed/released

**Date:** 2026-10-22
**Status:** IN PROGRESS (review ~80% done; everything since release 1.91 is local only and never run on a device)
**Bead(s):** none (`bd` is not installed in this repo)
**Epic:** Opicentrum admin board + code quality pass
**Chain:** `standalone-24caf3f8` seq `2`
**Parent:** `HANDOFF_standalone-24caf3f8_opicentrum-perf-code-review_2026-10-22.md`
**Prior chain:** `HANDOFF_standalone-24caf3f8_opicentrum-perf-code-review_2026-10-22.md` > this

---

## Since Last Handoff

- Parent's "Where We're Going" #1 was a decision ship-vs-continue. The user answered with a single line, **"Pokracuj v kontrole kodu"** — so: keep reviewing, do NOT release yet. Parent's recommendation (ship first) was therefore not followed; I re-stated it at the end of the session and the question is still open.
- Parent said `OpicentrumSyncService.cs` was "uncommitted" — it was already inside the closing commit `6f3fbd8`; working tree was clean except the handoff file itself.
- Parent's step 4 ("finish the review") was executed for: P7 (Library/Cms/Contacts/Rendering/Translation/Notifications), P8 (Platforms), P4 leftovers (MauiProgram, AppShell, Infrastructure, Diagnostics, Profiles, Updates, Identity), and the bulk of P9 (GroupChatViewModel, ChatViewModel receive path, SettingsViewModel + .Community + .Updates, WorkplaceViewModel, ContactsViewModel, LibraryViewModel(+Actions), DocumentViewer/Browser, NewChat/NewGroup, ChatList, Notifications, SmartSearch, AddAssignment, Logbook main). One P10 file read (CmsEditorPage).
- Parent's risk "`FindInboundByHeaderAsync` relies on sqlite-net binding `byte[]` as BLOB — untested" is **still untested**. I re-read the code (`MessageRepository.cs:50`) and the same pattern exists in `ChatSessionRepository.GetByPeerPublicKeyAsync/GetAllByPeerPublicKeyAsync` (which I extended to more callers this session). sqlite-net-pcl normally binds `byte[]` as BLOB and the column is written from a `byte[]` row property, so it should match — but nobody has run it.
- Parent's items not touched: Faltus tunnel, sticky-column verification on S23+, retention of system payloads, WAL — all still open.
- Not done from parent: no device test, no relay redeploy, no 1.92 version bump (csproj still 1.91 / 94).
- New findings this session include **three genuine bugs** that are independent of the message path (contact e-mail wipe, update resume splice, SmartSearch race) — they are the strongest argument for shipping the review.

## Reference Documents

- Parent handoff (full detail of 1.80–1.91 speed work, review parts P1–P6, smoke-test checklist, rollback boundary `2b3aef1`, infra notes, release recipe gotchas) — **read its "Quick Start", "Smoke-test checklist", "Infra notes", "Repo / tooling gotchas" sections; they are still accurate and are not repeated here.**
- Auto-memory index `C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md` (HARD RULES: only signed Release builds on real devices; self-healing requirement; batch builds; adb technique).
- No CLAUDE.md in this repo.

## The Goal

SecureApp (.NET MAUI + self-hosted ASP.NET relay on a Raspberry Pi) is getting a whole-codebase quality pass the user asked for: logic bugs, duplicated code, simplifications — **"ale ne na ukor zpomaleni"** (never at the cost of speed) — **without creating a release** (user: "Nevytvarej release rozdrl kod na casti..."). Each part is read, fixed behaviour-preservingly (bug fixes allowed when clearly wrong), built on Windows/Android/relay, and committed locally. The end state is a reviewed, buildable branch that the user then ships as release 1.92 (build 95) after on-device smoke tests.

## Where We Are

Local commits since release 1.91 (`2b3aef1`), oldest first (nothing pushed; `pi` and `github` remotes are still at `2b3aef1`):

- `7ef7311` P1 relay — outbox `PeekOutbox`/`DeleteOutboxFrame`, `ConnectionRegistry.TrySendAsync` with per-socket `SemaphoreSlim`, upload via temp+rename, `/library/files/{id}` streamed.
- `2e8f628` P2/P3 Domain+Data — `RatchetService.SessionLocks`, `FindInboundByHeaderAsync`, schema v18 indexes, `EntityMaterializer` reflection cache, `SqlTime`, `ActiveKeyHelper`.
- `6fd358e` P4/P5/P11 — supervisor single-flight `RunBackgroundJobsAsync`, `RelayHttp`, `SharedKeyWire`, `RelayConnection`.
- `1b21a73` P5–P10 partial — `ChatViewModel.LoadAsync` uses `GetVisibleBySessionAsync`, `ChatAttachmentPicker`, WebSocket client send lock.
- `6f3fbd8` session commit of the previous handoff (also contains `OpicentrumSyncService.WithSessionAsync` + `LeaveGridRowRegex`).
- **`03e13a7` P7** (this session) — see below.
- **`76bb780` P8**
- **`49736ce` P4 leftovers**
- **`230e9f7` P9 batch 1**
- **`6a6be8e` P9 batch 2**
- **`d29f0ef` P9 batch 3**

Net diff of review work vs 1.91: 69 files, +865 / −1118 lines (`git diff --stat 2b3aef1 HEAD -- src relay`). This session alone (`6f3fbd8..HEAD`): 24 files, +117 / −84.

What changed this session, by file (each verified to build; none run):

- `Library/HttpLibraryReviewService.cs` — own copy of `GetHttpEndpointAsync` replaced by `RelayHttp.GetHttpEndpointAsync` (identical logic, same Czech error text). Stale `<remarks>` ("duplicated rather than shared") fixed in this file, `HttpCmsAssignmentService`, `HttpContactDirectoryService`, `HttpSharedContactService`, `HttpDiagnosticsReporter`.
- `Library/HttpCmsAnnotationService.cs` — `EncryptAsync` now uses `SharedKeyWire.Combine`; `NonceLength/AuthTagLength` constants alias `SharedKeyWire`'s.
- `Library/HttpSharedLibraryService.cs` — one mis-indented method fixed (cosmetic).
- `Notifications/ActiveChatThread.cs` — `MarkActiveThreadReadAsync` uses `IChatSessionRepository.GetAllByPeerPublicKeyAsync(current.PeerIdentityPublicKey)` instead of `GetAllAsync()` + hex compare.
- `Cms/CmsMediaProvider.cs` — `FetchAsync` writes `part-{guid}` temp then `File.Move(overwrite)`; temp name deliberately does NOT match the `"{id:N}.*"` lookup glob.
- `Platforms/Android/UpdateDownloadService.cs` — **resume fix**: new `destination + ".partial.versioncode"` marker; if a `.partial` exists and its marker differs from the requested `versionCode` (or is missing) the partial is deleted before the Range request. Only when `versionCode > 0`.
- `Platforms/Windows/NativeAppInstaller.cs` — `Quote()` local function doubles `'` in every path embedded in the generated PowerShell script.
- `Diagnostics/AppLogUploader.cs` — `RelayHttp.ToHttpUri(wsEndpoint)` instead of inline scheme switch.
- `Infrastructure/AppLog.cs` — `Time()` lost an empty `try/finally`.
- `Updates/UpdateService.cs` — `SaveToCacheAsync(response, fileName, onProgress, ct)` replaces two identical download loops (full download and Windows delta).
- `Profiles/ProfileRegistry.cs` — `Save` writes `profiles.json.tmp` then `File.Move(overwrite)`.
- `Domain/.../IMessageRepository.cs` + `Data/Repositories/MessageRepository.cs` — new `GetVisibleByGroupAsync(groupChatId)` = `WHERE group_chat_id = ? AND is_system_payload = 0 ORDER BY created_at_utc`.
- `ViewModels/GroupChatViewModel.cs` — both load paths (`LoadAsync` background block and `LoadMessagesAsync`) use `GetVisibleByGroupAsync`; the manual `IsSystemPayload` skip was removed; receive path ignores an envelope whose `message.Id` is already in `Messages`.
- `ViewModels/ChatViewModel.cs` — same `Messages.Any(m => m.Id == message.Id)` guard in the live-receive path.
- `App.xaml.cs` — `ResolveChatRoute` (≈line 1830) uses `GetAllByPeerPublicKeyAsync(original.PeerIdentityPublicKey)` (the `peerKeyHex` local was removed).
- `ViewModels/ContactsViewModel.cs` — `SharedContactItem` got a trailing `string? Email = null`; `ToItem` passes `c.Email`; `PersistReorderAsync` builds `SharedContact(..., list[i].Email)`.
- `ViewModels/SmartSearchViewModel.cs` — `_searchVersion` counter; guards after each of the four awaits; `IsSearching` only cleared by the newest search; empty-query branch also sets `IsSearching = false`.
- `ViewModels/LibraryViewModel.cs` — `SearchAsync` computes `query`/`folder` once and passes `results` to `RefreshCategoriesAsync(unfilteredFiles)` when both are null (saves one relay call on every open).
- `ViewModels/DocumentViewerViewModel.cs` — `CachePathFor(Document)` = `Path.GetFileName(document.FileName)` (fallback `Id:N`) used by `LoadVideoAsync`, `OpenExternallyAsync`, `DownloadAsync`.

Build state at the end of the session: Windows `net10.0-windows10.0.19041.0` 0 errors, Android `net10.0-android` 0 errors, relay 0 errors (all Release). Working tree: only this handoff file (new) + the parent handoff file (+6 lines "Session Closed") are uncommitted until the close commit.

## What We Tried (Chronological)

1. **Onboarding.** Read the parent handoff, `git status/log` (git.exe is NOT on PATH — full path in parent's Quick Start), `ConnectionRegistry.cs`, `MessagingService.ReceiveMessageAsync` (lines 210–256), `MessageRepository.FindInboundByHeaderAsync`. Confirmed lock nesting `ReceiveLocks → SessionLocks` is the only order. Narrated plan, user answered "Pokracuj v kontrole kodu".
2. **P7 (Library/Cms/Contacts/Rendering/Translation/Notifications, ~3.5k lines).** Read all Http*Service files, `NotificationPublisher`, `ActiveChatThread`, `DocumentRenderingService`, `LocalAi*`, `CmsDocumentView/Saver/AnnotationPanel/MediaProvider/ImageProcessor/SelectableText`, `ContactDirectoryData`, `PhoneNumberFormat`, `ContactPhoneCache`. Result: duplicate endpoint code and stale comments removed; CMS media cache made atomic. Rejected: rewriting `HttpSharedLibraryService`'s ~12 methods to `RelayHttp.CreateAuthorizedRequestAsync` (saves ~2 lines each, moves endpoint/auth ordering relative to encryption in `UploadAsync`, not worth the diff). Kept `HttpDocumentDownloadLogService` untouched: its endpoint falls back to `RelayDefaults.DefaultEndpoint` and forces `Path = "/"`, different semantics from `RelayHttp`.
3. **P8 (Platforms).** Read widget provider, `UpdateDownloadService`, `RelayConnectionService`, `MainActivity`, `NativeNotificationService`, Windows installer/DLP. Found the cross-version `.partial` resume splice and the apostrophe-in-path PowerShell breakage. Build Windows + Android: 0 errors.
4. **P4 leftovers.** Read `MauiProgram` (no change), `AppShell` (no change), `AppLog`, `BackgroundRun`, `CachedRouteFactory`, `FontScaling`, `InputFocusCrashGuard`, `MauiSecureVaultKeyStore`, `ActiveProfile`, `ProfileRegistry`, `UpdateService`, `IdentityBackupService`, `AppLogUploader`, `HttpDiagnosticsReporter`. Fixed the small items above. Left `IdentityBackupService` endpoint code alone: its scheme mapping is `wss→https else http` (so an `https` base would become `http`) plus `Path="/"` — not equivalent to `RelayHttp.ToHttpUri`.
5. **P9 GroupChatViewModel (1,158 lines, read fully).** Verified that group "delete message" commands are sent with `isSystemPayload: true, groupChatId: ...` and NO `groupMessageId`; delivery acks are sent via `SendMessageAsync(..., isSystemPayload: true)` WITHOUT a group id (`App.xaml.cs:1266`). Therefore filtering `is_system_payload = 0` in SQL cannot change `AggregateOutboundStatusByGroupMessageId` (which only looks at outbound rows with a `GroupMessageId`). Added `GetVisibleByGroupAsync`. Also noticed a consequence of the earlier `ReceiveLocks` change: the loser of the app-handler/page-handler race now returns the stored row instead of throwing, so the page handler must not add a second bubble if the same envelope is delivered twice → `Messages.Any` guard in both thread VMs.
6. **P9 SettingsViewModel (+Community, +Updates).** Read fully, no bugs. Noted two security-relevant leftovers (see Risks): WireGuard `.conf` with private key stays in cache after share; decrypted docs stay in cache.
7. **P9 ContactsViewModel — real bug.** `PersistReorderAsync` constructed `SharedContact` without the 7th (`Email`) argument. Confirmed in `RelayDatabase.cs:2550–2554` that the relay upsert is `ON CONFLICT(id) DO UPDATE SET ... email = @email` (overwrites with NULL). So a drag-reorder on PC blanked the e-mail of every contact whose `SortOrder` changed. Fix: carry `Email` through `SharedContactItem`. (`AddContactViewModel` creates new contacts without e-mail — correct.)
8. **P9 LibraryViewModel.** Found that `SearchAsync` did a filtered `SearchAsync` and then `RefreshCategoriesAsync` did an unfiltered one — identical when no filter is active. Reuse implemented.
9. **P9 SmartSearchViewModel.** `OnSearchQueryChanged => _ = SearchAsync()` per keystroke with no cancel/ordering; fixed with a version counter. (ContactsViewModel already debounces its own search; SmartSearch did not.)
10. **P9 DocumentViewerViewModel.** `Path.Combine(CacheDirectory, document.FileName)` where `FileName` can come from another member's upload → `CachePathFor`.
11. **Pattern greps over Presentation:** `GetAllAsync()`, `.Result`, `.Wait()`, `async void`. `.Result` hits in `RoomStatusViewModel` are on already-awaited tasks (safe). `GetAllAsync()` + peer-key filter existed in `ActiveChatThread` and `App.xaml.cs` (fixed); other `GetAllAsync()` uses are legitimate (full lists). `async void` handlers in Views (ChatListPage, DocumentViewerPage, CmsEditorPage, DocumentBrowserPage...) — sampled ChatListPage/DocumentViewerPage/CmsEditorPage: the annotation handlers have try/catch; `OnRenameClicked`, `OnResetSessionClicked` etc. rely on the VM command catching its own errors (the VM commands I read do).
12. **Rejected/abandoned:** merging the two `CategoryDisplayName` switches (`NotificationsViewModel` returns plural "Zprávy"/"Skupiny", `NotificationDetailViewModel` singular "Chat"/"Skupina" — deliberately different); introducing a shared base class for the repeated `[ObservableProperty] IsLoading/StatusErrorMessage/HasStatusError` pattern (source-generator `partial` properties on a base class — risk > benefit); caching text pagination in `DocumentRenderingService` (behaviour change in memory use, flagged only).

## Key Decisions

- **Review stays local; no release** until the user says so (user's standing instruction + their answer "Pokracuj v kontrole kodu"). No push, no relay deploy, no csproj version bump.
- **Bug fixes are allowed inside the "behaviour-preserving" rule** when the old behaviour is plainly wrong (data loss, corrupt resume, duplicate bubble). Each is listed with its reason in the commit message (Czech, ASCII-ish without diacritics because of the PowerShell here-string/`git commit -m` quoting gotcha).
- **Do not rewrite working Http*Service call patterns** purely to shorten them when ordering/semantics could shift (`HttpSharedLibraryService`, `HttpDocumentDownloadLogService`, `IdentityBackupService` left as is). Rejected alternative: converting everything to `RelayHttp.CreateAuthorizedRequestAsync`.
- **System-payload filtering in SQL for groups is safe** because delete commands carry `group_chat_id` but no `group_message_id`, and acks carry neither (evidence above). Rejected alternative: keep loading everything and filter in memory.
- **`Messages.Any(m => m.Id == message.Id)` instead of a dedupe set** — thread sizes are tens to a few hundred items; a linear scan per incoming message is negligible and needs no extra state to keep in sync with deletes.
- **Temp-file naming for the CMS media cache** (`part-{guid}`): must not start with the item's id, because `FetchAsync`'s existence check is `EnumerateFiles(cacheDir, "{id:N}.*")`; a `{id}.tmp` name would be mistaken for a finished file by a concurrent fetch.
- **Update-resume marker only when `versionCode > 0`:** `UpdateDownloadService.Start` can be called with 0 from older call sites; in that case behaviour is exactly the old resume.
- **Left security-posture items alone** (cache plaintext, WireGuard conf) — product/security decisions, not refactors.

## Evidence & Data

### Commit log (all 2026-10-22, local only)

| Commit | Scope | Files / lines |
|---|---|---|
| `7ef7311` | P1 relay | see parent |
| `2e8f628` | P2/P3 Domain+Data | see parent |
| `6fd358e` | P4/P5/P11 | see parent |
| `1b21a73` | P5–P10 partial | see parent |
| `6f3fbd8` | previous session close (OpicentrumSyncService + parent handoff) | see parent |
| `03e13a7` | P7 Library/Cms/Notifications | 8 files |
| `76bb780` | P8 Platforms | 2 files |
| `49736ce` | P4 leftovers | 5 files (ProfileRegistry, UpdateService, AppLogUploader, HttpDiagnosticsReporter, AppLog) |
| `230e9f7` | P9 batch 1 (group SQL filter, duplicate bubble guard, App route) | 6 files |
| `6a6be8e` | P9 batch 2 (contacts e-mail, SmartSearch, Library) | 3 files |
| `d29f0ef` | P9 batch 3 (DocumentViewer cache path) | 1 file |

Aggregate: review vs 1.91 = 69 files, +865/−1118 lines; this session (`6f3fbd8..HEAD`) = 24 files, +117/−84.

### Verified vs NOT verified

| Item | How | Result |
|---|---|---|
| Windows/Android/relay build after each batch | `dotnet build ... -c Release` (Czech output: grep `Počet chyb`) | 0 errors every time |
| Relay upsert overwrites e-mail | read `RelayDatabase.cs:2550–2554` | `email = @email` in `DO UPDATE SET` → confirmed bug |
| Group delete/ack rows never have `group_message_id` | grep `isSystemPayload: true` (4 call sites) | none pass `groupMessageId`; acks pass no `groupChatId` |
| `byte[]` bound as BLOB in `WHERE header_dh_public_key = ?` / `peer_identity_public_key = ?` | NOT run | still untested — check first on a device or in a tiny test |
| Update `.partial` marker logic | NOT run | needs a real interrupted download across two versions |
| Windows PowerShell quoting with `'` in path | NOT run | logic only |
| SmartSearch ordering | NOT run | |
| Anything on a device (S23+, PC) | none | review never installed |

### Review coverage inventory (what was actually READ this session; "clean" = read fully, no change needed)

| Area | Files read | Outcome |
|---|---|---|
| Library | `HttpSharedLibraryService` (449 l), `HttpCmsAnnotationService` (207), `HttpLibraryReviewService` (174), `HttpCmsAssignmentService` (86), `HttpContactDirectoryService` (126) | endpoint/crypto-wire dedupe, stale remarks |
| Cms | `CmsDocumentSaver`, `CmsDocumentView`, `CmsAnnotationPanel`, `CmsMediaProvider`, `CmsImageProcessor`, `FfmpegCmsVideoProcessor`, `SelectableText` | atomic media cache; rest clean (temp video leak flagged) |
| Contacts | `HttpSharedContactService`, `ContactPhoneCache`, `PhoneNumberFormat`, `ContactDirectoryData` (first 80 l) | clean |
| Rendering/Translation | `DocumentRenderingService` (315), `HttpDocumentDownloadLogService`, `LocalAiLibraryTranslationService` (296, `#if WINDOWS`), `LocalAiModelDiscovery` | clean (perf note on text pagination) |
| Notifications | `NotificationPublisher` (289), `ActiveChatThread` (140) | SQL peer lookup |
| Platforms/Android | `NotificationsWidgetProvider` (356), `UpdateDownloadService` (260), `RelayConnectionService`, `MainActivity`, `NativeNotificationService`, `NativeAppInstaller`, `MainApplication` | resume fix |
| Platforms/Windows | `NativeAppInstaller`, `NativeDlpService` | quoting fix |
| Root/Infra | `MauiProgram` (352), `AppShell.xaml.cs` (210), `AppLog`, `BackgroundRun`, `CachedRouteFactory`, `FontScaling`, `InputFocusCrashGuard`, `MauiSecureVaultKeyStore` | clean / tiny |
| Diagnostics/Updates/Identity/Profiles | `AppLogUploader`, `HttpDiagnosticsReporter`, `UpdateService`, `IdentityBackupService`, `ActiveProfile`, `ProfileRegistry` | dedupe + atomic save |
| ViewModels | `GroupChatViewModel` (all), `ChatThreadViewModelBase`, `SettingsViewModel` (all) + `.Community` + `.Updates`, `WorkplaceViewModel`, `ContactsViewModel`, `AddContactViewModel` (tail), `LibraryViewModel` + `.Actions`, `DocumentViewerViewModel`, `DocumentBrowserViewModel`, `NewChatViewModel` (90–374), `NewGroupViewModel` (60–220), `ChatListViewModel` (130–337), `NotificationsViewModel` (120–284), `SmartSearchViewModel` (all), `AddAssignmentViewModel` (60–300), `LogbookViewModel` (120–300) | 4 bug fixes, 2 perf fixes |
| Views | `CmsEditorPage` (all), `ChatListPage` (280–370), `DocumentViewerPage` (215–357) | clean (async void handlers guarded or delegate to VM commands) |

Size map of Presentation (non-obj .cs, lines): ViewModels 51 files / 11,341; Views 36 / 2,012 (+XAML code-behind elsewhere); Workplace 18 / 1,952; Transport 12 / 1,209; Library 6 / 937; Chat 13 / 932; Cms 9 / 897; Platforms/Android 8 / 885; Infrastructure 13 / 761; Translation 4 / 465; Notifications 4 / 462; Contacts 4 / 407; Rendering 2 / 336; Controls 4 / 203; Diagnostics 2 / 188; Profiles 2 / 171; Updates 2 / 167; Identity 1 / 153; Platforms/Windows 4 / 129; Logbook 1 / 118; root (`App.xaml.cs` 1,656, `AppShell.xaml.cs` 176, `MauiProgram.cs` 304).

### Why each real fix was classified as a bug (the reasoning that is expensive to re-derive)

- **Contact e-mail wipe:** relay `POST /contacts` → `SetSharedContact(id, displayName, phone, note, sortOrder, createdAtUtc, email)` → SQL `ON CONFLICT(id) DO UPDATE SET display_name = @name, phone = @phone, note = @note, sort_order = @sort, updated_at_utc = @now, email = @email`. `SharedContactDto` has `Email = null` default, so an older/short client record deserializes with `null` and the upsert writes NULL. The e-mail column (added 2026-10-08) is what `SettingsViewModel.ReconcileArimContactAsync` and the admin member screen (`ArimContactEmail`) rely on, so wiping it silently breaks the ARIM profile match for that person. Affected rows: only those whose `SortOrder != index` after a reorder (the loop skips unchanged ones).
- **Update resume splice:** `DownloadAsync` computed `have = new FileInfo(partial).Length` and sent `Range: bytes={have}-` regardless of which version the partial belonged to. A relay serving a different APK answers 206 with the tail of the NEW file; appended to the head of the OLD → file length equals `total` (ContentRange length) so the "truncated" guard passes, the file is renamed to `secureapp-update.apk`, and the installer rejects it, repeatedly, because the partial is only ever removed by a successful move.
- **Duplicate bubble:** since `ReceiveMessageAsync` returns the stored row for a re-delivered envelope (idempotency guard + the new `ReceiveLocks`), `HandleEnvelopeReceivedAsync` in both thread VMs would `Messages.Add` it again. The id of the returned row equals the id of the bubble built from the first delivery (`ChatMessageItem(message.Id, ...)` / `GroupMessageItem(message.Id, ...)`).
- **SmartSearch race:** each keystroke ran four awaited repository calls; the last assignment wins, so a slow query for "no" could overwrite "novak". Version counter fixes assignments and spinner ownership.

### Residual duplicate / cleanup candidates (still open)

- `ToggleExpanded` in `ContactsViewModel` (x2 classes), `LogbookViewModel`, `NotificationsViewModel`; `OnRequestNavigate` in 4 pages; `OnViewModelPropertyChanged` in 4 pages; `[ObservableProperty]` IsLoading/Status pairs in 4 VMs; translation-start block duplicated between `LibrarySubcategoryDetailViewModel` and `LibraryViewModel.Actions`; "find existing session for member" loop in `GroupChatViewModel` and `NewGroupViewModel`. (All unchanged from parent's list; deliberately not merged.)
- `DocumentRenderingService` re-wraps the whole text document for every page render and calls `Conversion.GetPageCount` per PDF render (functional, slow for long text docs).
- Temp video files from `FfmpegCmsVideoProcessor` (`cms-video-*.mp4`) are never deleted after upload.

## Code Analysis

- `IMessageRepository` now has: `GetVisibleBySessionAsync` (1:1: `group_chat_id IS NULL AND is_system_payload = 0`), `GetVisibleByGroupAsync` (`group_chat_id = ? AND is_system_payload = 0`), `FindInboundByHeaderAsync(sessionId, dhPublicKey, messageNumber)`. `GetByGroupAsync` still exists (used nowhere else in `src` after this session except via interface — verify before deleting).
- `RelayHttp` (`Transport/RelayHttp.cs`): `ToHttpUri(Uri)` (ws→http, wss→https, other unchanged, keeps path), `GetHttpEndpointAsync(repo, ct)` (throws Czech "Zatím není nastavená adresa relay serveru…"), `CreateAuthorizedRequestAsync(method, path, repo, vault, ct)`, `AddDeviceAuthAsync` (headers `X-Device-Id`, `X-Device-Secret`). Services not on it: `HttpDocumentDownloadLogService` (default-endpoint fallback), `UpdateService`, `IdentityBackupService`, `HttpDevicePolicyService`, `HttpPiccRosterService`, `HttpCommunityBoardService` (parent's list).
- `SharedKeyWire`: `NonceLength = 12`, `AuthTagLength = 16`, `Combine`, `Split(wireBytes, tooShortMessage)` (throws `InvalidOperationException`). `HttpCmsAnnotationService.TryDecryptAsync` still hand-slices (returns null instead of throwing) on purpose.
- `UpdateDownloadService.DownloadAsync` (Android foreground service): files in `CacheDir`: `secureapp-update.apk`, `.partial`, `.versioncode` (completed version), new `.partial.versioncode` (version of the partial). Range resume: `206` → append, `200` → restart. Truncation guard compares file length to `total`.
- `LibraryCmsMediaProvider.FetchAsync`: cache dir `{CacheDirectory}/cms-media`, files `{id:N}{ext}`.
- `SmartSearchViewModel`: sections Notifications (SQL via `GetPagedAsync` with `SearchText`), chats (`GetAllAsync` + Contains + `DistinctBy(PeerIdentityPublicKey)`), groups, static phone directory, work assignments (−60/+180 days).
- `ContactsViewModel` search is debounced 120 ms (`SearchDebounceMilliseconds`), Czech collation via `CultureInfo("cs-CZ")`; ARIM contacts are `Note == "ARIM"` rows of the same relay table.
- Relay shared contacts: `SharedContactDto(Id, DisplayName, Phone, Note, SortOrder, CreatedAtUtc, Email = null)`; any republish MUST include every field.
- `DocumentViewerViewModel.CachePathFor` fallback for blank names: `document.Id.ToString("N")`.

## Files Changed

### Source code — this session (all committed locally)
- `src/SecureApp.Presentation/Library/HttpLibraryReviewService.cs`, `HttpCmsAnnotationService.cs`, `HttpCmsAssignmentService.cs`, `HttpContactDirectoryService.cs`, `HttpSharedLibraryService.cs` — RelayHttp/SharedKeyWire reuse, stale remarks, indentation
- `src/SecureApp.Presentation/Contacts/HttpSharedContactService.cs`, `Diagnostics/HttpDiagnosticsReporter.cs`, `Diagnostics/AppLogUploader.cs` — remarks / `ToHttpUri`
- `src/SecureApp.Presentation/Notifications/ActiveChatThread.cs` — SQL peer lookup
- `src/SecureApp.Presentation/Cms/CmsMediaProvider.cs` — atomic cache write
- `src/SecureApp.Presentation/Platforms/Android/UpdateDownloadService.cs` — partial-version marker
- `src/SecureApp.Presentation/Platforms/Windows/NativeAppInstaller.cs` — PowerShell quoting
- `src/SecureApp.Presentation/Updates/UpdateService.cs`, `Infrastructure/AppLog.cs`, `Profiles/ProfileRegistry.cs`
- `src/SecureApp.Domain/Interfaces/Repositories/IMessageRepository.cs`, `src/SecureApp.Data/Repositories/MessageRepository.cs` — `GetVisibleByGroupAsync`
- `src/SecureApp.Presentation/ViewModels/GroupChatViewModel.cs`, `ChatViewModel.cs`, `ContactsViewModel.cs`, `SmartSearchViewModel.cs`, `LibraryViewModel.cs`, `DocumentViewerViewModel.cs`
- `src/SecureApp.Presentation/App.xaml.cs` — chat route resolution

### Docs
- `plans/handoffs/HANDOFF_standalone-24caf3f8_code-review-p7-p9_2026-10-22.md` — this file
- `plans/handoffs/HANDOFF_standalone-24caf3f8_opicentrum-perf-code-review_2026-10-22.md` — +6 lines (Session Closed block of the parent)

### Not touched
- csproj version (1.91 / 94), relay code (no relay change this session), XAML, tests (there is no test project run in this workflow).

## User Feedback & Preferences (REQUIRED)

This session had only one explicit instruction; the rest are standing preferences from the parent that still apply.

- **"Pokracuj v kontrole kodu"** — answer to "ship the review as 1.92 or keep reviewing?" → keep reviewing, no release.
- The paste prompt the user used: "Do NOT push, deploy or release without my explicit OK." and "Then wait for my go-ahead before executing." (the onboarding narration was done; the go-ahead came as the line above).
- Standing (from parent): replies terse and in Czech; honest status ("zatím jsem nic nezměřil"); the user tests builds himself; **never slow things down** while simplifying; "Nevytvarej release" for review work; Opicentrum credentials never go through chat/dev tooling; HARD RULE only signed Release builds on real devices.
- Process style that worked this session: small batches → build all three targets → local commit with a Czech, quote-free message; short progress sentence when a step takes long (the harness reminded twice that the user had not heard from me).

## Repo / tooling gotchas hit this session (additions to the parent's list)

- The shell's working directory **persists between PowerShell calls**: a `cd src\SecureApp.Presentation` inside one command changed the primary working directory for later ones (the harness printed "Primary working directory" updates). Use absolute paths or `cd` back to `H:\Visual Studio\C#\Aplikace` at the start of each command.
- `git` writes `LF will be replaced by CRLF` warnings to stderr for every touched file; pipe `2>$null` on `git add`/`git commit` to keep output readable (the commit still succeeds — verify with `git log --oneline -1`).
- `git commit -m @'...'@` with Czech text without diacritics worked; avoid double quotes inside the message.
- Raw interpolated string with a local static function inside the braces works: `$$"""... '{{Quote(path)}}' ..."""` (used in `Windows/NativeAppInstaller.cs`); the local function must be declared before the string.
- `Path.Combine(dir, name)` silently discards `dir` when `name` is rooted — another reason `CachePathFor` uses `Path.GetFileName`.
- `dotnet build` prints `Počet chyb: N`; a clean build of all three targets takes roughly 2–4 minutes together, so build once per batch (user rule: batch builds).
- Do not use `GetAllAsync()` + hex compare to find sessions of one peer any more: `IChatSessionRepository.GetAllByPeerPublicKeyAsync(byte[])` exists (includes Closed sessions) and `GetByPeerPublicKeyAsync` returns the non-closed one.
- `SharedContact` is a positional record with a trailing optional `Email`; any code that republishes a contact must pass every field (the relay overwrites all of them).

## Where We're Going

1. **Decision with the user (still open): ship now or review the remainder first.** Recommendation unchanged: ship soon — the review contains 3 unrelated real bug fixes (contact e-mail wipe, update APK splice, search race) plus the message-path fixes from the parent that have never run on a device. Ship recipe is in the parent's "Where We're Going" #2 and "Quick Start" (relay `POST /admin/deploy` first, then 1.92/95 with the Edit tool on the csproj, Android+Windows publish, push `pi`+`github`, upload both artifacts, verify `/download/*/version`).
2. **Before shipping: verify `byte[]` BLOB matching** (`FindInboundByHeaderAsync`, `GetByPeerPublicKeyAsync`, `GetAllByPeerPublicKeyAsync`) — the cheapest way is a throwaway console/test that opens a SQLCipher db, inserts a row with a `byte[]` key and queries it back; otherwise first smoke test on the S23+ (duplicate delivery + reopening a chat).
3. **Finish P9:** `RedakceViewModel` (+`.LibraryCategories`, `.CmsAssignments`), `RoomStatusViewModel`, `NotificationsViewModel.RoomStatus.cs`, `LibrarySubcategoryDetailViewModel`, `LibraryReviewQueueViewModel`, `CmsAnnotationQueueViewModel`, `LogbookManageViewModel`, `LogbookChecklistViewModel`, `SettingsViewModel.Opicentrum/Identity/Picc/LocalAiTranslation/WorkplaceColors/ScreenCapture/DocumentDownloads/LibraryDocumentAudit`, `ChatListViewModel` (first 130 lines), `NotificationDetailViewModel`, `ProfileLoginViewModel`, calculators.
4. **P10 Views (70 files, mostly code-behind):** only `CmsEditorPage`, `ChatListPage` (lines 280–370), `DocumentViewerPage` (215–357) were read. Still to read: rest of `ChatListPage`, `DocumentViewerPage` top, `ContactsPage` (drag/drop), `OpicentrumBoardPage`, `WorkplacePage`, `LibraryPage`, `LibraryReviewQueuePage`, XAML code-behind that subscribes to VM events (check every `+=` has a matching `-=`).
5. **P6 leftovers:** `Logbook/*`, `Workplace/OpicentrumParsing.cs`, `PiccRosterParser`, remaining `Workplace/*` services; **P4 leftovers:** `Controls/*`, `Infrastructure/AccentPalette`, `ChatAppearance`, `RolePreviewService`, `ScreenCapture*`, `TrustedAdminDevices`, `ActiveProfile`.
6. Optional cleanup if user wants: delete `IMessageRepository.GetByGroupAsync` if nothing else uses it (grep first); delete cms temp video files after upload.
7. Parent's still-open items: verify 1.89/1.90 sticky columns + Volno on S23+ (pull `GET /admin/applog/6b4bf8e9-131b-4304-8368-c831a32c5a2b?kind=metrics&limit=400`), Faltus WireGuard keepalive, WAL decision, system-payload retention decision.

## Risks & Blockers

- **Everything since 1.91 is unrun.** The riskiest parts remain the message path changes from the parent (ratchet lock, `ReceiveLocks`, relay outbox, WebSocket send lock). New this session: `GetVisibleByGroupAsync` and the two bubble guards (low risk). Smoke-test checklist: parent's list (9 items) + (a) drag-reorder a "Firemní kontakt" that has an e-mail, confirm the e-mail survives; (b) start an update download, kill it, publish a newer APK, retry → partial must restart; (c) type fast in Nástěnka search, results must match the final text.
- **Security/product items (not changed, need a decision):** WireGuard `.conf` (contains the peer private key) written to `FileSystem.CacheDirectory` by `SettingsViewModel.DownloadWireGuardConfigAsync` and never deleted; decrypted documents/videos stay in the cache after "Otevřít externě"/"Stáhnout"/video playback (`DocumentViewerViewModel`) — plaintext at rest outside the SQLCipher vault; transcoded CMS videos in `%TEMP%`.
- **Content-Length/Range assumption:** the update-resume fix deletes a partial when the marker is missing — the first update after installing this build will therefore re-download once even for the same version (acceptable).
- S23+ is the user's Remote Control phone (PIN/biometric): do not adb-tap blindly; `adb connect 10.8.0.3:5555` works. Never debug-deploy.
- `LF will be replaced by CRLF` git warnings are cosmetic. Commit messages: single-quoted here-string, no double-quote characters.

## Open Questions

- Ship 1.92 now, or finish P9/P10/P6 first? (User's last word: continue reviewing.)
- Should the WireGuard `.conf` and decrypted-document cache files be deleted after use (needs care on Android: the receiving app may still be reading a content URI), or encrypted/overwritten?
- Should `UpdateDownloadService` also guard against a *different server file* with the same versionCode (e.g. re-uploaded APK without bump)? Currently not handled (`If-Range`/ETag not used).
- Retention for system payloads (acks/key offers) in `messages` — product decision (from parent), still open.
- WAL for relay SQLite — needs a Pi test (from parent), still open.

## Quick Start for Next Session

```bash
# git.exe is NOT on PATH:
#   C:\Program Files\Microsoft Visual Studio\2022\Community\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe
git status -s ; git log --oneline -14      # expect HEAD = 'session: code-review-p7-p9 ...' on top of d29f0ef; remotes pi/github still at 2b3aef1
git diff --stat 2b3aef1 HEAD -- src relay   # 69 files, +865/-1118 (review vs release 1.91)

# Parent handoff (infra, release recipe, smoke tests, rollback boundary 2b3aef1):
#   plans/handoffs/HANDOFF_standalone-24caf3f8_opicentrum-perf-code-review_2026-10-22.md

# Memory index
#   C:\Users\dvora\.claude\projects\H--Visual-Studio-C--Aplikace\memory\MEMORY.md

# Key files to read first
#   src/SecureApp.Data/Repositories/MessageRepository.cs   (BLOB-bound byte[] lookups, GetVisibleBy*Async)
#   src/SecureApp.Presentation/ViewModels/ContactsViewModel.cs  (Email carried through reorder)
#   src/SecureApp.Presentation/Platforms/Android/UpdateDownloadService.cs  (partial-version marker)
#   src/SecureApp.Presentation/ViewModels/GroupChatViewModel.cs  (GetVisibleByGroupAsync, duplicate-bubble guard)
#   relay/SecureApp.Relay/RelayDatabase.cs ~2550 (shared_contacts upsert: overwrites all columns)

# Build verification (all 0 errors at handoff time; output is Czech: grep 'Počet chyb' and ' error ')
dotnet build src\SecureApp.Presentation\SecureApp.Presentation.csproj -f net10.0-windows10.0.19041.0 -c Release
dotnet build src\SecureApp.Presentation\SecureApp.Presentation.csproj -f net10.0-android -c Release
dotnet build relay\SecureApp.Relay\SecureApp.Relay.csproj -c Release

# Remaining review scope: P9 leftovers (Redakce*, RoomStatus, LibrarySubcategoryDetail, Logbook*, Settings partials),
# P10 Views code-behind, P6 Logbook/OpicentrumParsing/PiccRosterParser, P4 Controls/Infrastructure small files.

# Next action
# Ask the user: ship the review as 1.92 (recommended: first verify BLOB matching, then relay deploy + release per parent recipe)
# or continue with P9 leftovers -> P10. Do NOT push/deploy/release without explicit OK.
```
