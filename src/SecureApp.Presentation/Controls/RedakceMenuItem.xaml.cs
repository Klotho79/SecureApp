using System.Windows.Input;

namespace SecureApp.Presentation.Controls;

/// <summary>One row of the left-hand menu of the PC Redakce layout — see RedakceMenuItem.xaml.</summary>
public partial class RedakceMenuItem : ContentView
{
    public static readonly BindableProperty IconProperty = BindableProperty.Create(nameof(Icon), typeof(string), typeof(RedakceMenuItem), string.Empty);
    public static readonly BindableProperty TitleProperty = BindableProperty.Create(nameof(Title), typeof(string), typeof(RedakceMenuItem), string.Empty);
    public static readonly BindableProperty IsSelectedProperty = BindableProperty.Create(nameof(IsSelected), typeof(bool), typeof(RedakceMenuItem), false);
    public static readonly BindableProperty OpensWindowProperty = BindableProperty.Create(nameof(OpensWindow), typeof(bool), typeof(RedakceMenuItem), false);
    public static readonly BindableProperty CommandProperty = BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(RedakceMenuItem), null);
    public static readonly BindableProperty CommandParameterProperty = BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(RedakceMenuItem), null);

    public RedakceMenuItem()
    {
        InitializeComponent();
    }

    public string Icon { get => (string)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public string Title { get => (string)GetValue(TitleProperty); set => SetValue(TitleProperty, value); }
    public bool IsSelected { get => (bool)GetValue(IsSelectedProperty); set => SetValue(IsSelectedProperty, value); }

    /// <summary>True for items that open a page of their own instead of filling the right-hand pane (shows a small ↗).</summary>
    public bool OpensWindow { get => (bool)GetValue(OpensWindowProperty); set => SetValue(OpensWindowProperty, value); }
    public ICommand? Command { get => (ICommand?)GetValue(CommandProperty); set => SetValue(CommandProperty, value); }
    public object? CommandParameter { get => GetValue(CommandParameterProperty); set => SetValue(CommandParameterProperty, value); }
}
