using Microsoft.EntityFrameworkCore;
using MPMS.API.Models;
using MPMS.API.Services;

namespace MPMS.API.Data;

public static class DatabaseSeeder
{
    /// <summary>
    /// Seeds 4 test users (one per role) if no users exist yet.
    /// Safe to call on every startup — does nothing if data is already present.
    /// </summary>
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        // Apply pending migrations automatically
        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var roles = await db.Roles.ToDictionaryAsync(r => r.Name);
            var users = new[]
            {
                CreateSeedUser("Иван", "Администратов", "admin", "admin@mpms.local", "admin123", roles["Administrator"].Id),
                CreateSeedUser("Алексей", "Менеджеров", "manager", "manager@mpms.local", "manager123", roles["Project Manager"].Id),
                CreateSeedUser("Сергей", "Прорабов", "foreman", "foreman@mpms.local", "foreman123", roles["Foreman"].Id),
                CreateSeedUser("Пётр", "Работников", "worker", "worker@mpms.local", "worker123", roles["Worker"].Id)
            };
            db.Users.AddRange(users);
            await db.SaveChangesAsync();
            return;
        }

    }

    private static User CreateSeedUser(string firstName, string lastName, string username, string email, string password, Guid roleId)
    {
        var fullName = $"{firstName} {lastName}".Trim();
        return new User
        {
            FirstName = firstName,
            LastName = lastName,
            Username = username,
            Email = email,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(password),
            RoleId = roleId,
            AvatarData = AvatarGenerator.GenerateInitialsAvatar(fullName),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
    }
}
