using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
    
    [ObservableProperty] private ObservableCollection<LocalNote> _notes = [];
    [ObservableProperty] private LocalNote? _selectedNote;
    [ObservableProperty] private bool _isNoteEditing;
    [ObservableProperty] private string _currentNoteXaml = string.Empty;
    [ObservableProperty] private bool _isBackConfirmPopupOpen;

    private string _originalTitle = string.Empty;
    private string _originalXaml = string.Empty;

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

    [RelayCommand]
    private async Task CreateNoteAsync()
    {
        var note = new LocalNote
        {
            UserId = _auth.UserId ?? Guid.Empty,
            Title = "",
            Content = "",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        SelectedNote = note;
        CurrentNoteXaml = "";
        _originalTitle = "";
        _originalXaml = "";
        IsNoteEditing = true;
    }

    [RelayCommand]
    private void SelectNote(LocalNote note)
    {
        SelectedNote = note;
        CurrentNoteXaml = note.Content;
        _originalTitle = note.Title ?? "";
        _originalXaml = note.Content ?? "";
        IsNoteEditing = true;
    }

    [RelayCommand]
    private async Task SaveNoteAsync()
    {
        if (SelectedNote == null) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            
            SelectedNote.Content = CurrentNoteXaml;
            SelectedNote.UpdatedAt = DateTime.UtcNow;
            SelectedNote.UserId = _auth.UserId ?? Guid.Empty;

            if (SelectedNote.Id == Guid.Empty || !await db.Notes.AnyAsync(n => n.Id == SelectedNote.Id))
            {
                if (SelectedNote.Id == Guid.Empty) SelectedNote.Id = Guid.NewGuid();
                db.Notes.Add(SelectedNote);
            }
            else
            {
                db.Notes.Update(SelectedNote);
            }

            await db.SaveChangesAsync();
            
            // Update original values to reflect the saved state
            _originalTitle = SelectedNote.Title ?? "";
            _originalXaml = SelectedNote.Content ?? "";
            
            await LoadNotesAsync();
        }
        catch (Exception ex)
        {
            SetError("Ошибка при сохранении заметки: " + ex.Message);
        }
    }

    [RelayCommand]
    private async Task DeleteNoteAsync(LocalNote note)
    {
        if (note == null) return;

        try
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.Notes.Remove(note);
            await db.SaveChangesAsync();
            
            if (SelectedNote?.Id == note.Id)
            {
                IsNoteEditing = false;
                SelectedNote = null;
            }
            
            await LoadNotesAsync();
        }
        catch (Exception ex)
        {
            SetError("Ошибка при удалении заметки: " + ex.Message);
        }
    }

    [RelayCommand]
    private void BackToList()
    {
        if (HasChanges())
        {
            IsBackConfirmPopupOpen = true;
        }
        else
        {
            CloseEditing();
        }
    }

    [RelayCommand]
    private async Task SaveNoteAndBackAsync()
    {
        await SaveNoteAsync();
        CloseEditing();
    }

    [RelayCommand]
    private void DiscardAndBack()
    {
        CloseEditing();
    }

    private bool HasChanges()
    {
        if (SelectedNote == null) return false;
        // Compare with original values
        return (SelectedNote.Title ?? "") != _originalTitle || (CurrentNoteXaml ?? "") != _originalXaml;
    }

    private void CloseEditing()
    {
        IsNoteEditing = false;
        SelectedNote = null;
        IsBackConfirmPopupOpen = false;
    }

    private async Task LoadNotesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var userId = _auth.UserId;
        
        var notesList = await db.Notes
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.UpdatedAt)
            .ToListAsync();

        Notes = new ObservableCollection<LocalNote>(notesList);
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

            // 5. Notes
            await LoadNotesAsync();

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
