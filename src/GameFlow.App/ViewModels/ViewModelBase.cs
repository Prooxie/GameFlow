using CommunityToolkit.Mvvm.ComponentModel;

namespace GameFlow.App.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected ViewModelBase() => UiTextResources.Track(this);

    /// <summary>Refreshes translated properties after the language changes.</summary>
    internal virtual void RefreshResourceText() => OnPropertyChanged(string.Empty);
}
