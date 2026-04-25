using MPMS.API.Models;

namespace MPMS.API.Services;

public interface IJwtService
{
    string GenerateToken(User user);
    string GenerateRefreshToken();
    DateTime GetExpiryTime();
    System.Security.Claims.ClaimsPrincipal? GetPrincipalFromExpiredToken(string token);
}
