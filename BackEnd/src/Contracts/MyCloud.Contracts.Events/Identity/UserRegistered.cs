namespace MyCloud.Contracts.Events.Identity;

public sealed record UserRegistered(Guid UserId, string Email, string DisplayName) : IntegrationEvent;
