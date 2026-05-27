using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Services;
using MPMS.Data;
using Microsoft.EntityFrameworkCore;
using CommunityToolkit.Mvvm.ComponentModel;

namespace MPMS.ViewModels;

public partial class FilesPageViewModel : ViewModelBase, ILoadable
{
    public FilesControlViewModel FilesControlVM { get; }
    private bool _isInitialized;

    public FilesPageViewModel(
        IDbContextFactory<LocalDbContext> dbFactory,
        IAuthService auth,
        IApiService api,
        IUserSettingsService settings,
        ISyncService sync,
        IPageUiStateStore uiState,
        SidebarFooterViewModel sidebarFooter)
    {
        FilesControlVM = new FilesControlViewModel(dbFactory, auth, api, settings, sync, uiState, sidebarFooter);
    }

    public Task LoadAsync()
    {
        if (!_isInitialized)
        {
            _isInitialized = true;
            FilesControlVM.Initialize(null);
        }
        else
        {
            // Повторное открытие страницы: данные уже есть в памяти,
            // обновляем их в фоне без показа скелетона.
            _ = FilesControlVM.LoadFilesAsync();
        }
        return Task.CompletedTask;
    }
}
