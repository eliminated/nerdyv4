using System.Text.Json.Serialization;

namespace Nerdy.Contracts;

// Enums travel as strings so the wire format stays readable and stable when values are added.

[JsonConverter(typeof(JsonStringEnumConverter<UserRole>))]
public enum UserRole { Student, Teacher, Researcher, Learner }

/// <summary>Why a role was suggested from an email address; the app words the explanation.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<RoleSuggestionSource>))]
public enum RoleSuggestionSource { School, SchoolStaff, Alumni, Personal, Organization }

[JsonConverter(typeof(JsonStringEnumConverter<FocusMode>))]
public enum FocusMode { Focus, Ultra }

[JsonConverter(typeof(JsonStringEnumConverter<FocusOutcome>))]
public enum FocusOutcome
{
    /// <summary>Ran to its planned length.</summary>
    Completed,
    /// <summary>A normal Focus session the user ended before the timer ran out.</summary>
    EndedEarly,
    /// <summary>An Ultra-Focus session left through the emergency exit (reason required).</summary>
    EmergencyExit,
    /// <summary>Nerdy was closed or killed while the session was running; recorded on the next launch.</summary>
    Interrupted
}

[JsonConverter(typeof(JsonStringEnumConverter<ExitReason>))]
public enum ExitReason { Medical, Family, Urgent, Other }

[JsonConverter(typeof(JsonStringEnumConverter<ClassRole>))]
public enum ClassRole { Teacher, Student }
