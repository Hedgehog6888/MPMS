namespace MPMS.ViewModels;

public interface ILoadable
{
    Task LoadAsync();
    void Invalidate();
}
