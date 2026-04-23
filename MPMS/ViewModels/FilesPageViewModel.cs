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

    public FilesPageViewModel(IDbContextFactory<LocalDbContext> dbFactory, IAuthService auth, IApiService api, IUserSettingsService settings, ISyncService sync)
    {
        FilesControlVM = new FilesControlViewModel(dbFactory, auth, api, settings, sync);
    }

    public async Task LoadAsync()
    {
        FilesControlVM.Initialize(null); // Глобальный режим
        await Task.CompletedTask;
    }
}
