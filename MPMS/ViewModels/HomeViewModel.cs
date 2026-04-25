using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using System.Windows.Threading;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;

namespace MPMS.ViewModels;

public partial class HomeViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly IAuthService _auth;

    [ObservableProperty] private int _activeProjectsCount;
    [ObservableProperty] private int _myTasksCount;
    [ObservableProperty] private int _upcomingDeadlinesCount;
    [ObservableProperty] private int _totalFilesCount;

    public ObservableCollection<LocalProject> RecentProjects { get; } = [];
    public ObservableCollection<LocalTask> MyUpcomingTasks { get; } = [];
    [ObservableProperty] private ObservableCollection<LocalActivityLog> _recentActivities = [];

    [ObservableProperty] private string _currentTime = DateTime.Now.ToString("HH:mm:ss");
    public string WelcomeMessage => $"Добрый день, {_auth.UserName?.Split(' ').FirstOrDefault() ?? "пользователь"}!";
    public string CurrentDateText => DateTime.Now.ToString("dd MMMM yyyy, dddd");

    private DispatcherTimer? _clockTimer;

    public HomeViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth)
    {
        _dbFactory = dbFactory;
        _auth = auth;

        SetupClock();
    }

    private void SetupClock()
    {
        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (s, e) => CurrentTime = DateTime.Now.ToString("HH:mm:ss");
        _clockTimer.Start();
    }

    public async Task LoadAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        ClearMessages();

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var userId = _auth.UserId;

            // 1. Stats
            ActiveProjectsCount = await db.Projects.CountAsync(p => p.Status == ProjectStatus.InProgress && !p.IsArchived);
            
            if (userId.HasValue)
            {
                MyTasksCount = await db.Tasks.CountAsync(t => t.AssignedUserId == userId && t.Status != Models.TaskStatus.Completed && !t.IsArchived);
            }

            var tomorrow = DateOnly.FromDateTime(DateTime.Today.AddDays(1));
            UpcomingDeadlinesCount = await db.Tasks.CountAsync(t => t.DueDate <= tomorrow && t.Status != Models.TaskStatus.Completed && !t.IsArchived);
            
            TotalFilesCount = await db.Files.CountAsync();

            // 2. Recent Projects
            var recentProjects = await db.Projects
                .Where(p => !p.IsArchived)
                .OrderByDescending(p => p.UpdatedAt)
                .Take(4)
                .ToListAsync();

            RecentProjects.Clear();
            foreach (var p in recentProjects) RecentProjects.Add(p);

            // 3. My Upcoming Tasks
            if (userId.HasValue)
            {
                var myTasks = await db.Tasks
                    .Where(t => t.AssignedUserId == userId && t.Status != Models.TaskStatus.Completed && !t.IsArchived)
                    .OrderBy(t => t.DueDate)
                    .Take(5)
                    .ToListAsync();
                
                MyUpcomingTasks.Clear();
                foreach (var t in myTasks) MyUpcomingTasks.Add(t);
            }

            // 4. Recent Activities
            var activities = await ActivityFilterService.GetFilteredActivitiesAsync(db, _auth, 100, excludeAuthEvents: true);
            RecentActivities = new ObservableCollection<LocalActivityLog>(activities);


        }
        catch (Exception ex)
        {
            SetError("Ошибка при загрузке данных: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
