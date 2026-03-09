using MPMS.API.Models;

namespace MPMS.API.Services;

public interface IJwtService
{
    string GenerateToken(User user);
    DateTime GetExpiryTime();
}
