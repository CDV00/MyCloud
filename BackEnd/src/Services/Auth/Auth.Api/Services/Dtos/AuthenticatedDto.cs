namespace Auth.Api.Services.Dtos;

public class AuthenticatedDto
{
    public string AccessToken { get; set; }
    public string RefreshToken { get; set; }
    public DateTime RefreshTokenExpiryTime { get; set; }
}