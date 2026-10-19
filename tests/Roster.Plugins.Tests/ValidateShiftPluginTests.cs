using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using NSubstitute;
using Xunit;

namespace Roster.Plugins.Tests
{
    public class ValidateShiftPluginTests
    {
        private static readonly DateTime Nine = new DateTime(2026, 11, 2, 9, 0, 0, DateTimeKind.Utc);
        private static readonly EntityReference Volunteer = new EntityReference("contact", Guid.NewGuid());

        private static Entity NewShift(DateTime? start, DateTime? end, EntityReference volunteer = null)
        {
            var shift = new Entity(Shift.EntityName, Guid.NewGuid());
            if (start.HasValue) shift[Shift.Start] = start.Value;
            if (end.HasValue) shift[Shift.End] = end.Value;
            if (volunteer != null) shift[Shift.Volunteer] = volunteer;
            return shift;
        }

        [Fact]
        public void Create_sets_duration_in_hours()
        {
            var target = NewShift(Nine, Nine.AddHours(3.5));

            new PluginHarness("Create", target).Run();

            Assert.Equal(3.5m, target.GetAttributeValue<decimal>(Shift.DurationHours));
        }

        [Fact]
        public void Rejects_shift_that_ends_before_it_starts()
        {
            var harness = new PluginHarness("Create", NewShift(Nine, Nine.AddHours(-1)));

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => harness.Run());
            Assert.Contains("end after it starts", ex.Message);
        }

        [Fact]
        public void Rejects_shift_longer_than_the_maximum()
        {
            var harness = new PluginHarness("Create", NewShift(Nine, Nine.AddHours(9)));

            Assert.Throws<InvalidPluginExecutionException>(() => harness.Run());
        }

        [Fact]
        public void Rejects_shift_without_an_end()
        {
            var harness = new PluginHarness("Create", NewShift(Nine, null));

            Assert.Throws<InvalidPluginExecutionException>(() => harness.Run());
        }

        [Fact]
        public void Rejects_shift_that_overlaps_another_for_the_same_volunteer()
        {
            var harness = new PluginHarness("Create", NewShift(Nine, Nine.AddHours(2), Volunteer));
            var existing = new Entity(Shift.EntityName, Guid.NewGuid()) { [Shift.Name] = "Saturday morning" };
            harness.ExistingShifts(existing);

            var ex = Assert.Throws<InvalidPluginExecutionException>(() => harness.Run());
            Assert.Contains("Saturday morning", ex.Message);
        }

        [Fact]
        public void Skips_overlap_check_when_no_volunteer_is_assigned()
        {
            var harness = new PluginHarness("Create", NewShift(Nine, Nine.AddHours(2)));

            harness.Run();

            harness.Service.DidNotReceiveWithAnyArgs().RetrieveMultiple(default);
        }

        [Fact]
        public void Update_fills_unchanged_columns_from_the_pre_image()
        {
            var id = Guid.NewGuid();
            var target = new Entity(Shift.EntityName, id) { [Shift.End] = Nine.AddHours(4) };
            var preImage = new Entity(Shift.EntityName, id) { [Shift.Start] = Nine, [Shift.End] = Nine.AddHours(2) };

            new PluginHarness("Update", target, preImage).Run();

            Assert.Equal(4m, target.GetAttributeValue<decimal>(Shift.DurationHours));
        }

        [Fact]
        public void Update_excludes_the_shift_itself_from_the_overlap_check()
        {
            var id = Guid.NewGuid();
            var target = new Entity(Shift.EntityName, id) { [Shift.Start] = Nine.AddHours(1) };
            var preImage = new Entity(Shift.EntityName, id)
            {
                [Shift.Start] = Nine,
                [Shift.End] = Nine.AddHours(3),
                [Shift.Volunteer] = Volunteer,
            };
            var harness = new PluginHarness("Update", target, preImage);

            harness.Run();

            var selfExclusion = harness.LastQuery.Criteria.Conditions
                .Single(c => c.AttributeName == Shift.Id);
            Assert.Equal(ConditionOperator.NotEqual, selfExclusion.Operator);
            Assert.Equal(id, selfExclusion.Values.Single());
        }

        [Fact]
        public void Update_of_unrelated_columns_does_nothing()
        {
            var target = new Entity(Shift.EntityName, Guid.NewGuid()) { [Shift.Name] = "Renamed" };
            var harness = new PluginHarness("Update", target);

            harness.Run();

            Assert.False(target.Contains(Shift.DurationHours));
            harness.Service.DidNotReceiveWithAnyArgs().RetrieveMultiple(default);
        }
    }
}
