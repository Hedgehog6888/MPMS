using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using MPMS.Data;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views;

namespace MPMS;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private void OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        EnsureLocalDatabase();

        var authService = Services.GetRequiredService<IAuthService>();

        if (authService.TryRestoreSessionAsync().GetAwaiter().GetResult())
        {
            OpenMainWindow();
        }
        else
        {
            var loginWindow = Services.GetRequiredService<LoginWindow>();
            loginWindow.Show();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Local SQLite DB ───────────────────────────────────────────────────
        services.AddDbContextFactory<LocalDbContext>(options =>
            options.UseSqlite("Data Source=mpms_local.db"));

        // ── HTTP Client ───────────────────────────────────────────────────────
        services.AddHttpClient<IApiService, ApiService>(client =>
        {
            client.BaseAddress = new Uri("http://localhost:5147/api/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });

        // ── Services ──────────────────────────────────────────────────────────
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISyncService, SyncService>();

        // ── Windows ───────────────────────────────────────────────────────────
        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();
        services.AddTransient<MainWindow>();
        services.AddTransient<MainViewModel>();
    }

    private static void EnsureLocalDatabase()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
    }

    public static void OpenMainWindow()
    {
        var main = Services.GetRequiredService<MainWindow>();
        main.Show();

        // Start background sync
        _ = Services.GetRequiredService<ISyncService>().SyncAsync();
    }

    public static void NavigateToLogin()
    {
        foreach (Window w in Current.Windows)
            w.Close();

        var loginWindow = Services.GetRequiredService<LoginWindow>();
        loginWindow.Show();
    }
}
