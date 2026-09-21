# SecureApp — Notification Hub / Personal Work Information Center

Founding prompt for the next major initiative on this project, given by the user
2026-09-20. Saved verbatim (below) because it's long, precise, and phased — worth
re-reading in full before touching any of this, rather than working from a summary.
Not started yet as of the date above; see **Status** at the bottom.

## Visual/design reference

Two images the user attached alongside this prompt, asking for the app's
visualization and color palette to move toward this style. Saved in-repo (not just
this doc) so a future session doesn't need them re-uploaded:

- `design/notification-hub-reference/widget-reference.jpg` — Android share-sheet-style
  composite: several notification/archive screens, a widget "add to home screen"
  overlay (⋮ / download / Sdílet buttons), and the actual home-screen widget preview —
  dark wallpaper, large clock, three colored counter pills (🔴 Důležitá 7 / 🟠 Čeká na mě
  12 / 🟢 Dnes 28), then a compact white card listing the 3 most recent notifications
  (colored leading icon circle, title, timestamp, one-line preview, unread dot), "+2
  další oznámení", edit/resize/remove controls below. **This is the literal target
  design for the Android home-screen widget** — the prompt is explicit: "Do not invent
  a completely different widget design. Use the supplied image as the visual and
  functional reference."
- `design/notification-hub-reference/notifications-screen-wireframe.png` — an annotated
  (Czech callouts) wireframe of the in-app Notifications screen: dark theme, top bar
  (time/date/settings gear), "Oznámení (50)" header with search + overflow icons,
  horizontal pill filter chips (Vše 50 / Důležité 7 / Zprávy 12 / Systém 6), then
  grouped sections each with a colored left-edge accent bar and a count badge —
  Důležité (red), Zprávy (blue), Systém (green), Aplikace (purple), Ostatní — each row
  showing a colored icon-circle avatar, title, timestamp, one-line preview, and an
  unread dot. Annotations call out: clear top panel, filter/search, priority-colored
  grouping with counts, per-notification anatomy, readability at high volume, quick
  access.

Palette signal from both images: dark surface as the base, a small fixed set of
accent colors used consistently for MEANING not brand — red (critical/důležité),
orange (waiting/action), blue (normal/messages), green (today/system/positive),
purple (apps) — plus pill-shaped chips/badges and colored left-border card accents.
This is a real departure from the app's current `Styles.xaml`/`Colors.xaml` (see
Status below) and should be reconciled with the existing `Success`/`Danger`/`Warn`/
`AccentStrong` token set rather than run alongside it as a second palette.

---

## The prompt (verbatim)

> DEVELOPMENT PROMPT – COMPLETING THE EXISTING .NET MAUI APP
>
> ### 1. IMPORTANT – EXISTING PROJECT
>
> This is not a new application.
>
> The application already partially exists and has been developed using:
>
> - C#
> - .NET MAUI
> - Android as the primary target
>
> Before making any changes, analyze the existing project and its current architecture.
>
> Do NOT rewrite the application from scratch.
>
> Do NOT replace working functionality unnecessarily.
>
> First determine:
>
> - what is already implemented,
> - what is working,
> - what is partially implemented,
> - what is missing,
> - what should be refactored,
> - what can be reused.
>
> Keep the existing architecture and code wherever it makes sense.
>
> If a major architectural change is proposed, explain why it is necessary before implementing it.
>
> ### 2. MAIN PURPOSE OF THE APPLICATION
>
> The application is intended to be a personal/work information center combining:
>
> - notifications,
> - important notifications,
> - normal notifications,
> - workplace assignments,
> - calendar,
> - contacts,
> - search,
> - archive,
> - Android notifications,
> - Android home-screen widget.
>
> The main goal is to allow the user to understand within a few seconds:
>
> - what is important,
> - what is new,
> - what requires attention,
> - where they are supposed to work,
> - what is coming next,
> - who they need to contact,
> - and where to find an older notification.
>
> This must remain easy to use even when there are hundreds or thousands of archived notifications.
>
> The application should feel like a personal work information center, not just a notification list.
>
> ### 3. MAIN NAVIGATION
>
> The application should have four primary sections:
>
> 1. IMPORTANT
> 2. NORMAL
> 3. WORKPLACE
> 4. ARCHIVE
>
> There should also be global access to:
>
> - Smart Search
> - Contacts
> - Calendar
>
> The exact navigation implementation should follow the existing application's architecture and UI conventions where possible.
>
> ### 4. IMPORTANT
>
> The IMPORTANT section contains:
>
> - important notifications,
> - unread important notifications,
> - notifications requiring action,
> - critical information,
> - notifications manually marked as important by the user.
>
> This screen should be extremely clear.
>
> Each notification should be able to display:
>
> - icon,
> - short title,
> - source,
> - time,
> - short preview,
> - read/unread state,
> - priority,
> - optionally related person,
> - optionally related workplace.
>
> Important notifications must not disappear among ordinary information.
>
> Example:
>
> ```
> IMPORTANT (3)
>
> 🔴 Hospital – Operating Room 7
> Schedule change for today's procedure
> 10:25
>
> 🟠 Department meeting
> Meeting moved to 07:30
> 09:48
>
> 🔴 Work assignment
> Change of today's workplace
> 08:15
> ```
>
> ### 5. NORMAL
>
> The NORMAL section contains ordinary information.
>
> Examples:
>
> - informational messages,
> - routine operational information,
> - application updates,
> - system messages,
> - less important notifications.
>
> Default sorting: newest first.
>
> The user should also be able to group notifications by:
>
> - day,
> - source,
> - application,
> - person,
> - type.
>
> Example:
>
> ```
> TODAY (27)
> 10:25 Notification A
> 09:42 Notification B
> 08:15 Notification C
>
> YESTERDAY (42)
> ...
>
> OLDER
> ...
> ```
>
> Groups should be expandable/collapsible.
>
> ### 6. WORKPLACE
>
> One of the most important sections. WORKPLACE must not simply display
> notifications — it should show the user's current and upcoming work assignments.
>
> Example:
>
> ```
> TODAY
> Workplace: 📍 Operating Room 7
> Assignment: Anesthesia
> 07:00–15:30
>
> UPCOMING ASSIGNMENTS
> Monday    Operating Room 3    07:00–15:30
> Tuesday   Outpatient clinic   08:00–16:00
> Wednesday VACATION
> Thursday  BUSINESS TRIP
> Friday    Operating Room 5
> ```
>
> The application should support different assignment/status types, e.g.:
> WORK, VACATION, SICK LEAVE, BUSINESS TRIP, TRAINING, DAY OFF, ON-CALL, OTHER.
> Each type may have its own icon and visual indicator.
>
> ### 7. CALENDAR
>
> Integrated with WORKPLACE. Day / Week / Month views; Week is primary.
>
> ```
> WEEK
> MON  Operating Room 7   07:00–15:30
> TUE  Operating Room 3   07:00–15:30
> WED  VACATION
> THU  BUSINESS TRIP
> FRI  Operating Room 5
> ```
>
> Clicking an assignment opens its detail.
>
> ### 8. CONTACTS
>
> Accessible from the app and, where appropriate, the widget. A contact can contain:
> name, photo/avatar, position, workplace, phone, email, additional info. Actions:
> Call, SMS, Email, Open details — use native Android APIs where possible. If a
> notification is associated with a person, that person should be directly
> accessible from the notification.
>
> ### 9. ANDROID HOME-SCREEN WIDGET
>
> Visual design supplied as reference images (see **Visual/design reference** above —
> do not invent a different design). Quick access to: important notifications, new
> notifications, today's workplace, contacts, calendar. Deep links into the app
> (Widget → Important → opens IMPORTANT; → New → new-notifications list; → Workplace →
> today's assignment; → Contact → that contact; → Calendar → today). Stay fast and
> lightweight — don't overload it with information.
>
> ### 10. NEW NOTIFICATION INDICATORS
>
> On arrival: update app state, update counters/badges, update the widget, optionally
> show an Android system notification. A new important notification should be
> visually more prominent than an ordinary one.
>
> ### 11. SMART SEARCH
>
> Global search, not limited to exact text match — e.g. "anesthesia" should also
> surface related workplaces/calendar assignments/contacts, not just literal text
> hits. "Novak" → contact + notifications from/mentioning them + related workplace
> info. "vacation" → calendar entries, workplace status, related notifications.
>
> ### 12. SMART SEARCH FILTERS
>
> Quick filters: All / Important / Unread / People / Workplace / Calendar / Archive.
> Additional filters: date/time, person, workplace, notification type, priority,
> read/unread, archived/not archived. Filters should combine.
>
> ### 13. HOME / DASHBOARD
>
> Top area: time/date, greeting, today's assignment. Quick counters (Important /
> New). Most-important-information list. Today's workplace card with a Details
> link. Quick actions: Search / Contacts / Calendar. **Must not become a long
> notification list** — its job is a fast overview only.
>
> ### 14. ARCHIVE
>
> Archiving ≠ deletion: stays stored, disappears from active lists, stays
> searchable, can be restored. Supports search, filter, and sort by date/source/
> person/workplace.
>
> ### 15. NOTIFICATION DETAILS
>
> On open, show: title, source, date/time, content, priority, status (New / Read /
> Resolved / Archived), related info (person / workplace / calendar event / related
> notifications). Actions: Mark as Read, Mark as Important, Archive, Share, Open
> Related Information (+ type-specific actions).
>
> ### 16. SMART GROUPING
>
> Collapse large same-category runs instead of listing every item — e.g.
> "OPERATING ROOMS (15)" showing only the latest with a "Show all 15" expander.
> Grouping keys: person, workplace, application/source, type, date, category.
>
> ### 17. PRIORITIES
>
> Max ~3–4 levels: 🔴 Critical, 🟠 Important, 🔵 Normal, ⚪ Informational. Color
> should encode importance, not app identity — keep the palette limited.
>
> ### 18. DESIGN PRINCIPLES
>
> Modern, clean, professional, fast, highly readable, fit for daily long-term use.
> Large readable elements, high contrast, clear hierarchy, minimal decoration,
> limited palette, rounded cards where appropriate, simple icons. Support light AND
> dark mode. Follow the existing app's design system where one already exists.
>
> ### 19. PERFORMANCE
>
> Stay responsive at 100 / 500 / 1,000 / 10,000+ (archived) notifications — no
> load-everything-at-once. Use MAUI virtualization, incremental loading/pagination,
> efficient collections, DB indexing, efficient search. Scrolling must stay smooth;
> search must stay fast even against a large archive.
>
> ### 20. DATA MODEL
>
> Adapt the existing model where one already exists — don't blindly duplicate.
> Minimum shape:
>
> - **Notification**: Id, Title, Body, Timestamp, Source, Category, Priority, Read,
>   Archived, Important, RelatedPersonId, RelatedWorkplaceId, RelatedCalendarEventId
> - **Person**: Id, Name, Position, Workplace, Phone, Email, Photo
> - **Workplace**: Id, Name, Description
> - **Assignment**: Id, Date, StartTime, EndTime, WorkplaceId, Type, Note
> - **CalendarEvent**: Id, Title, Start, End, Type, Location, Description
>
> ### 21. ARCHITECTURE
>
> Keep UI, data, and business logic separated: DATA → SERVICES/BUSINESS LOGIC →
> VIEW MODELS/STATE → UI. Don't put application logic directly in MAUI pages.
> Respect the existing architecture if it's already sound.
>
> ### 22. FUTURE BACKEND / SYNCHRONIZATION
>
> Architecture should allow future integration with a remote API/custom server, DB
> sync, calendar sync, contact sync, additional notification sources. Don't couple
> the UI tightly to one specific data source.
>
> ### 23. OFFLINE SUPPORT
>
> Basic functionality (stored notifications, archive, calendar, workplace
> assignments, contacts) should work offline; sync opportunistically when online.
> Don't make the base UI depend on a permanent connection unless the existing app
> already specifically requires it.
>
> ### 24. NOTIFICATION SYSTEM
>
> Distinguish: new, update-to-existing, deletion, priority change, archive,
> read-state change. On an important arrival, update: app state → badge/counter →
> widget → Android system notification (where appropriate).
>
> ### 25. CONTACTS FROM NOTIFICATIONS
>
> If a notification is tied to a person, surface 👤 name + [Call]/[Email]/[Contact
> Details] inline. If tied to a workplace, surface 📍 workplace + link to its
> details/today's assignment.
>
> ### 26. NOTIFICATION ↔ WORKPLACE ↔ CALENDAR RELATIONSHIPS
>
> These three should be interconnected both directions — e.g. a notification about
> an assignment change links to the calendar day, which links to the workplace,
> which links to the assignment detail; and the reverse (Calendar → Assignment →
> related notification; Workplace → Assignment → related notification).
>
> ### 27. GESTURES AND QUICK ACTIONS
>
> Swipe right → mark read. Swipe left → archive. Long press → context menu (mark
> important, mark read/unread, archive, share, open related info). Avoid gestures
> that could accidentally destroy information — archiving must never be permanent
> deletion.
>
> ### 28. LARGE DATA SETS
>
> E.g. Today (27) / Yesterday (42) / This week (184) / Older (800+), collapsible.
> Suggested initial state: Today + Yesterday expanded, This week + Older collapsed
> (adjustable after testing).
>
> ### 29. INFORMATION PRIORITY ON THE DASHBOARD
>
> Answer, in this order: (1) what needs attention, (2) what's new, (3) where to
> work today, (4) what's coming next, (5) where to find older info. Never an
> overwhelming wall of notifications.
>
> ### 30. DEEP LINKS FROM THE WIDGET
>
> Widget → Important/New/a specific workplace/a specific contact/Calendar, each
> opening the matching in-app screen. Use the appropriate MAUI/Android deep-link
> mechanism, respecting existing navigation.
>
> ### 31. OPENING THE APP FROM AN ANDROID NOTIFICATION
>
> Should open that notification's own detail, not just the app's home screen. If
> already archived, open the archived detail if possible; if the notification no
> longer exists, fall back gracefully.
>
> ### 32. DEVELOPMENT PROCESS — WORK INCREMENTALLY, DO NOT DO EVERYTHING AT ONCE
>
> - **Phase 1 — Analyze the existing project**: structure, .NET/MAUI version,
>   target frameworks, Android config, navigation, pages, ViewModels, services,
>   models, database, notification handling, existing widget (if any), resources,
>   styles, existing APIs. Produce a concise assessment: already implemented /
>   partially implemented / missing / needs refactoring / can be reused / potential
>   technical problems. **Do not rewrite anything yet.**
> - **Phase 2 — Data model**: review existing models, create new ones only where
>   necessary, wire the Notification ↔ Person ↔ Workplace ↔ Assignment ↔
>   CalendarEvent relationships, avoid duplication.
> - **Phase 3 — Main UI**: dashboard, the four main sections, navigation,
>   notification list, notification detail — compatible with existing MAUI
>   architecture.
> - **Phase 4 — Smart search**: global + full-text, filters (person/workplace/date/
>   priority/read-unread/archive), sorting.
> - **Phase 5 — Workplace and calendar**: day/week/month views, assignments,
>   vacation/business-trip/training/day-off/other absence types.
> - **Phase 6 — Contacts**: list, details, phone, email, relation to notifications.
> - **Phase 7 — Widget**: per the supplied reference image, not a reinvented design;
>   updates on new notification / important change / today's assignment change /
>   calendar change.
> - **Phase 8 — Android notifications**: reception, categories, priorities, badges,
>   deep links, notification details, widget updates — per Android best practices
>   for the project's target Android version.
> - **Phase 9 — Performance and testing**: 10 / 100 / 500 / 1,000 / 10,000
>   (archived) notifications — startup time, scroll perf, memory, DB perf, search
>   perf, widget update perf, notification handling, navigation, deep links.
>
> ### 33. UX PRINCIPLE
>
> For every screen/feature, ask: "Can the user find the required information within
> approximately three seconds?" If not, simplify. Internal complexity is fine; the
> UI must stay simple. Don't add visual elements just because they're technically
> possible.
>
> ### 34. TARGET STRUCTURE (conceptual — adapt to existing nav, don't force-replace it)
>
> ```
> HOME / DASHBOARD
> ├── 🔴 IMPORTANT      (New, Unread, Important)
> ├── 🔵 NORMAL         (Today, Yesterday, Older)
> ├── 📍 WORKPLACE      (Today, This Week, Assignments, Absence)
> ├── 🗄 ARCHIVE         (Notifications, Search)
> ├── 🔎 SMART SEARCH
> ├── 👤 CONTACTS
> └── 📅 CALENDAR       (Day, Week, Month)
> ```
>
> ### 35. FINAL GOAL
>
> A personal work information center — NOTIFICATIONS + WORKPLACE + CALENDAR +
> CONTACTS + SEARCH in one coherent app — where the user instantly knows: 🔴 what's
> important, 🔵 what's new, 📍 where they have to be, 📅 what's coming next, 👤 who to
> contact, 🔎 where to find something old. Must stay usable from 5 notifications to
> thousands of archived ones.
>
> ### 36. IMPORTANT IMPLEMENTATION RULE
>
> Because the app already exists: **FIRST ANALYZE. THEN PLAN. THEN IMPLEMENT.**
> Don't immediately modify large parts of the project. Per development step:
> explain what was found → identify the relevant existing files/classes → explain
> what needs to change → keep existing working functionality → make the smallest
> reasonable change → build → fix compile errors → test the affected
> functionality → only then move to the next phase. Prefer incremental,
> maintainable changes over replacing the existing architecture.

---

## Status

**2026-09-21 — Phase 1, 2, 3, 4, 5 (first slice), 8 done.**

Phase 4 — Smart Search (`SmartSearchViewModel`/`SmartSearchPage`, reached via 🔍 on Nástěnka): one
query fanned out across every searchable source that actually exists in the app today —
Notifications (title/body, via `INotificationRepository.GetPagedAsync`'s own `SearchText` filter),
1:1 chats and groups (by peer/group display name — "Novák" jumps straight to that thread, matching
spec §11's own example), and the static hospital phone directory (name/section/extension). Results
render as four collapsible sections, each row pre-built with the route it navigates to
(`SearchResultItem`). Person/Workplace/CalendarEvent search isn't wired in — see Phase 5/6 below for
why. Deliberately Contains-match, not fuzzy/semantic — covers the spec's own examples without the
extra complexity a real full-text engine would add for this data volume.

Phase 8 — real Android notifications: `INativeNotificationService` (Android implementation uses
`NotificationCompat`/`NotificationManagerCompat`, a dedicated channel, `PendingIntent`); the
`POST_NOTIFICATIONS` runtime permission (API 33+) is requested from `MainActivity` on first launch;
`NotificationPublisher` calls it (when supplied) alongside writing the in-app `Notification` row, so
every real incoming chat/group message produces both; tapping a system notification deep-links into
that notification's own detail via `NativeNotificationRouter`'s cold-start (pending-id consumed in
`CreateWindow`) and warm-start (`MainActivity.OnNewIntent`, `LaunchMode.SingleTop`) paths — spec §31's
"open that notification's own detail, not just the home screen."

Phase 6 (Contacts) was superseded by the user's own separate ask that same day: a shared, relay-synced
company phone/extension directory with no chat linking (`ISharedContactService`), not the spec's
Person/CRM model — see `ContactsViewModel`'s own remarks.

Phase 5 (Workplace + Calendar), first slice (2026-09-20, "pokracujem kalendarem") — `WorkAssignment`
domain entity + `AssignmentType` enum (schema v15, local per-device — this is one person's own
schedule, not shared data) plus a relay-synced `Workplace` name catalog (`IWorkplaceCatalogService`,
same shared-reference-catalog pattern as `ISharedContactService`/`ILogbookCatalogSyncService`).
`WorkplacePage` (reached via a 📅 button next to 🔍 on Nástěnka): a "Dnes" card (spec §6's TODAY block,
always accurate regardless of which week is browsed) + a navigable Week strip (spec §7 — "Week is
primary"), each day tappable into `AddAssignmentPage` (create/edit/delete one day's status;
free-typing a new workplace name auto-publishes it to the shared catalog). Verified live on-device
(S23+, via adb/uiautomator) end-to-end: open → Dnes empty state → week strip renders the correct
7-day range → tap a day → save → returns with the row updated → edit → delete → back to empty state.

Phase 5, second slice (2026-09-21) — Month view added alongside Week (a Week/Month toggle on
`WorkplacePage`; spec §7 asks for Day/Week/Month — Day is effectively already covered by the
`AddAssignmentPage` day-detail/editor tapped into from either view, so this slice is just Month).
Verified live on S23+ end-to-end, including a real bug caught in review before it shipped: `LoadAsync`
(bound to the Page's own `OnAppearing`) only ever refreshed Today+Week, never Month — editing a day
from Month view would show stale data until manually toggling away and back; fixed before deploy.

Phase 5, third slice (2026-09-21) — `SmartSearchViewModel` now also searches the personal schedule
(spec §11's own "vacation" example explicitly calls for this): a bounded ±60/180-day window, matched
against the assignment type's Czech label, workplace name, or note, each result deep-linking straight
into `AddAssignmentPage` for that exact day. Still open: the Notification↔Workplace↔Calendar
cross-links spec §26 calls for — `Notification` still has no `RelatedWorkplaceId`/
`RelatedCalendarEventId` FK (deliberately deferred, see that entity's own remarks; there's also no
event source that would populate them yet — nothing currently publishes a Notification about a
schedule change). Phase 7 (Android home-screen widget) and Phase 9 (performance at scale) haven't
been started. Next slice: whichever of those the user picks.

---

**2026-09-20 (superseded snapshot, kept for history) — Phase 1 (analysis) done, Phase 2 (data model) + Phase 3 (main UI) done, first slice.**

Phase 1 findings: `ContactsPage`/`Kontakty` is a static hospital phone directory (transcribed from a
reference PDF), not a dynamic `Person`/CRM-style contact — spec's Person model still to build
(Phase 6). `LogbookPage` tracks performed procedures, not shift/workplace assignments — different
concept from spec's WORKPLACE section (Phase 5). No Android system-notification handling and no
Android home-screen widget existed at all before this pass. Reusable building blocks found and used:
`Success`/`Danger`/`Warn`/`AccentStrong` color tokens, `CardBorder`/`SectionHeader`/`Chip`/`ChipLabel`
styles, and `ContactsViewModel`'s `ContactSectionGroup`/`VisibleEntries` collapsible-section pattern.

Built this pass: `Notification` domain entity + `NotificationPriority`/`NotificationCategory` enums +
`INotificationRepository` (schema v13); `NotificationsPage` (counters, filter chips, grouped
collapsible sections) as the new leading "Oznámení" tab; `NotificationDetailPage` (mark
important/archive, open-related deep link); `NotificationPublisher` wired into `App.xaml.cs`'s
`OnEnvelopeReceived` so a real incoming chat/group message always becomes a Notification, regardless
of which screen is open. New `Info`/`DangerSoft` color tokens for the priority accents, applied to
this new screen only — not an app-wide re-theme.

Deliberately not done yet, per the user's own explicit scoping this pass:
- **Notification sources stay SecureApp-internal only** (2026-09-20 decision) — no Android
  `NotificationListenerService` reading other apps' notifications. The model doesn't block adding
  that later, it's just not built.
- Group-invite/pairing-completed events aren't wired to `NotificationPublisher` yet (only real
  messages are) — straightforward to add, cut for scope this pass.
- Phases 5–9 (Workplace, Calendar, Contacts-as-Person, the Android widget, real system notifications,
  cross-entity Smart Search, full 10,000-row archive virtualization) are all still open — this list's
  page size is a fixed 100 rows, not true incremental "load older" yet.

Next natural step: either Phase 4 (Smart Search) building on what exists now, or Phase 5
(Workplace/Calendar) which needs its own new entities — ask the user which before starting either.
