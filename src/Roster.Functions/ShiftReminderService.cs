using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Roster.Plugins;

namespace Roster.Functions;

public sealed record ReminderRunResult(int Sent, int Failed, int SkippedNoEmail);

/// <summary>
/// Finds active shifts starting in the next 24 hours that haven't had a
/// reminder, notifies the volunteer, then flags the shift so the next run
/// doesn't send it again.
/// </summary>
public sealed class ShiftReminderService(
    IOrganizationServiceAsync2 dataverse,
    IVolunteerNotifier notifier,
    TimeProvider clock,
    ILogger<ShiftReminderService> logger)
{
    public static readonly TimeSpan Window = TimeSpan.FromHours(24);
    private const string VolunteerAlias = "volunteer";

    public async Task<ReminderRunResult> SendRemindersAsync(CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var shifts = await dataverse.RetrieveMultipleAsync(BuildQuery(now), cancellationToken);

        int sent = 0, failed = 0, skipped = 0;
        foreach (var shift in shifts.Entities)
        {
            var reminder = ToReminder(shift);
            if (reminder is null)
            {
                logger.LogWarning("Shift {ShiftId} has no volunteer email; skipping.", shift.Id);
                skipped++;
                continue;
            }

            // One bad shift shouldn't stop everyone else's reminders.
            try
            {
                await notifier.SendAsync(reminder, cancellationToken);
                await MarkSentAsync(shift.Id, cancellationToken);
                sent++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // If sending succeeded but the flag update failed, the next run
                // will resend: at-least-once is preferable to silently missing one.
                logger.LogError(ex, "Reminder for shift {ShiftId} failed.", shift.Id);
                failed++;
            }
        }

        return new ReminderRunResult(sent, failed, skipped);
    }

    private static QueryExpression BuildQuery(DateTime now)
    {
        var query = new QueryExpression(Shift.EntityName)
        {
            ColumnSet = new ColumnSet(Shift.Name, Shift.Start),
        };
        query.Criteria.AddCondition(Shift.StateCode, ConditionOperator.Equal, Shift.ActiveState);
        query.Criteria.AddCondition(Shift.ReminderSent, ConditionOperator.Equal, false);
        query.Criteria.AddCondition(Shift.Start, ConditionOperator.Between, now, now.Add(Window));

        var volunteer = query.AddLink("contact", Shift.Volunteer, "contactid");
        volunteer.EntityAlias = VolunteerAlias;
        volunteer.Columns = new ColumnSet("fullname", "emailaddress1");

        return query;
    }

    private static ShiftReminder? ToReminder(Entity shift)
    {
        var email = Aliased<string>(shift, "emailaddress1");
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        return new ShiftReminder(
            shift.Id,
            shift.GetAttributeValue<string>(Shift.Name) ?? "Your shift",
            shift.GetAttributeValue<DateTime>(Shift.Start),
            Aliased<string>(shift, "fullname") ?? "",
            email);
    }

    private static T? Aliased<T>(Entity entity, string column) =>
        entity.GetAttributeValue<AliasedValue>($"{VolunteerAlias}.{column}")?.Value is T value ? value : default;

    private Task MarkSentAsync(Guid shiftId, CancellationToken cancellationToken) =>
        dataverse.UpdateAsync(
            new Entity(Shift.EntityName, shiftId) { [Shift.ReminderSent] = true },
            cancellationToken);
}
