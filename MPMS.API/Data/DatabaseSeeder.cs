using Microsoft.EntityFrameworkCore;
using System.Linq;
using MPMS.API.Models;
using MPMS.API.Services;

namespace MPMS.API.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        await db.Database.MigrateAsync();

        if (!await db.Users.AnyAsync())
        {
            var adminRoleId = await db.Roles
                .Where(r => r.Name == "Administrator")
                .Select(r => r.Id)
                .FirstAsync();

            var admin = CreateSeedUser("Админ", "Администратов", "admin", "admin@mpms.local", "admin123", adminRoleId);
            db.Users.Add(admin);
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
