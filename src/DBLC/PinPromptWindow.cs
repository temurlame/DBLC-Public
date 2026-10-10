using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;

namespace DBLC;

/// <summary>A small local WPF prompt. It is never shown over a revealed campaign window during unlock.</summary>
public sealed class PinPromptWindow : Window
{
    private readonly PasswordBox _pin = new() { MaxLength = 4 };
    private readonly PasswordBox? _confirm;
    private readonly TextBlock _error = new() { Foreground = new SolidColorBrush(Color.FromRgb(210, 158, 158)), FontSize = 11 };

    private PinPromptWindow(string heading, bool creating)
    {
        Title = "DBLC · Privacy PIN";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        MinHeight = 250;
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 40);
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ShowInTaskbar = true;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 24));
        Foreground = Brushes.Gainsboro;
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = heading, FontFamily = new FontFamily("Georgia"), FontSize = 20, Margin = new Thickness(0, 0, 0, 16) });
        panel.Children.Add(new TextBlock { Text = "Four-digit PIN", FontSize = 11, Foreground = Brushes.DarkGray });
        StyleInput(_pin);
        panel.Children.Add(_pin);
        if (creating)
        {
            _confirm = new PasswordBox { MaxLength = 4 };
            panel.Children.Add(new TextBlock { Text = "Confirm PIN", FontSize = 11, Foreground = Brushes.DarkGray });
            StyleInput(_confirm);
            panel.Children.Add(_confirm);
        }
        panel.Children.Add(_error);
        if (!creating) panel.Children.Add(new TextBlock { Text = "Forgotten PIN? Run DBLC.exe --reset-pin locally.", Foreground = Brushes.Gray, FontSize = 10, Margin = new Thickness(0, 8, 0, 0) });
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        var cancel = new Button { Content = "Cancel", Width = 76, Margin = new Thickness(0, 0, 8, 0) };
        cancel.Click += (_, _) => DialogResult = false;
        var accept = new Button { Content = creating ? "Save PIN" : "Open DBLC", Width = 94, IsDefault = true };
        accept.Click += (_, _) => Submit();
        foreach (var button in new[] { cancel, accept })
        {
            button.Padding = new Thickness(6);
            button.Background = new SolidColorBrush(Color.FromRgb(40, 40, 40));
            button.Foreground = Brushes.Gainsboro;
            button.BorderBrush = new SolidColorBrush(Color.FromRgb(84, 84, 84));
        }
        buttons.Children.Add(cancel);
        buttons.Children.Add(accept);
        panel.Children.Add(buttons);
        Content = panel;
        Loaded += (_, _) => _pin.Focus();
    }

    private static void StyleInput(PasswordBox box)
    {
        box.PreviewTextInput += (_, args) => args.Handled = !args.Text.All(c => c is >= '0' and <= '9');
        System.Windows.DataObject.AddPastingHandler(box, (_, args) =>
        {
            var pasted = args.DataObject.GetData(System.Windows.DataFormats.UnicodeText) as string;
            if (pasted is null || !pasted.All(c => c is >= '0' and <= '9')) args.CancelCommand();
        });
        box.Margin = new Thickness(0, 5, 0, 13);
        box.Padding = new Thickness(8);
        box.Background = new SolidColorBrush(Color.FromRgb(17, 17, 17));
        box.Foreground = Brushes.Gainsboro;
        box.BorderBrush = new SolidColorBrush(Color.FromRgb(70, 70, 70));
    }

    private Func<string, string?, bool>? _accept;

    private void Submit()
    {
        if (_accept?.Invoke(_pin.Password, _confirm?.Password) == true)
        {
            DialogResult = true;
            return;
        }
        _error.Text = _confirm is null ? "Incorrect PIN." : "Enter matching four-digit PINs.";
        _pin.Clear();
        _confirm?.Clear();
        _pin.Focus();
    }

    public static bool Unlock(PrivacyLockStore privacy) =>
        new PinPromptWindow("Open DBLC", false) { _accept = (pin, _) => privacy.Verify(pin) }.ShowDialog() == true;

    public static bool Configure(PrivacyLockStore privacy) =>
        new PinPromptWindow(privacy.HasPin ? "Change privacy PIN" : "Set privacy PIN", true)
        { _accept = (pin, confirmation) => privacy.SetPin(pin, confirmation) }.ShowDialog() == true;
}
