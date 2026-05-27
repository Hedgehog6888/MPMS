using System.Net.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using System.Windows.Threading;
using MPMS.Data;
using MPMS.Services;
using MPMS.Services.Sync;
using MPMS.ViewModels;
using MPMS.Views;
using System.Text.Json;

using MPMS.Views.Pages;

namespace MPMS;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            MessageBox.Show($"Критическая ошибка: {ex?.Message}\n\n{ex?.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            e.SetObserved();
        };
        DispatcherUnhandledException += (s, e) =>
        {
            MessageBox.Show($"Ошибка: {e.Exception.Message}\n\n{e.Exception.StackTrace}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            e.Handled = true;
        };
    }

    private async void OnStartup(object sender, StartupEventArgs e)
    {
        try
        {
            var splash = new SplashWindow();
            splash.Show();

            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            splash.SetLoadingText("Инициализация базы данных...");
            EnsureLocalDatabase();

            splash.SetLoadingText("Проверка авторизации...");
            var authService = Services.GetRequiredService<IAuthService>();

            if (await authService.TryRestoreSessionAsync())
            {
                splash.SetLoadingText("Загрузка данных...");
                await Services.GetRequiredService<IApiService>().ProbeAsync();

                splash.SetLoadingText("Открытие приложения...");
                await OpenMainWindowAsync(splash);
            }
            else
            {
                splash.SetLoadingText("Открытие входа...");
                var loginWindow = Services.GetRequiredService<LoginWindow>();

                // Не закрываем splash до тех пор пока LoginWindow не загрузится полностью
                // Иначе приложение закроется из-за ShutdownMode=OnLastWindowClose
                loginWindow.Loaded += (s, _) => splash.CloseWithFadeOut();

                loginWindow.Show();
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Критическая ошибка при запуске:\n\n{ex.Message}\n\n{ex.StackTrace}",
                "Ошибка запуска", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddDbContextFactory<LocalDbContext>(options =>
            options.UseSqlite(LocalDbPaths.GetConnectionString()));

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

        services.AddSingleton<IAuthService, AuthService>();
        services.AddSingleton<IUserSettingsService, UserSettingsService>();
        services.AddSingleton<IPageUiStateStore, PageUiStateStore>();

        var jsonOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        services.AddSingleton(jsonOptions);
        services.AddSingleton<IEntitySyncer, ProjectSyncer>();
        services.AddSingleton<IEntitySyncer, TaskSyncer>();
        services.AddSingleton<IEntitySyncer, WarehouseSyncer>();
        services.AddSingleton<IEntitySyncer, UserSyncer>();
        services.AddSingleton<IEntitySyncer, FileSyncer>();
        services.AddSingleton<IEntitySyncer, SocialSyncer>();
        services.AddSingleton<ISyncService, SyncCoordinator>();
        services.AddTransient<HomeViewModel>();
        services.AddTransient<ProjectsViewModel>();
        services.AddTransient<ClosedProjectsViewModel>();
        services.AddSingleton<ProjectDetailViewModel>();
        services.AddTransient<StageDetailViewModel>();
        services.AddTransient<FilesControlViewModel>();
        services.AddTransient<TasksViewModel>();
        services.AddTransient<WarehouseViewModel>();
        services.AddTransient<TaskDetailViewModel>();
        services.AddTransient<StagesViewModel>();
        services.AddTransient<ProfileViewModel>();
        services.AddTransient<AdminViewModel>();
        services.AddTransient<CatalogsViewModel>();
        services.AddTransient<CalendarViewModel>();
        services.AddTransient<TimelineViewModel>();
        services.AddTransient<FilesPageViewModel>();
        services.AddTransient<LoginWindow>();
        services.AddTransient<LoginViewModel>();
        services.AddSingleton<MainWindow>();
        services.AddSingleton<MainViewModel>();
        services.AddTransient(sp => new ConfirmDeleteDialog());
    }

    private static void EnsureLocalDatabase()
    {
        using var scope = Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        using var db = factory.CreateDbContext();
        db.Database.EnsureCreated();

        var connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA journal_mode=WAL;";
        command.ExecuteNonQuery();
        command.CommandText = "PRAGMA synchronous=NORMAL;";
        command.ExecuteNonQuery();

        LocalSchemaMigrator.Apply(LocalDbPaths.GetConnectionString());
    }

    public static async Task OpenMainWindowAsync(SplashWindow? splash = null)
    {
        await Services.GetRequiredService<ISyncService>().SyncAsync();
        var main = Services.GetRequiredService<MainWindow>();
        Services.GetRequiredService<MainViewModel>().RefreshUserInfoAndNavigateHome();

        splash?.CloseWithFadeOut();

        main.Show();
    }

    public static void NavigateToLogin()
    {
        var loginWindow = Services.GetRequiredService<LoginWindow>();
        loginWindow.Show();

        foreach (Window w in Current.Windows.Cast<Window>().ToList())
        {
            if (w is not Views.LoginWindow)
                w.Hide();
        }
    }
}

