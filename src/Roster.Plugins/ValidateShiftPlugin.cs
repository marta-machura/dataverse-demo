using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Roster.Plugins
{
    /// <summary>
    /// Validates a shift and derives its duration before it is saved.
    ///
    /// Register on cr_shift for Create and Update, stage PreOperation (20),
    /// synchronous. For Update, set filtering attributes to cr_start, cr_end,
    /// cr_volunteer and add a pre-image named "PreImage" with those columns.
    /// </summary>
    public sealed class ValidateShiftPlugin : IPlugin
    {
        public const string PreImageName = "PreImage";
        public const decimal MaxShiftHours = 8m;

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            if (!context.InputParameters.TryGetValue("Target", out var targetParam)
                || !(targetParam is Entity target)
                || target.LogicalName != Shift.EntityName)
            {
                return;
            }

            // An update that touches none of the scheduling columns has nothing to validate.
            if (!target.Contains(Shift.Start) && !target.Contains(Shift.End) && !target.Contains(Shift.Volunteer))
            {
                tracing.Trace("No scheduling columns changed; skipping validation.");
                return;
            }

            // On Update the Target only holds changed columns, so fill the rest from the pre-image.
            context.PreEntityImages.TryGetValue(PreImageName, out var preImage);

            var start = GetMerged<DateTime?>(target, preImage, Shift.Start);
            var end = GetMerged<DateTime?>(target, preImage, Shift.End);
            var volunteer = GetMerged<EntityReference>(target, preImage, Shift.Volunteer);

            if (start == null || end == null)
            {
                throw new InvalidPluginExecutionException("A shift needs both a start and an end time.");
            }

            if (end <= start)
            {
                throw new InvalidPluginExecutionException("A shift must end after it starts.");
            }

            var hours = (decimal)(end.Value - start.Value).TotalHours;
            if (hours > MaxShiftHours)
            {
                throw new InvalidPluginExecutionException($"A shift can be at most {MaxShiftHours} hours long.");
            }

            target[Shift.DurationHours] = Math.Round(hours, 2);
            tracing.Trace("Shift duration set to {0} hours.", hours);

            if (volunteer != null)
            {
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);
                EnsureNoOverlap(service, context, target.Id, volunteer.Id, start.Value, end.Value);
            }
        }

        private static void EnsureNoOverlap(
            IOrganizationService service,
            IPluginExecutionContext context,
            Guid shiftId,
            Guid volunteerId,
            DateTime start,
            DateTime end)
        {
            var query = new QueryExpression(Shift.EntityName)
            {
                ColumnSet = new ColumnSet(Shift.Name),
                TopCount = 1,
            };
            query.Criteria.AddCondition(Shift.Volunteer, ConditionOperator.Equal, volunteerId);
            query.Criteria.AddCondition(Shift.Start, ConditionOperator.LessThan, end);
            query.Criteria.AddCondition(Shift.End, ConditionOperator.GreaterThan, start);
            query.Criteria.AddCondition(Shift.StateCode, ConditionOperator.Equal, Shift.ActiveState);

            // A shift being moved will always "overlap" its own saved row.
            if (context.MessageName == "Update")
            {
                query.Criteria.AddCondition(Shift.Id, ConditionOperator.NotEqual, shiftId);
            }

            var clash = service.RetrieveMultiple(query).Entities;
            if (clash.Count > 0)
            {
                var name = clash[0].GetAttributeValue<string>(Shift.Name) ?? "another shift";
                throw new InvalidPluginExecutionException($"This volunteer is already booked for {name} at that time.");
            }
        }

        private static T GetMerged<T>(Entity target, Entity preImage, string attribute)
        {
            if (target.Contains(attribute))
            {
                return target.GetAttributeValue<T>(attribute);
            }

            return preImage != null ? preImage.GetAttributeValue<T>(attribute) : default;
        }
    }
}
