namespace Nerdy.Api.Data;

/// <summary>
/// Input limits shared by request validation and column sizes. Text lengths are counted in UTF-16 characters
/// (<see cref="string.Length"/>), which is what the desktop app can check cheaply too.
/// </summary>
public static class FieldLimits
{
    /// <summary>Titles, names and subtitles of every entity.</summary>
    public const int Name = 200;
    public const int DisplayName = 100;
    public const int Email = 256;
    public const int Password = 128;
    public const int ColorHex = 7;

    public const int ReminderNotes = 10_000;
    public const int FocusExitNote = 2_000;
    public const int NoteBodyRtf = 2 * 1024 * 1024;
    public const int NoteBodyText = 200 * 1024;
    public const int ChecklistItems = 200;
    public const int ChecklistItemText = 1_000;
    public const int PreferencesJson = 64 * 1024;
    public const int AssignmentDescription = 10_000;

    /// <summary>Maximum rows of each entity type in one sync request.</summary>
    public const int SyncItemsPerType = 2_000;

    /// <summary>Hashes are SHA-256 encoded as unpadded base64url (43 characters).</summary>
    public const int Hash = 64;
    public const int RedirectUri = 2_048;
}
