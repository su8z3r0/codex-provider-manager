using Avalonia.Controls;
using Avalonia.Interactivity;
using CodexProvider.Core;
using System;
using System.Text.RegularExpressions;

namespace CodexProvider.UI;

public partial class AddProviderForm : Window
{
    public string ProviderId => TxtId.Text?.Trim();
    public string ProviderName => TxtName.Text?.Trim();
    public string ProviderUrl => TxtUrl.Text?.Trim();
    public string ProviderEnvKey => TxtEnvKey.Text?.Trim();
    public string ProviderWire => (CmbWire.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "responses";
    public string ProviderModel => TxtModel.Text?.Trim();

    public bool EditedExisting { get; private set; }

    public AddProviderForm() { InitializeComponent(); }

    public AddProviderForm(ProviderInfo existing) : this()
    {
        EditedExisting = true;
        Title = "Edit provider";
        TxtId.Text = existing.Id;
        TxtId.IsEnabled = false; // the ID is the TOML section key: renaming = remove + re-add
        TxtName.Text = string.IsNullOrEmpty(existing.Name) ? existing.Id : existing.Name;
        TxtUrl.Text = existing.BaseUrl ?? "https://";
        TxtEnvKey.Text = existing.EnvKey ?? "";
        CodexConfig.DefaultModels.TryGetValue(existing.Id, out var dm);
        TxtModel.Text = dm ?? "";
    }

    void OnSave(object sender, RoutedEventArgs e)
    {
        string id = ProviderId ?? "";
        if (id.Length == 0 || !Regex.IsMatch(id, "^[A-Za-z0-9_-]+$"))
        { ShowError("Invalid ID: use letters, numbers, - and _ only."); return; }
        if (!EditedExisting && CodexConfig.Find(id) != null)
        { ShowError("Provider '" + id + "' already exists."); return; }
        string url = ProviderUrl ?? "";
        if (!url.StartsWith("http://") && !url.StartsWith("https://"))
        { ShowError("Invalid Base URL (must start with http:// or https://)."); return; }

        string name = string.IsNullOrEmpty(ProviderName) ? id : ProviderName;
        string envKey = ProviderEnvKey ?? "";
        string wire = ProviderWire ?? "responses";

        try
        {
            if (EditedExisting)
                CodexConfig.UpdateProvider(id, name, url, envKey, wire);
            else
                CodexConfig.AddProvider(id, name, url, envKey, wire);
        }
        catch (Exception ex)
        {
            ShowError("Failed to write the provider into config.toml:\n" + ex.Message);
            return;
        }

        try
        {
            CodexConfig.SaveDefaultModel(id, ProviderModel);
            string pasted = TxtApiKey.Text;
            if (!string.IsNullOrEmpty(envKey) && !string.IsNullOrEmpty(pasted))
                CodexConfig.SetApiKey(envKey, pasted);
        }
        catch (Exception ex)
        {
            // the provider IS written; surface the secondary error but close anyway
            ShowError("Provider saved, but a secondary step failed:\n" + ex.Message);
        }

        Close(true);
    }

    async void ShowError(string msg)
    {
        var dlg = new Window
        {
            Title = "Invalid input",
            Width = 340, Height = 140,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = Avalonia.Media.Brush.Parse("#1E1E29"),
            Content = new StackPanel
            {
                Margin = new Avalonia.Thickness(16),
                Spacing = 12,
                Children =
                {
                    new TextBlock { Text = msg, TextWrapping = Avalonia.Media.TextWrapping.Wrap, Foreground = Avalonia.Media.Brush.Parse("#EDEDF5") },
                    new Button { Content = "OK", Width = 70, Background = Avalonia.Media.Brush.Parse("#7C6CF0"), Foreground = Avalonia.Media.Brushes.White, CornerRadius = new Avalonia.CornerRadius(8) }
                }
            }
        };
        await dlg.ShowDialog(this);
    }

    void OnCancel(object sender, RoutedEventArgs e) => Close(false);
}