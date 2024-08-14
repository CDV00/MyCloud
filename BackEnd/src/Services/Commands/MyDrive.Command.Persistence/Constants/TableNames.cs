namespace MyDrive.Command.Persistence.Constants;

internal static class TableNames
{
    internal const string Folder = nameof(Folder);
    internal const string File = nameof(File);
    // For Outbox Pattern
    internal const string OutboxMessages = nameof(OutboxMessages);
}
