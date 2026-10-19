using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Roster.Functions;
using Roster.Plugins;

namespace Roster.Functions.Tests;

public class ShiftReminderServiceTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 1, 18, 0, 0, TimeSpan.Zero);

    private readonly IOrganizationServiceAsync2 _dataverse = Substitute.For<IOrganizationServiceAsync2>();
    private readonly IVolunteerNotifier _notifier = Substitute.For<IVolunteerNotifier>();
    private QueryExpression? _query;

    private ShiftReminderService CreateService(params Entity[] shifts)
    {
        _dataverse
            .RetrieveMultipleAsync(Arg.Any<QueryBase>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                _query = call.Arg<QueryBase>() as QueryExpression;
                return new EntityCollection(shifts);
            });

        return new ShiftReminderService(
            _dataverse, _notifier, new FakeTimeProvider(Now), NullLogger<ShiftReminderService>.Instance);
    }

    private static Entity ShiftFor(string? email, string name = "Saturday morning")
    {
        var shift = new Entity(Shift.EntityName, Guid.NewGuid())
        {
            [Shift.Name] = name,
            [Shift.Start] = Now.UtcDateTime.AddHours(15),
            ["volunteer.fullname"] = new AliasedValue("contact", "fullname", "Aroha Smith"),
        };
        if (email is not null)
        {
            shift["volunteer.emailaddress1"] = new AliasedValue("contact", "emailaddress1", email);
        }
        return shift;
    }

    [Fact]
    public async Task Sends_reminder_and_marks_the_shift_as_sent()
    {
        var shift = ShiftFor("aroha@example.com");
        var service = CreateService(shift);

        var result = await service.SendRemindersAsync(CancellationToken.None);

        Assert.Equal(new ReminderRunResult(Sent: 1, Failed: 0, SkippedNoEmail: 0), result);
        await _notifier.Received(1).SendAsync(
            Arg.Is<ShiftReminder>(r => r.ShiftId == shift.Id && r.Email == "aroha@example.com" && r.VolunteerName == "Aroha Smith"),
            Arg.Any<CancellationToken>());
        await _dataverse.Received(1).UpdateAsync(
            Arg.Is<Entity>(e => e.Id == shift.Id && e.GetAttributeValue<bool>(Shift.ReminderSent)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Only_queries_active_unsent_shifts_in_the_next_24_hours()
    {
        await CreateService().SendRemindersAsync(CancellationToken.None);

        var conditions = _query!.Criteria.Conditions.ToDictionary(c => c.AttributeName);
        Assert.Equal(Shift.ActiveState, conditions[Shift.StateCode].Values.Single());
        Assert.Equal(false, conditions[Shift.ReminderSent].Values.Single());

        var window = conditions[Shift.Start];
        Assert.Equal(ConditionOperator.Between, window.Operator);
        Assert.Equal([Now.UtcDateTime, Now.UtcDateTime.AddHours(24)], window.Values.Cast<DateTime>());
    }

    [Fact]
    public async Task Skips_shifts_whose_volunteer_has_no_email()
    {
        var result = await CreateService(ShiftFor(email: null)).SendRemindersAsync(CancellationToken.None);

        Assert.Equal(1, result.SkippedNoEmail);
        await _notifier.DidNotReceiveWithAnyArgs().SendAsync(default!, default);
        await _dataverse.DidNotReceiveWithAnyArgs().UpdateAsync(default!, default);
    }

    [Fact]
    public async Task A_failed_send_is_not_marked_sent_and_does_not_stop_the_others()
    {
        var failing = ShiftFor("broken@example.com", "Failing shift");
        var working = ShiftFor("ok@example.com", "Working shift");
        var service = CreateService(failing, working);
        _notifier
            .SendAsync(Arg.Is<ShiftReminder>(r => r.ShiftId == failing.Id), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("mail server down"));

        var result = await service.SendRemindersAsync(CancellationToken.None);

        Assert.Equal(new ReminderRunResult(Sent: 1, Failed: 1, SkippedNoEmail: 0), result);
        await _dataverse.DidNotReceive().UpdateAsync(
            Arg.Is<Entity>(e => e.Id == failing.Id), Arg.Any<CancellationToken>());
        await _dataverse.Received(1).UpdateAsync(
            Arg.Is<Entity>(e => e.Id == working.Id), Arg.Any<CancellationToken>());
    }
}
