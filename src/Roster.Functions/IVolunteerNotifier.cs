namespace Roster.Functions;

public sealed record ShiftReminder(Guid ShiftId, string ShiftName, DateTime Start, string VolunteerName, string Email);

public interface IVolunteerNotifier
{
    Task SendAsync(ShiftReminder reminder, CancellationToken cancellationToken);
}

/// <summary>
/// Stand-in notifier that only logs. Swap for Azure Communication Services
/// email or Microsoft Graph sendMail in a real deployment.
/// </summary>
public sealed class LoggingNotifier(ILogger<LoggingNotifier> logger) : IVolunteerNotifier
{
    public Task SendAsync(ShiftReminder reminder, CancellationToken cancellationToken)
    {
        logger.LogInformation(
            "Reminder to {Email}: {Shift} starts at {Start:u}",
            reminder.Email, reminder.ShiftName, reminder.Start);
        return Task.CompletedTask;
    }
}
