using Microsoft.Azure.Functions.Worker;

namespace Roster.Functions;

public sealed class ShiftReminderFunction(ShiftReminderService service, ILogger<ShiftReminderFunction> logger)
{
    // Schedule comes from app settings; the sample runs at 18:00 UTC (07:00 NZDT).
    [Function("ShiftReminders")]
    public async Task Run([TimerTrigger("%ShiftReminderSchedule%")] TimerInfo timer, CancellationToken cancellationToken)
    {
        var result = await service.SendRemindersAsync(cancellationToken);
        logger.LogInformation(
            "Shift reminders: {Sent} sent, {Failed} failed, {Skipped} skipped (no email).",
            result.Sent, result.Failed, result.SkippedNoEmail);
    }
}
