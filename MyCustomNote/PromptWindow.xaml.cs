using System.Windows;

namespace MyCustomNote;

/// <summary>A small "type a name" dialog (new folder, rename, new tag).</summary>
public partial class PromptWindow : Window
{
    private PromptWindow(string title, string prompt, string initial)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        Input.Text = initial;
        Loaded += (_, _) =>
        {
            Input.Focus();
            Input.SelectAll();
        };
    }

    private string? Result { get; set; }

    /// <summary>Shows the dialog; returns the trimmed text, or null if the user cancelled or left it empty.</summary>
    public static string? Ask(Window? owner, string title, string prompt, string initial = "")
    {
        var window = new PromptWindow(title, prompt, initial) { Owner = owner };
        return window.ShowDialog() == true ? window.Result : null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        string text = Input.Text.Trim();
        if (text.Length == 0)
        {
            return;
        }

        Result = text;
        DialogResult = true;
    }
}
