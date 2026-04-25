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
            await Services.GetRequiredService<IApiService>().ProbeAsync();
            await OpenMainWindowAsync();
        }
        else
        {
            var loginWindow = Services.GetRequiredService<LoginWindow>();
            loginWindow.Show();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        // ── Local SQLite DB (%LocalAppData%\MPMS\ — не в bin\Debug) ────────────
        services.AddDbContextFactory<LocalDbContext>(options =>
            options.UseSqlite(LocalDbPaths.GetConnectionString()));

        // ── HTTP Client ───────────────────────────────────────────────────────
        // Базовый URL задаётся в appsettings.json и IAuthService.ApiBaseUrl — запросы собирают полный Uri в ApiService.
        services.AddHttpClient("MPMS", client =>
        {
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
        services.AddSingleton<IUserSettingsService, UserSettingsService>();

        // ── Page ViewModels ───────────────────────────────────────────────────
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProjectsViewModel>();
        services.AddSingleton<ProjectDetailViewModel>();
        services.AddSingleton<StageEditViewModel>();
        services.AddTransient<TasksViewModel>();
        services.AddTransient<WarehouseViewModel>();
        services.AddTransient<TaskDetailViewModel>();
        services.AddTransient<StagesViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<GanttViewModel>();
        services.AddTransient<FilesPageViewModel>();

        // ── Windows ───────────────────────────────────────────────────────────
        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();

        // ── Dialogs ───────────────────────────────────────────────────────────
        services.AddTransient(sp => new ConfirmDeleteDialog());
    }

    private static void EnsureLocalDatabase()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();
        LocalSchemaMigrator.Apply(LocalDbPaths.GetConnectionString());
    }

    /// <summary>Сначала полная синхронизация с сервером в SQLite, затем главное окно и навигация — чтобы списки не были пустыми.</summary>
    public static async Task OpenMainWindowAsync()
    {
        await Services.GetRequiredService<ISyncService>().SyncAsync();
        var main = Services.GetRequiredService<MainWindow>();
        Services.GetRequiredService<MainViewModel>().RefreshUserInfo();
        main.Show();
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

