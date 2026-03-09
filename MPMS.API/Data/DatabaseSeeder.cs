using Microsoft.EntityFrameworkCore;
using MPMS.API.Models;

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

        if (await db.Users.AnyAsync()) return;

        var roles = await db.Roles.ToDictionaryAsync(r => r.Name);

        var users = new[]
        {
            new User
            {
                Name         = "Иван Администратов",
                Username     = "admin",
                Email        = "admin@mpms.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("admin123"),
                RoleId       = roles["Administrator"].Id,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow
            },
            new User
            {
                Name         = "Алексей Менеджеров",
                Username     = "manager",
                Email        = "manager@mpms.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("manager123"),
                RoleId       = roles["Project Manager"].Id,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow
            },
            new User
            {
                Name         = "Сергей Прорабов",
                Username     = "foreman",
                Email        = "foreman@mpms.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("foreman123"),
                RoleId       = roles["Foreman"].Id,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow
            },
            new User
            {
                Name         = "Пётр Работников",
                Username     = "worker",
                Email        = "worker@mpms.local",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword("worker123"),
                RoleId       = roles["Worker"].Id,
                CreatedAt    = DateTime.UtcNow,
                UpdatedAt    = DateTime.UtcNow
            }
        };

        db.Users.AddRange(users);
        await db.SaveChangesAsync();
    }
}
