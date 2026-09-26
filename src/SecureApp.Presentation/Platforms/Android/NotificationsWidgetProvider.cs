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

    public override void OnUpdate(Context? context, AppWidgetManager? appWidgetManager, int[]? appWidgetIds)
    {
        if (context is null || appWidgetManager is null || appWidgetIds is null || appWidgetIds.Length == 0) return;
        var pendingResult = GoAsync();
        _ = RefreshAsync(context, appWidgetManager, appWidgetIds, pendingResult);
    }

    private static readonly int[] DayRowIds =
    [
        Resource.Id.widget_day1, Resource.Id.widget_day2, Resource.Id.widget_day3, Resource.Id.widget_day4,
        Resource.Id.widget_day5,
    ];
    private static readonly int[] DayColorIds =
    [
        Resource.Id.widget_day1_color, Resource.Id.widget_day2_color, Resource.Id.widget_day3_color, Resource.Id.widget_day4_color,
        Resource.Id.widget_day5_color,
    ];
    private static readonly int[] DayLabelIds =
    [
        Resource.Id.widget_day1_label, Resource.Id.widget_day2_label, Resource.Id.widget_day3_label, Resource.Id.widget_day4_label,
        Resource.Id.widget_day5_label,
    ];
    private static readonly int[] DayTextIds =
    [
        Resource.Id.widget_day1_text, Resource.Id.widget_day2_text, Resource.Id.widget_day3_text, Resource.Id.widget_day4_text,
        Resource.Id.widget_day5_text,
    ];
    private const int DayCount = 5;
    private static readonly string[] CzechDayAbbreviations = ["Po", "Út", "St", "Čt", "Pá", "So", "Ne"];

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
            var byDate = rangeAssignments.ToDictionary(a => a.Date);

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
