using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using CodexProvider.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CodexProvider.UI;

public partial class MainWindow : Window
{
    List<string> currentModels = new List<string>();
    ProviderInfo selected;

    public MainWindow()
    {
        InitializeComponent();
        CodexConfig.Platform = OperatingSystem.IsWindows() ? new WindowsHooks() : new UnixHooks();
        CodexConfig.LoadSettings();
        ChkRestart.IsChecked = CodexConfig.RestartAfterSwitch;
        RefreshState();
    }

    void Log(string msg, string color = null)
    {
        // Avalonia TextBox.Text can be null; never crash on logging
        TxtLog.Text = ((TxtLog.Text ?? "").Length > 0 ? TxtLog.Text + "\n" : "") + msg;
    }

    void RefreshState()
    {
        CodexConfig.Load();
        LstProviders.ItemsSource = CodexConfig.Providers.Select(p =>
            p.Id + (p.Id == CodexConfig.CurrentProvider ? "   <-- active" : ""));
        LblActive.Text = (CodexConfig.CurrentProvider ?? "(default)") + "  -  " + (CodexConfig.CurrentModel ?? "(app default)");
        if (CodexConfig.CurrentProvider != null)
        {
            int idx = CodexConfig.Providers.FindIndex(p => p.Id == CodexConfig.CurrentProvider);
            if (idx >= 0) LstProviders.SelectedIndex = idx;
        }
    }

    void OnProviderSelected(object sender, SelectionChangedEventArgs e)
    {
        if (LstProviders.SelectedIndex < 0) return;
        selected = CodexConfig.Providers[LstProviders.SelectedIndex];
        LoadModels();
    }

    async void LoadModels()
    {
        if (selected == null) return;
        LstModels.ItemsSource = new[] { "(loading...)" };
        var models = await System.Threading.Tasks.Task.Run(() => CodexConfig.Platform.FetchModels(selected));
        currentModels = models;
        RenderModels();
    }

    void RenderModels()
    {
        string q = (TxtSearch.Text ?? "").Trim();
        IEnumerable<string> list = currentModels;
        if (q.Length > 0) list = currentModels.Where(m => m.ToLower().Contains(q.ToLower()));
        var l = list.ToList();
        LstModels.ItemsSource = l.Count > 0 ? l : new[] { "(no model matches the search)" };
    }

    void OnSearchChanged(object sender, TextChangedEventArgs e) => RenderModels();

    void OnActivate(object sender, RoutedEventArgs e)
    {
        if (selected == null) { Log("Select a provider first."); return; }
        string model = CodexConfig.DefaultModels.TryGetValue(selected.Id, out var m) ? m : null;
        CodexConfig.Switch(selected.Id, model ?? "", model == null);
        Log("Active provider: " + selected.Id + (model != null ? " | model: " + model : " | (app default model)"));
        MaybeRestart();
        RefreshState();
    }

    void OnUseModel(object sender, RoutedEventArgs e)
    {
        if (selected == null || LstModels.SelectedItem == null) { Log("Select a model first."); return; }
        string model = LstModels.SelectedItem.ToString();
        if (!currentModels.Contains(model)) { Log("Select a real model (not a placeholder)."); return; }
        CodexConfig.Switch(selected.Id, model, false);
        Log("Active: " + selected.Id + " | model: " + model);
        MaybeRestart();
        RefreshState();
    }

    void MaybeRestart()
    {
        if (CodexConfig.RestartAfterSwitch)
        {
            Log(CodexConfig.Platform.RestartDesktopApp());
        }
    }

    void OnRestartNow(object sender, RoutedEventArgs e)
    {
        Log(CodexConfig.Platform.RestartDesktopApp());
        RefreshState();
    }

    void OnRestoreOpenai(object sender, RoutedEventArgs e)
    {
        // reset to the built-in openai account: remove model_provider/model overrides,
        // then ALWAYS restart the desktop app (that is the point of this button)
        CodexConfig.Switch("openai", "", true);
        Log("OpenAI restored as provider (overrides removed). Restarting the app...");
        Log(CodexConfig.Platform.RestartDesktopApp());
        RefreshState();
    }

    void OnRestartCheckChanged(object sender, RoutedEventArgs e)
    {
        CodexConfig.RestartAfterSwitch = ChkRestart.IsChecked == true;
        CodexConfig.SaveSettings();
    }

    void OnAddProvider(object sender, RoutedEventArgs e)
    {
        var f = new AddProviderForm();
        f.ShowDialog(this).GetAwaiter().GetResult();
        if (f.ProviderId != null) Log("Provider added: " + f.ProviderId);
        RefreshState();
    }

    void OnEditProvider(object sender, RoutedEventArgs e)
    {
        if (selected == null) { Log("Select a provider to edit."); return; }
        var f = new AddProviderForm(selected);
        f.ShowDialog(this).GetAwaiter().GetResult();
        Log("Provider updated: " + selected.Id);
        RefreshState();
    }

    void OnRemoveProvider(object sender, RoutedEventArgs e)
    {
        if (selected == null) { Log("Select a provider to remove."); return; }
        string id = selected.Id;
        bool wasActive = id == CodexConfig.CurrentProvider;
        CodexConfig.RemoveProvider(id);
        Log("Provider removed: " + id + (wasActive ? " (reset to openai)" : ""));
        RefreshState();
    }
}