using Avalonia;
using CodexProvider.Core;
using System;
using System.Linq;

namespace CodexProvider.UI;

class Program
{
    // CLI mode: when invoked with arguments, run headless (works cross-platform, no GUI needed).
    // GUI otherwise. Same binary does both, like the original WinForms app.
    [STAThread]
    public static int Main(string[] args)
    {
        CodexConfig.Platform = OperatingSystem.IsWindows() ? new WindowsHooks() : new UnixHooks();

        if (args.Length > 0)
        {
            return RunCli(args);
        }

        // GUI — a UI handler crash must log, not kill the app
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        return 0;
    }

    static int RunCli(string[] args)
    {
        CodexConfig.Load();
        string cmd = args[0].ToLower();
        switch (cmd)
        {
            case "list":
                Console.WriteLine("Active provider: " + (CodexConfig.CurrentProvider ?? "(openai default)") +
                                  "  |  model: " + (CodexConfig.CurrentModel ?? "(app default)"));
                foreach (var p in CodexConfig.Providers)
                {
                    CodexConfig.DefaultModels.TryGetValue(p.Id, out var dm);
                    string model = p.Id == CodexConfig.CurrentProvider
                        ? (CodexConfig.CurrentModel ?? "(app default)")
                        : (dm ?? "(app default)");
                    Console.WriteLine("  - " + p.Id.PadRight(18) + " -> " + model + (p.Id == CodexConfig.CurrentProvider ? "  <-- active" : ""));
                }
                return 0;

            case "models" when args.Length > 1:
            {
                var p = CodexConfig.Find(args[1]);
                if (p == null) { Console.WriteLine("Unknown provider: " + args[1]); return 1; }
                var models = CodexConfig.Platform.FetchModels(p);
                string filter = args.Length > 2 ? args[2].ToLower() : null;
                foreach (var m in models.Where(m => filter == null || m.ToLower().Contains(filter)))
                    Console.WriteLine("  " + m);
                return 0;
            }

            case "set" when args.Length > 1:
            {
                string id = args[1];
                if (CodexConfig.Find(id) == null) { Console.WriteLine("Unknown provider: " + id); return 1; }
                string model = args.Length > 2 ? args[2] : null;
                if (model == null)
                {
                    CodexConfig.DefaultModels.TryGetValue(id, out var dm);
                    CodexConfig.Switch(id, dm ?? "", dm == null);
                }
                else CodexConfig.Switch(id, model, false);
                Console.WriteLine("Active now: " + id + " | model: " + (CodexConfig.CurrentModel ?? "(app default)"));
                if (CodexConfig.RestartAfterSwitch) Console.WriteLine("(restart the desktop app to apply: run 'restart' or use the GUI)");
                return 0;
            }

            case "add" when args.Length > 2:
            {
                string id = args[1], url = args[2];
                string envKey = args.Length > 3 && args[3] != "-" ? args[3] : null;
                string model = args.Length > 4 ? args[4] : null;
                CodexConfig.AddProvider(id, id, url, envKey, "responses");
                CodexConfig.SaveDefaultModel(id, model);
                Console.WriteLine("Provider added: " + id + " -> " + url);
                return 0;
            }

            case "remove" when args.Length > 1:
                CodexConfig.RemoveProvider(args[1]);
                Console.WriteLine("Provider removed: " + args[1]);
                return 0;

            case "restart":
                Console.WriteLine(CodexConfig.Platform.RestartDesktopApp());
                return 0;

            case "help":
            default:
                Console.WriteLine("CodexProvider CLI - manage Codex providers (cross-platform)");
                Console.WriteLine("Usage: codexprovider <command> [args]");
                Console.WriteLine("  list                     show providers and the active one");
                Console.WriteLine("  set <id> [model]         activate a provider (model optional)");
                Console.WriteLine("  models <id> [filter]     list models (filter = substring)");
                Console.WriteLine("  add <id> <url> [env|-] [model]");
                Console.WriteLine("  remove <id>");
                Console.WriteLine("  restart                  restart the Codex desktop app");
                return cmd == "help" ? 0 : 1;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}