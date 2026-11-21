using System.Windows;
using System.Windows.Controls;

namespace MyCustomNote;

/// <summary>
/// A dialog with one or more PIN boxes. The caller gives the labels and a check that gets the typed values and
/// returns an error text (the dialog stays open and shows it) or null (the dialog closes and returns the values).
/// Used for every PIN question: unlock, set, change, remove.
/// </summary>
public partial class PinWindow : Window
{
    private readonly List<PasswordBox> _boxes = [];
    private readonly Func<string[], string?> _validate;

    private PinWindow(string title, string message, string[] labels, Func<string[], string?> validate, string cancelLabel)
    {
        InitializeComponent();
        _validate = validate;
        Title = title;
        MessageText.Text = message;
        CancelButton.Content = cancelLabel;

        foreach (string label in labels)
        {
            Fields.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 6, 0, 2) });
            var box = new PasswordBox { Padding = new Thickness(6, 5, 6, 5) };
            box.PasswordChanged += (_, _) => ErrorText.Text = "";
            _boxes.Add(box);
            Fields.Children.Add(box);
        }

        Loaded += (_, _) => _boxes[0].Focus();
    }

    private string[]? Result { get; set; }

    /// <summary>Shows the dialog. Returns the typed values, or null if the user cancelled (or quit).</summary>
    public static string[]? Ask(
        Window? owner,
        string title,
        string message,
        string[] labels,
        Func<string[], string?> validate,
        string cancelLabel = "Cancel")
    {
        var window = new PinWindow(title, message, labels, validate, cancelLabel)
        {
            Owner = owner,
            ShowInTaskbar = owner is null,
        };
        if (owner is null)
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        return window.ShowDialog() == true ? window.Result : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string[] values = _boxes.Select(b => b.Password).ToArray();
        string? error = _validate(values);
        if (error is not null)
        {
            ErrorText.Text = error;
            _boxes[0].Focus();
            _boxes[0].SelectAll();
            return;
        }

        Result = values;
        DialogResult = true;
    }
}
