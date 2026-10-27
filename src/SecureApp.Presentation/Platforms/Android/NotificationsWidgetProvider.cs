using Android.App;
using Android.Appwidget;
using Android.Content;
using Android.Widget;
using Microsoft.Extensions.DependencyInjection;
using System.Linq;
using SecureApp.Domain.Entities;
using SecureApp.Domain.Enums;
using SecureApp.Domain.Events;
using SecureApp.Domain.Interfaces.Repositories;
using SecureApp.Domain.ValueObjects;
using SecureApp.Presentation.Contacts;
using SecureApp.Presentation.Workplace;

namespace SecureApp.Presentation.Platforms.Android;

/// <summary>
/// Phase 7 home-screen widget (2026-09-22, NOTIFICATION_HUB_SPEC.md §9/§30). The reference image's own
/// three counter pills (design/notification-hub-reference/widget-reference.jpg) were removed
/// (2026-09-23, user's own explicit ask: "pilulky vyhoď a nech jen jeden řádek") in favor of a single
/// one-line "latest notification" ticker at the very top — the most recent non-System notification
/// (same exclusion <c>NotificationsViewModel</c>'s own Important/Unread counts already apply), tapping
/// it opens that notification's own detail exactly like a tapped OS notification already does (reuses
/// <see cref="Notifications.NativeNotificationRouter"/>'s existing "notificationId" extra).
///
/// Below it: a compact 5-day rozpis, today first (2026-09-22, user's own ask), reusing
/// <see cref="IWorkAssignmentRepository.GetByDateRangeAsync"/>/<c>AssignmentTypeCatalog</c>/
/// <c>AssignmentColorCatalog</c> — the exact same data/labels/per-device-customizable colors the
/// in-app Rozpis page itself uses, so the widget can never show a different schedule than the app. An
/// on-call/duty day (2026-09-23, user's own ask: "barevně zvýrazni služby") gets its text in
/// <c>widgetOnCallHighlight</c> instead of the plain muted color. Tapping the card pushes
/// <c>WorkplacePage</c> via a "widgetRoute" extra (<see cref="Notifications.PendingWidgetRouteRouter"/>).
///
/// Two update paths, both converging on <see cref="RefreshAsync"/>:
/// 1. <see cref="OnUpdate"/> — the normal Android widget lifecycle (added to home screen, or the
///    ~30-minute periodic floor Android enforces regardless of this provider's own
///    updatePeriodMillis — see notifications_widget_info.xml's own remarks).
/// 2. <see cref="WidgetRefreshSignal"/> — the real, fast-path trigger: subscribed once from
///    <see cref="MainApplication"/>'s OnCreate, fires immediately whenever a Notification OR a
///    WorkAssignment row changes (see that signal's own remarks for why it's a plain Domain-layer
///    event rather than a platform service), so the widget never visibly lags the in-app pages.
/// </summary>
[BroadcastReceiver(Label = "Oznámení – SecureApp", Exported = true)]
[IntentFilter(["android.appwidget.action.APPWIDGET_UPDATE"])]
[MetaData("android.appwidget.provider", Resource = "@xml/notifications_widget_info")]
public sealed class NotificationsWidgetProvider : AppWidgetProvider
{
    /// <summary>Subscribed once from MainApplication.OnCreate — see this class's own remarks.</summary>
    public static void SubscribeToChanges()
    {
        WidgetRefreshSignal.Changed += RequestUpdateAllWidgets;
    }

    /// <summary>Fire-and-forget, best-effort: a widget refresh failing must never affect the actual notification write that triggered it.</summary>
    private static void RequestUpdateAllWidgets()
    {
        try
        {
            var context = global::Android.App.Application.Context;
            var manager = AppWidgetManager.GetInstance(context);
            var component = new ComponentName(context, Java.Lang.Class.FromType(typeof(NotificationsWidgetProvider)));
            var ids = manager?.GetAppWidgetIds(component);
            if (manager is null || ids is null || ids.Length == 0) return;
            _ = RefreshAsync(context, manager, ids);
        }
        catch
        {
            // Best-effort — see class remarks.
        }
    }

    private const string ActionToggleMode = "com.companyname.secureapp.presentation.WIDGET_TOGGLE_MODE";
    private const string ActionCalendarPrev = "com.companyname.secureapp.presentation.WIDGET_CAL_PREV";
    private const string ActionCalendarNext = "com.companyname.secureapp.presentation.WIDGET_CAL_NEXT";
    private const string ActionCalendarToday = "com.companyname.secureapp.presentation.WIDGET_CAL_TODAY";
    private const string ShowDutyPreferenceKey = "widget_show_duty"; // legacy 2-state switch, still read once so an existing widget keeps its view
    private const string ModePreferenceKey = "widget_mode";
    private const string CalendarOffsetPreferenceKey = "widget_calendar_month_offset";

    // 2026-10-27, user's ask: "další v pořadí bude tedy kalendář, rozpis, služby, kalendář" — the one tap
    // target cycles Kalendář → Rozpis → Služby → Kalendář. Rozpis stays the default for a widget that never toggled.
    private const int ModeCalendar = 0;
    private const int ModeRozpis = 1;
    private const int ModeDuty = 2;
    private const int MaxCalendarMonthOffset = 12;

    private static int GetMode()
    {
        var preferences = Microsoft.Maui.Storage.Preferences.Default;
        if (preferences.ContainsKey(ModePreferenceKey))
            return preferences.Get(ModePreferenceKey, ModeRozpis);
        return preferences.Get(ShowDutyPreferenceKey, false) ? ModeDuty : ModeRozpis;
    }

    private static int GetCalendarOffset() =>
        Math.Clamp(Microsoft.Maui.Storage.Preferences.Default.Get(CalendarOffsetPreferenceKey, 0), -MaxCalendarMonthOffset, MaxCalendarMonthOffset);

    /// <summary>True when <paramref name="action"/> was one of this widget's own in-place taps (state changed, redraw needed).</summary>
    private static bool HandleLocalAction(string? action)
    {
        var preferences = Microsoft.Maui.Storage.Preferences.Default;
        switch (action)
        {
            case ActionToggleMode:
                var next = (GetMode() + 1) % 3;
                preferences.Set(ModePreferenceKey, next);
                preferences.Set(CalendarOffsetPreferenceKey, 0);
                return true;
            case ActionCalendarPrev:
                preferences.Set(CalendarOffsetPreferenceKey, Math.Max(-MaxCalendarMonthOffset, GetCalendarOffset() - 1));
                return true;
            case ActionCalendarNext:
                preferences.Set(CalendarOffsetPreferenceKey, Math.Min(MaxCalendarMonthOffset, GetCalendarOffset() + 1));
                return true;
            case ActionCalendarToday:
                preferences.Set(CalendarOffsetPreferenceKey, 0);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The Kalendář → Rozpis → Služby switch and the calendar's month arrows — flip the stored state and redraw in place, never opening the app.</summary>
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context is null || !HandleLocalAction(intent?.Action))
        {
            base.OnReceive(context, intent);
            return;
        }

        var manager = AppWidgetManager.GetInstance(context);
        var ids = manager?.GetAppWidgetIds(new ComponentName(context, Java.Lang.Class.FromType(typeof(NotificationsWidgetProvider))));
        if (manager is null || ids is null || ids.Length == 0) return;
        _ = RefreshAsync(context, manager, ids, GoAsync());
    }

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null || appWidgetIds.Length == 0) return;
        var pendingResult = GoAsync();
        _ = RefreshAsync(context, appWidgetManager, appWidgetIds, pendingResult);
    }

    // 2026-10-05, user's own ask: 7 days (a full week) instead of 5.
    private static readonly int[] DayRowIds =
    [
        Resource.Id.widget_day1, Resource.Id.widget_day2, Resource.Id.widget_day3, Resource.Id.widget_day4,
        Resource.Id.widget_day5, Resource.Id.widget_day6, Resource.Id.widget_day7,
    ];
    private static readonly int[] DayColorIds =
    [
        Resource.Id.widget_day1_color, Resource.Id.widget_day2_color, Resource.Id.widget_day3_color, Resource.Id.widget_day4_color,
        Resource.Id.widget_day5_color, Resource.Id.widget_day6_color, Resource.Id.widget_day7_color,
    ];
    private static readonly int[] DayLabelIds =
    [
        Resource.Id.widget_day1_label, Resource.Id.widget_day2_label, Resource.Id.widget_day3_label, Resource.Id.widget_day4_label,
        Resource.Id.widget_day5_label, Resource.Id.widget_day6_label, Resource.Id.widget_day7_label,
    ];
    private static readonly int[] DayTextIds =
    [
        Resource.Id.widget_day1_text, Resource.Id.widget_day2_text, Resource.Id.widget_day3_text, Resource.Id.widget_day4_text,
        Resource.Id.widget_day5_text, Resource.Id.widget_day6_text, Resource.Id.widget_day7_text,
    ];
    private const int DayCount = 7;
    private static readonly string[] CzechDayAbbreviations = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];

    // Duty-roster rows (2026-09-29) — see widget_notifications.xml's own remarks on why this is a
    // fixed-row array (RemoteViews has no dynamic-length list without a RemoteViewsService adapter).
    private static readonly int[] DutyRowIds =
    [
        Resource.Id.widget_duty1, Resource.Id.widget_duty2, Resource.Id.widget_duty3, Resource.Id.widget_duty4,
        Resource.Id.widget_duty5, Resource.Id.widget_duty6, Resource.Id.widget_duty7, Resource.Id.widget_duty8,
        Resource.Id.widget_duty9, Resource.Id.widget_duty10,
    ];
    private static readonly int[] DutyTextIds =
    [
        Resource.Id.widget_duty1_text, Resource.Id.widget_duty2_text, Resource.Id.widget_duty3_text, Resource.Id.widget_duty4_text,
        Resource.Id.widget_duty5_text, Resource.Id.widget_duty6_text, Resource.Id.widget_duty7_text, Resource.Id.widget_duty8_text,
        Resource.Id.widget_duty9_text, Resource.Id.widget_duty10_text,
    ];
    // 2026-10-04, user's own ask: the bare 20dp icon was too small/imprecise to reliably tap — these
    // now point at the surrounding widget_duty{N}_phone_tap FrameLayout (widget_notifications.xml),
    // sized to match the row's own height (no visual change) with generous horizontal padding around
    // the same 20dp icon, so the real tap target is the whole cell, not just the icon's own pixels.
    private static readonly int[] DutyPhoneIds =
    [
        Resource.Id.widget_duty1_phone_tap, Resource.Id.widget_duty2_phone_tap, Resource.Id.widget_duty3_phone_tap, Resource.Id.widget_duty4_phone_tap,
        Resource.Id.widget_duty5_phone_tap, Resource.Id.widget_duty6_phone_tap, Resource.Id.widget_duty7_phone_tap, Resource.Id.widget_duty8_phone_tap,
        Resource.Id.widget_duty9_phone_tap, Resource.Id.widget_duty10_phone_tap,
    ];
    private const int MaxDutyRows = 10;

    private static async Task RefreshAsync(Context context, AppWidgetManager manager, int[] widgetIds, PendingResult? pendingResult = null)
    {
        try
        {
            var services = IPlatformApplication.Current?.Services;
            var notificationRepository = services?.GetService<INotificationRepository>();
            var workAssignmentRepository = services?.GetService<IWorkAssignmentRepository>();
            if (notificationRepository is null || workAssignmentRepository is null) return;

            // System notifications (sync results, relay events, ...) excluded — same rule
            // NotificationsViewModel's own Important/Unread counts already apply (see
            // NotificationRepository.GetPagedAsync's own remarks) — informational noise, not the
            // "what's new" content this ticker line is for.
            var latest = (await notificationRepository.GetPagedAsync(NotificationFilter.Default, 20))
                .FirstOrDefault(n => n.Category != NotificationCategory.System);

            // 5 days starting from TODAY, not Monday (2026-09-22, user's own follow-up ask: "dej
            // widgetu tabulku 5 dní a aktuální den nahoru") — row 1 is always today, see ApplyDay's
            // own remarks for the matching highlight.
            var today = DateOnly.FromDateTime(DateTime.Today);
            var rangeAssignments = await workAssignmentRepository.GetByDateRangeAsync(today, today.AddDays(DayCount - 1));
            // A day can hold 2+ rows now (multiple concurrent workplaces) — see WorkplaceViewModel's own
            // remarks on the same ToDictionary crash this used to have.
            var byDate = rangeAssignments.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.OrderBy(a => a.CreatedAtUtc).First());

            var views = new RemoteViews(context.PackageName, Resource.Layout.widget_notifications);

            if (latest is null)
            {
                views.SetTextViewText(Resource.Id.widget_latest_notification, "Žádná nová oznámení");
            }
            else
            {
                views.SetTextViewText(Resource.Id.widget_latest_notification, $"{latest.Title}: {latest.Body}");
                var openNotificationIntent = new Intent(context, typeof(MainActivity));
                openNotificationIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
                openNotificationIntent.PutExtra("notificationId", latest.Id.ToString());
                views.SetOnClickPendingIntent(Resource.Id.widget_latest_notification, PendingIntent.GetActivity(
                    context, latest.Id.GetHashCode(), openNotificationIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
            }

            var openRozpisIntent = new Intent(context, typeof(MainActivity));
            openRozpisIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            openRozpisIntent.PutExtra("widgetRoute", nameof(Views.WorkplacePage));
            views.SetOnClickPendingIntent(Resource.Id.widget_card, PendingIntent.GetActivity(
                context, "widgetRozpis".GetHashCode(), openRozpisIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));

            // 2026-09-29, user's ask: "na widget dej i možnost otevřít kontakty" — ContactsPage IS a
            // real tab (unlike WorkplacePage), so a plain absolute route already lands correctly
            // highlighted; no WorkplaceRoute-style tab-fixup needed on the App.xaml.cs side.
            var openContactsIntent = new Intent(context, typeof(MainActivity));
            openContactsIntent.SetFlags(ActivityFlags.SingleTop | ActivityFlags.ClearTop);
            openContactsIntent.PutExtra("widgetRoute", "//ContactsTab");
            views.SetOnClickPendingIntent(Resource.Id.widget_open_contacts, PendingIntent.GetActivity(
                context, "widgetContacts".GetHashCode(), openContactsIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));

            ApplyMode(context, views, today);
            if (GetMode() == ModeCalendar)
                await ApplyCalendarAsync(context, views, workAssignmentRepository, today);

            for (var i = 0; i < DayCount; i++)
                ApplyDay(context, views, DayRowIds[i], DayColorIds[i], DayLabelIds[i], DayTextIds[i], today.AddDays(i), byDate.GetValueOrDefault(today.AddDays(i)), isToday: i == 0);

            foreach (var id in widgetIds)
                manager.UpdateAppWidget(id, views);
        }
        catch
        {
            // Best-effort — see class remarks; a stale/blank widget is far better than crashing the
            // whole app process the widget shares with.
        }
        finally
        {
            pendingResult?.Finish();
        }
    }

    /// <summary>
    /// Rozpis ⇄ Služby (2026-09-26, user's ask): the card shows either the 5-day rozpis or today's full
    /// duty list ("Pozice: Jméno", from the last Opicentrum sync — see DutyRosterStore). The toggle
    /// label names the other view and fires a broadcast back to this provider, not an app launch.
    /// </summary>
    private static void ApplyMode(Context context, RemoteViews views, DateOnly today)
    {
        var mode = GetMode();
        var showDuty = mode == ModeDuty;

        var toggleIntent = new Intent(context, typeof(NotificationsWidgetProvider));
        toggleIntent.SetAction(ActionToggleMode);
        views.SetOnClickPendingIntent(Resource.Id.widget_mode_toggle, PendingIntent.GetBroadcast(
            context, "widgetToggleMode".GetHashCode(), toggleIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        // The label names the view a tap leads to: Kalendář → Rozpis → Služby → Kalendář.
        views.SetTextViewText(Resource.Id.widget_mode_toggle, mode switch
        {
            ModeCalendar => "Rozpis ›",
            ModeRozpis => "Služby ›",
            _ => "Kalendář ›",
        });

        var rowsVisibility = mode == ModeRozpis ? global::Android.Views.ViewStates.Visible : global::Android.Views.ViewStates.Gone;
        foreach (var rowId in DayRowIds)
            views.SetViewVisibility(rowId, rowsVisibility);
        views.SetViewVisibility(Resource.Id.widget_calendar, mode == ModeCalendar ? global::Android.Views.ViewStates.Visible : global::Android.Views.ViewStates.Gone);
        views.SetViewVisibility(Resource.Id.widget_duty_list, showDuty ? global::Android.Views.ViewStates.Visible : global::Android.Views.ViewStates.Gone);
        if (!showDuty) return;

        // PICC doctor (2026-10-12, user's own ask: "aktualni lekar by byl videt ve wigetu jako
        // posledni jako PICC") — appended as one more duty-list row rather than a new always-visible
        // UI element, so "poslední" (last) is literal and no widget_notifications.xml layout change
        // was needed. Public to every device (PiccRosterStore caches only the doctor, never nurses —
        // see that store's own remarks), and only shown when it's actually for TODAY.
        var entries = DutyRosterStore.Get(today).ToList();
        if (PiccRosterStore.GetTodayDoctorName() is { } piccDoctor && entries.Count < MaxDutyRows)
            entries.Add(new DutyEntry("PICC", piccDoctor));

        if (entries.Count == 0)
        {
            views.SetViewVisibility(DutyRowIds[0], global::Android.Views.ViewStates.Visible);
            views.SetTextViewText(DutyTextIds[0], "Služby na dnešek zatím nejsou načtené — otevřete Rozpis v aplikaci.");
            views.SetViewVisibility(DutyPhoneIds[0], global::Android.Views.ViewStates.Gone);
            for (var j = 1; j < MaxDutyRows; j++)
                views.SetViewVisibility(DutyRowIds[j], global::Android.Views.ViewStates.Gone);
            return;
        }

        // 2026-09-29, user's ask: "ke sloužícím přidej pictogram a na ktery když se klikne skopiruje
        // služební tel do tel k vytočení" — a phone icon next to whoever resolves to a dialable number
        // in ContactPhoneCache's offline snapshot of the shared contact directory (fuzzy name match,
        // same portal-name-mismatch handling OpicentrumParsing already solves). Intent.ActionDial always
        // just opens the dialer pre-filled — it can never place the call itself — same "never auto-dial"
        // rule ContactsViewModel.CallAsync already follows in-app.
        for (var i = 0; i < MaxDutyRows; i++)
        {
            if (i >= entries.Count)
            {
                views.SetViewVisibility(DutyRowIds[i], global::Android.Views.ViewStates.Gone);
                continue;
            }

            var entry = entries[i];
            views.SetViewVisibility(DutyRowIds[i], global::Android.Views.ViewStates.Visible);
            views.SetTextViewText(DutyTextIds[i], $"{entry.Position}: {entry.Name}");

            var dialNumber = ContactPhoneCache.TryFindPhone(entry.Name);
            if (dialNumber is null)
            {
                views.SetViewVisibility(DutyPhoneIds[i], global::Android.Views.ViewStates.Gone);
                continue;
            }

            views.SetViewVisibility(DutyPhoneIds[i], global::Android.Views.ViewStates.Visible);
            var dialIntent = new Intent(Intent.ActionDial, global::Android.Net.Uri.Parse("tel:" + dialNumber));
            views.SetOnClickPendingIntent(DutyPhoneIds[i], PendingIntent.GetActivity(
                context, ("widgetDial" + i).GetHashCode(), dialIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable));
        }
    }

    // Month calendar (2026-10-27, user's ask: "měsíční kalendář, s čísly dnů a barevně odlišené služby,
    // dovolené, služební cesty, svátky"). A fixed Monday-first 6×7 grid for the same reason as the rows
    // above (no dynamic list in RemoteViews): each day is an inner TextView (number + fill colour) inside
    // an outer FrameLayout whose own background is the white "today" ring showing around the inner fill.
    private const int CalendarCells = 42;
    private static readonly int[] CalendarCellIds =
    [
        Resource.Id.widget_cal_c0, Resource.Id.widget_cal_c1, Resource.Id.widget_cal_c2, Resource.Id.widget_cal_c3, Resource.Id.widget_cal_c4, Resource.Id.widget_cal_c5, Resource.Id.widget_cal_c6,
        Resource.Id.widget_cal_c7, Resource.Id.widget_cal_c8, Resource.Id.widget_cal_c9, Resource.Id.widget_cal_c10, Resource.Id.widget_cal_c11, Resource.Id.widget_cal_c12, Resource.Id.widget_cal_c13,
        Resource.Id.widget_cal_c14, Resource.Id.widget_cal_c15, Resource.Id.widget_cal_c16, Resource.Id.widget_cal_c17, Resource.Id.widget_cal_c18, Resource.Id.widget_cal_c19, Resource.Id.widget_cal_c20,
        Resource.Id.widget_cal_c21, Resource.Id.widget_cal_c22, Resource.Id.widget_cal_c23, Resource.Id.widget_cal_c24, Resource.Id.widget_cal_c25, Resource.Id.widget_cal_c26, Resource.Id.widget_cal_c27,
        Resource.Id.widget_cal_c28, Resource.Id.widget_cal_c29, Resource.Id.widget_cal_c30, Resource.Id.widget_cal_c31, Resource.Id.widget_cal_c32, Resource.Id.widget_cal_c33, Resource.Id.widget_cal_c34,
        Resource.Id.widget_cal_c35, Resource.Id.widget_cal_c36, Resource.Id.widget_cal_c37, Resource.Id.widget_cal_c38, Resource.Id.widget_cal_c39, Resource.Id.widget_cal_c40, Resource.Id.widget_cal_c41,
    ];
    private static readonly int[] CalendarDayIds =
    [
        Resource.Id.widget_cal_d0, Resource.Id.widget_cal_d1, Resource.Id.widget_cal_d2, Resource.Id.widget_cal_d3, Resource.Id.widget_cal_d4, Resource.Id.widget_cal_d5, Resource.Id.widget_cal_d6,
        Resource.Id.widget_cal_d7, Resource.Id.widget_cal_d8, Resource.Id.widget_cal_d9, Resource.Id.widget_cal_d10, Resource.Id.widget_cal_d11, Resource.Id.widget_cal_d12, Resource.Id.widget_cal_d13,
        Resource.Id.widget_cal_d14, Resource.Id.widget_cal_d15, Resource.Id.widget_cal_d16, Resource.Id.widget_cal_d17, Resource.Id.widget_cal_d18, Resource.Id.widget_cal_d19, Resource.Id.widget_cal_d20,
        Resource.Id.widget_cal_d21, Resource.Id.widget_cal_d22, Resource.Id.widget_cal_d23, Resource.Id.widget_cal_d24, Resource.Id.widget_cal_d25, Resource.Id.widget_cal_d26, Resource.Id.widget_cal_d27,
        Resource.Id.widget_cal_d28, Resource.Id.widget_cal_d29, Resource.Id.widget_cal_d30, Resource.Id.widget_cal_d31, Resource.Id.widget_cal_d32, Resource.Id.widget_cal_d33, Resource.Id.widget_cal_d34,
        Resource.Id.widget_cal_d35, Resource.Id.widget_cal_d36, Resource.Id.widget_cal_d37, Resource.Id.widget_cal_d38, Resource.Id.widget_cal_d39, Resource.Id.widget_cal_d40, Resource.Id.widget_cal_d41,
    ];
    private static readonly int[] CalendarRowIds =
    [
        Resource.Id.widget_cal_row0, Resource.Id.widget_cal_row1, Resource.Id.widget_cal_row2,
        Resource.Id.widget_cal_row3, Resource.Id.widget_cal_row4, Resource.Id.widget_cal_row5,
    ];
    private static readonly string[] CzechMonthNames =
        ["Leden", "Únor", "Březen", "Duben", "Květen", "Červen", "Červenec", "Srpen", "Září", "Říjen", "Listopad", "Prosinec"];

    // Fixed calendar palette (2026-10-27, user's own pick: "služba červená, víkendy šedé, dovolená zelená,
    // SC žlutá") — deliberately NOT the per-device Nastavení colours, which are tuned for the app's own
    // light/dark surfaces and read poorly on the widget's dark translucent card.
    private const int DutyFill = unchecked((int)0xFFD32F2F);
    private const int VacationFill = unchecked((int)0xFF2E9E5B);
    private const int TripFill = unchecked((int)0xFFFBC02D);
    private const int WeekendFill = unchecked((int)0xFF616161);
    private const int DayOffFill = unchecked((int)0xFF4A90C2);
    private const int SickFill = unchecked((int)0xFFEF6C00);
    private const int HolidayFill = unchecked((int)0xFF8E24AA);
    private const int WhiteText = unchecked((int)0xFFFFFFFF);
    private const int DarkText = unchecked((int)0xFF202020); // on the yellow trip fill
    private const int OtherMonthText = unchecked((int)0x66FFFFFF);
    private const int TodayRing = unchecked((int)0xFFFFFFFF);
    private const int TodayEmptyInner = unchecked((int)0xFF2A2F33);

    private static async Task ApplyCalendarAsync(Context context, RemoteViews views, IWorkAssignmentRepository repository, DateOnly today)
    {
        var shown = today.AddMonths(GetCalendarOffset());
        var firstOfMonth = new DateOnly(shown.Year, shown.Month, 1);
        var lastOfMonth = firstOfMonth.AddMonths(1).AddDays(-1);
        var gridStart = firstOfMonth.AddDays(-(((int)firstOfMonth.DayOfWeek + 6) % 7));

        var assignments = await repository.GetByDateRangeAsync(gridStart, gridStart.AddDays(CalendarCells - 1));
        // Several rows can share a day — show the one that matters most (a duty beats a plain work row).
        var byDate = assignments.GroupBy(a => a.Date).ToDictionary(g => g.Key, g => g.OrderBy(CalendarRank).ThenBy(a => a.CreatedAtUtc).First());

        views.SetTextViewText(Resource.Id.widget_cal_title, $"{CzechMonthNames[shown.Month - 1]} {shown.Year}");
        views.SetOnClickPendingIntent(Resource.Id.widget_cal_prev, CalendarBroadcast(context, ActionCalendarPrev));
        views.SetOnClickPendingIntent(Resource.Id.widget_cal_next, CalendarBroadcast(context, ActionCalendarNext));
        views.SetOnClickPendingIntent(Resource.Id.widget_cal_title, CalendarBroadcast(context, ActionCalendarToday));

        views.SetInt(Resource.Id.widget_cal_legend_duty, "setTextColor", DutyFill);
        views.SetInt(Resource.Id.widget_cal_legend_vacation, "setTextColor", VacationFill);
        views.SetInt(Resource.Id.widget_cal_legend_trip, "setTextColor", TripFill);
        views.SetInt(Resource.Id.widget_cal_legend_off, "setTextColor", DayOffFill);
        views.SetInt(Resource.Id.widget_cal_legend_holiday, "setTextColor", HolidayFill);

        // A month needs 4–6 rows; the unused trailing ones are hidden to give the widget its height back.
        var rowsNeeded = (lastOfMonth.DayNumber - gridStart.DayNumber) / 7 + 1;
        for (var row = 0; row < CalendarRowIds.Length; row++)
            views.SetViewVisibility(CalendarRowIds[row], row < rowsNeeded ? global::Android.Views.ViewStates.Visible : global::Android.Views.ViewStates.Gone);

        for (var i = 0; i < CalendarCells; i++)
        {
            var date = gridStart.AddDays(i);
            var inMonth = date.Month == shown.Month;
            var isToday = date == today;
            var fill = CalendarFill(byDate.GetValueOrDefault(date), date);
            if (fill is int solid && !inMonth)
                fill = (solid & 0x00FFFFFF) | 0x55000000;

            var textColor = !inMonth ? OtherMonthText : fill == TripFill ? DarkText : WhiteText;

            views.SetTextViewText(CalendarDayIds[i], date.Day.ToString());
            views.SetInt(CalendarDayIds[i], "setTextColor", textColor);
            views.SetInt(CalendarDayIds[i], "setBackgroundColor", fill ?? (isToday ? TodayEmptyInner : 0));
            views.SetInt(CalendarCellIds[i], "setBackgroundColor", isToday ? TodayRing : 0);
        }
    }

    private static PendingIntent? CalendarBroadcast(Context context, string action)
    {
        var intent = new Intent(context, typeof(NotificationsWidgetProvider));
        intent.SetAction(action);
        return PendingIntent.GetBroadcast(context, action.GetHashCode(), intent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
    }

    /// <summary>Ordinary work days stay unfilled so the special days stand out. Priority: the day's own entry (duty, leave, trip …), then a public holiday, then the weekend grey.</summary>
    private static int? CalendarFill(WorkAssignment? assignment, DateOnly date)
    {
        if (assignment is not null)
        {
            var isDuty = assignment.Type == AssignmentType.OnCall || !string.IsNullOrEmpty(assignment.OnCallWorkplaceName);
            var type = isDuty ? AssignmentType.OnCall : assignment.Type;
            switch (type)
            {
                case AssignmentType.OnCall: return DutyFill;
                case AssignmentType.Vacation: return VacationFill;
                case AssignmentType.BusinessTrip:
                case AssignmentType.Training: return TripFill;
                case AssignmentType.SickLeave: return SickFill;
                case AssignmentType.DayOff:
                case AssignmentType.Other: return DayOffFill;
            }
        }

        if (CzechCalendar.HolidayName(date) is not null) return HolidayFill;
        return CzechCalendar.IsWeekend(date) ? WeekendFill : null;
    }

    private static int CalendarRank(WorkAssignment assignment)
    {
        if (assignment.Type == AssignmentType.OnCall || !string.IsNullOrEmpty(assignment.OnCallWorkplaceName))
            return 0;
        return assignment.Type switch
        {
            AssignmentType.BusinessTrip or AssignmentType.Training => 1,
            AssignmentType.Vacation => 2,
            AssignmentType.SickLeave => 3,
            AssignmentType.DayOff => 4,
            AssignmentType.Other => 5,
            _ => 6,
        };
    }

    private static void ApplyDay(Context context, RemoteViews views, int rowId, int colorId, int labelId, int textId, DateOnly date, WorkAssignment? assignment, bool isToday)
    {
        // Highlight (2026-09-22, user's own ask: "aktuální den nahoru a zvýraznit a taky nějaké
        // tmavší pozadí") — row 1 is always today (see RefreshAsync's own remarks), so this only ever
        // fires once; every other row keeps the layout XML's own plain/transparent background.
        if (isToday)
            views.SetInt(rowId, "setBackgroundResource", Resource.Drawable.widget_row_today_background);

        var dayAbbreviation = isToday ? "Dnes" : CzechDayAbbreviations[(int)date.DayOfWeek == 0 ? 6 : (int)date.DayOfWeek - 1];
        views.SetTextViewText(labelId, $"{dayAbbreviation} {date.Day}.{date.Month}.");

        // "Víkend" / a holiday's name (2026-09-26, same rule as the in-app Rozpis — see
        // AssignmentDayItem.DisplayTypeLabel): "Bez záznamu" only for an ordinary day with nothing yet.
        var dayKind = CzechCalendar.DayKindLabel(date);

        if (assignment is null)
        {
            views.SetTextViewText(textId, dayKind.Length > 0 ? dayKind : "Bez záznamu");
            views.SetInt(colorId, "setBackgroundColor", ToAndroidColorInt(AssignmentColorCatalog.NoAssignmentSurfaceColor));
            return;
        }

        // WorkplaceName wins whenever it's set, regardless of type — same free-text-label convention
        // OpicentrumSyncService.MergeSpravavolnaAsync already relies on for AssignmentType.Other's own
        // descriptive text, now also used for the raw VV/NV/PS leave code on a DayOff day (2026-09-22,
        // user's own ask: "pokud je na webu PS dej PS ne volno" — the actual code, not the generic
        // "Volno" label every DayOff day would otherwise show identically).
        var text = !string.IsNullOrEmpty(assignment.WorkplaceName)
            ? assignment.WorkplaceName
            : AssignmentTypeCatalog.Label(assignment.Type);

        var isOnCallDuty = assignment.Type == AssignmentType.OnCall || !string.IsNullOrEmpty(assignment.OnCallWorkplaceName);
        if (dayKind.Length > 0 && assignment.Type == AssignmentType.OnCall && string.IsNullOrEmpty(assignment.OnCallWorkplaceName))
            text = $"{dayKind} + {AssignmentTypeCatalog.Label(AssignmentType.OnCall)}: {text}";
        else if (dayKind.Length > 0)
            text = $"{dayKind} · {text}";
        if (!string.IsNullOrEmpty(assignment.OnCallWorkplaceName))
            text += $" + {AssignmentTypeCatalog.Label(AssignmentType.OnCall)}: {assignment.OnCallWorkplaceName}";

        views.SetTextViewText(textId, text);
        // 2026-09-23, user's own ask: "barevně zvýrazni služby" — an on-call/duty day's own text (not
        // just its color square) gets a more vivid, distinct color instead of the plain muted one
        // every other day uses. Read from colors.xml via ContextCompat rather than duplicating the
        // hex values here, so there's exactly one place either ever needs updating.
        views.SetInt(textId, "setTextColor", AndroidX.Core.Content.ContextCompat.GetColor(context, isOnCallDuty
            ? Resource.Color.widgetOnCallHighlight
            : Resource.Color.widgetCardTextMuted));
        views.SetInt(colorId, "setBackgroundColor", ToAndroidColorInt(AssignmentColorCatalog.BaseColor(assignment.Type)));
    }

    /// <summary>Packs a MAUI <see cref="Microsoft.Maui.Graphics.Color"/> (0–1 float channels) into the plain ARGB int Android's own View.setBackgroundColor(int) expects — RemoteViews.SetInt only forwards a single int argument, so this can't go through MAUI's own Android color-conversion helpers (those work on real Views, not RemoteViews' reflection-based method calls).</summary>
    private static int ToAndroidColorInt(Microsoft.Maui.Graphics.Color color)
    {
        var a = (int)(color.Alpha * 255);
        var r = (int)(color.Red * 255);
        var g = (int)(color.Green * 255);
        var b = (int)(color.Blue * 255);
        return (a << 24) | (r << 16) | (g << 8) | b;
    }
}
