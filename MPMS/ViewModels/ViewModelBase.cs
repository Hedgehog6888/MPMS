using CommunityToolkit.Mvvm.ComponentModel;

namespace MPMS.ViewModels;

public abstract partial class ViewModelBase : ObservableObject
{
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _errorMessage = string.Empty;
    [ObservableProperty] private string _statusMessage = string.Empty;

    protected void SetError(string message)
    {
        ErrorMessage = message;
        StatusMessage = string.Empty;
    }

    protected void SetStatus(string message)
    {
        StatusMessage = message;
        ErrorMessage = string.Empty;
    }

    protected void ClearMessages()
    {
        ErrorMessage = string.Empty;
        StatusMessage = string.Empty;
    }
}
