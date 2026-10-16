using System.IO.Pipes;
using System.Text;
using System.Windows;
using CasualtiesModManager.Services;

namespace CasualtiesModManager;

public partial class App : Application
{
    private const string MutexName = @"Local\CasualtiesModManager.SingleInstance";
    private const string PipeName = "CasualtiesModManager.Ipc";

    private Mutex? _mutex;

    /// <summary>nxm-ссылка, с которой запустили программу (если запустил браузер после клика на сайте Nexus).</summary>
    public static string? StartupNxm { get; private set; }

    protected override void OnStartup(StartupEventArgs e)
    {
        // нас перезапустило автообновление — ждём, пока старая копия закроется
        SelfUpdater.WaitForPreviousProcess(e.Args);

        var nxm = e.Args.FirstOrDefault(NexusService.IsNxm);

        // Браузер запускает новый процесс на каждую nxm-ссылку. Передаём её уже работающему менеджеру и выходим.
        _mutex = new Mutex(true, MutexName, out var first);
        if (!first)
        {
            SendToRunning(nxm ?? "show");
            Environment.Exit(0);
            return;
        }

        StartupNxm = nxm;
        SelfUpdater.CleanupOld();
        base.OnStartup(e);
        _ = Task.Run(ListenAsync);
    }

    private static void SendToRunning(string message)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(5000);
            using var w = new StreamWriter(client, new UTF8Encoding(false)) { AutoFlush = true };
            w.WriteLine(message);
        }
        catch { /* первый экземпляр не отвечает — ничего не поделаешь */ }
    }

    /// <summary>Принимает сообщения от повторных запусков (только от того же пользователя Windows).</summary>
    private async Task ListenAsync()
    {
        while (true)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync();

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(server, Encoding.UTF8);
                var line = (await reader.ReadLineAsync(cts.Token))?.Trim();
                if (string.IsNullOrEmpty(line)) continue;

                // принимаем только «show» и nxm-ссылки; всё остальное игнорируем
                if (line != "show" && !NexusService.IsNxm(line)) continue;
                _ = Dispatcher.InvokeAsync(() => (MainWindow as MainWindow)?.HandleExternal(line));
            }
            catch
            {
                await Task.Delay(500);
            }
        }
    }
}
