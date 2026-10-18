using System.Windows;
using System.Windows.Controls;

namespace DLSSUpdater.Views;

/// <summary>
/// Builds its <see cref="Template"/> the first time <see cref="Show"/> is true and keeps it afterwards. Hidden pages
/// (settings, about) otherwise cost startup layout and, while any UI Automation client listens, seconds of
/// automation-tree building on the UI thread.
/// </summary>
public sealed class Deferred : Decorator
{
    public static readonly DependencyProperty ShowProperty = DependencyProperty.Register(
        nameof(Show), typeof(bool), typeof(Deferred), new PropertyMetadata(false, (d, _) => ((Deferred)d).Realize()));

    public bool Show
    {
        get => (bool)GetValue(ShowProperty);
        set => SetValue(ShowProperty, value);
    }

    public DataTemplate? Template { get; set; }

    /// <summary>Raised once with the built content, for code-behind that needs named elements inside it.</summary>
    public event Action<FrameworkElement>? Realized;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Realize();
    }

    private void Realize()
    {
        if (!Show || Child is not null || Template is null || !IsInitialized) return;
        var root = (FrameworkElement)Template.LoadContent();
        Child = root;
        Realized?.Invoke(root);
    }

    /// <summary>A named element inside the built content.</summary>
    public static T Find<T>(FrameworkElement root, string name) where T : class =>
        LogicalTreeHelper.FindLogicalNode(root, name) as T
        ?? throw new InvalidOperationException($"{name} not found in deferred content");
}
