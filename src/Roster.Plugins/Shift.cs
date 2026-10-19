namespace Roster.Plugins
{
    /// <summary>
    /// Schema names for the Shift table. Change the "cr_" prefix to match
    /// the publisher prefix in your environment.
    /// </summary>
    public static class Shift
    {
        public const string EntityName = "cr_shift";
        public const string Id = "cr_shiftid";
        public const string Name = "cr_name";
        public const string Start = "cr_start";
        public const string End = "cr_end";
        public const string Volunteer = "cr_volunteer";
        public const string DurationHours = "cr_durationhours";
        public const string ReminderSent = "cr_remindersent";
        public const string StateCode = "statecode";

        public const int ActiveState = 0;
    }
}
