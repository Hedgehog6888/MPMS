using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using MPMS.Data;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views;
using MPMS.Views.Pages;

namespace MPMS;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        var services = new ServiceCollection();
        ConfigureServices(services);
        Services = services.BuildServiceProvider();

        EnsureLocalDatabase();

        var authService = Services.GetRequiredService<IAuthService>();

        if (await authService.TryRestoreSessionAsync())
        {
            // Probe the server before showing the main window so the online/offline
            // indicator is correct from the very first frame (no green→red flash).
            await Services.GetRequiredService<IApiService>().ProbeAsync();
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
        services.AddHttpClient("MPMS", client =>
        {
            client.BaseAddress = new Uri("http://localhost:5147/api/");
            client.Timeout = TimeSpan.FromSeconds(15);
        });
        services.AddSingleton<IApiService>(sp =>
        {
            var factory = sp.GetRequiredService<IHttpClientFactory>();
            var http = factory.CreateClient("MPMS");
            var auth = sp.GetRequiredService<IAuthService>();
            return new ApiService(http, auth);
        });

        // ── Services ──────────────────────────────────────────────────────────
        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<ISyncService, SyncService>();

        // ── Page ViewModels ───────────────────────────────────────────────────
        services.AddTransient<ProjectsViewModel>();
        services.AddSingleton<ProjectDetailViewModel>();
        services.AddTransient<TasksViewModel>();
        services.AddTransient<MaterialsViewModel>();
        services.AddTransient<TaskDetailViewModel>();
        services.AddTransient<StagesViewModel>();
        services.AddTransient<ProfileViewModel>();

        // ── Windows ───────────────────────────────────────────────────────────
        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();

        // ── Dialogs (kept for materials dialog) ──────────────────────────────
        services.AddTransient(sp => new MPMS.Views.Dialogs.CreateMaterialDialog());
    }

    private const string LocalDbConnectionString = "Data Source=mpms_local.db";

    private static void EnsureLocalDatabase()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        LocalSchemaMigrator.Apply(LocalDbConnectionString);
    }

    public static void OpenMainWindow()
    {
        var main = Services.GetRequiredService<MainWindow>();
        // Refresh user info displayed in the sidebar (important after account switch)
        Services.GetRequiredService<MainViewModel>().RefreshUserInfo();
        main.Show();

        // Start background sync
        _ = Services.GetRequiredService<ISyncService>().SyncAsync();
    }

    public static void NavigateToLogin()
    {
        // Show login BEFORE hiding existing windows so the app never has zero visible windows,
        // which would trigger OnLastWindowClose shutdown.
        var loginWindow = Services.GetRequiredService<LoginWindow>();
        loginWindow.Show();

        foreach (Window w in Current.Windows.Cast<Window>().ToList())
        {
            if (w is not Views.LoginWindow)
                w.Hide();
        }
    }
}
