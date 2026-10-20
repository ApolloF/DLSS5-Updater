using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using DLSSUpdater.ViewModels;

namespace DLSSUpdater.Views;

/// <summary>The first-run setup over the main window. Enter goes on, Esc skips; focus stays inside while it is open.</summary>
public partial class OnboardingView : UserControl
{
    public OnboardingView()
    {
        InitializeComponent();
        PreviewKeyDown += OnPreviewKeyDown;
        IsVisibleChanged += (_, _) => FocusNext();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is OnboardingViewModel old) old.PropertyChanged -= OnStepChanged;
            if (e.NewValue is OnboardingViewModel vm) vm.PropertyChanged += OnStepChanged;
        };
    }

    private OnboardingViewModel? Vm => DataContext as OnboardingViewModel;

    /// <summary>A new step may hide the focused control (Back on the first step): focus moves back to the main button.</summary>
    private void OnStepChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OnboardingViewModel.Step)) FocusNext();
    }

    private void FocusNext()
    {
        if (IsVisible) Dispatcher.BeginInvoke(() => NextButton.Focus(), DispatcherPriority.Loaded);
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        if (e.Key == Key.Escape)
        {
            if (vm.IsLast) vm.Finish();
            else vm.SkipCommand.Execute(null);
            e.Handled = true;
        }
        // A focused button (Skip, Back) presses itself; anywhere else Enter means Next.
        else if (e.Key == Key.Enter && Keyboard.FocusedElement is not Button)
        {
            vm.NextCommand.Execute(null);
            e.Handled = true;
        }
    }
}
