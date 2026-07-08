namespace GameFlow.App.ViewModels;

public sealed class ControlMappingRequestedEventArgs(ControlMappingDialogViewModel dialogViewModel) : EventArgs
{
    public ControlMappingDialogViewModel DialogViewModel { get; } = dialogViewModel;
}
