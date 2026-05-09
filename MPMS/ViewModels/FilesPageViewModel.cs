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
    private bool _isLoaded = false;

    public FilesPageViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IApiService api, IUserSettingsService settings, ISyncService sync)
    {
        FilesControlVM = new FilesControlViewModel(dbFactory, auth, api, settings, sync);
    }

    public async Task LoadAsync()
    {
        if (!_isLoaded)
        {
            FilesControlVM.Initialize(null); // Глобальный режим
            _isLoaded = true;
        }
        await Task.CompletedTask;
    }
}
