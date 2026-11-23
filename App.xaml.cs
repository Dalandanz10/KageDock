using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using KageDock.Services;
using KageDock.Views;

namespace KageDock
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = @"Local\KageDock_SingleInstance";
        private const string PipeName = "KageDock_SettingsPipe";
        private SpotifyMediaSessionService? _spotifyMediaSessionService;
        private Mutex? _instanceMutex;
        private CancellationTokenSource? _ipcCancellation;
        private DockWindow? _dockWindow;
        private bool _ownsMutex;

        private static void LogCrash(string source, Exception ex)
        {
            try
            {
                string logPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "crash-log.txt");

                File.AppendAllText(
                    logPath,
                    $"[{DateTime.Now}] {source}\n{ex}\n\n");
            }
            catch
            {
                // Ignore logging failures.
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DispatcherUnhandledException += (_, args) =>
            {
                LogCrash("Dispatcher exception", args.Exception);
                args.Handled = false;
            };

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception ex)
                    LogCrash("AppDomain exception", ex);
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                LogCrash("Unobserved task exception", args.Exception);
            };

            _instanceMutex = new Mutex(true, MutexName, out bool createdNew);
            _ownsMutex = createdNew;

            if (!createdNew)
            {
                _instanceMutex.Dispose();
                _instanceMutex = null;

                NotifyExistingInstance();
                Shutdown();
                return;
            }

            _ipcCancellation = new CancellationTokenSource();

            _spotifyMediaSessionService = new SpotifyMediaSessionService(
                Dispatcher);

            _dockWindow = new DockWindow(_spotifyMediaSessionService);
            MainWindow = _dockWindow;
            _dockWindow.Show();

            _ = ListenForCommandsAsync(_ipcCancellation.Token);
        }

        private static void NotifyExistingInstance()
        {
            // Retry briefly in case the first instance is still starting its pipe server.
            for (int attempt = 0; attempt < 6; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(
                        ".",
                        PipeName,
                        PipeDirection.Out);

                    client.Connect(500);

                    using var writer = new StreamWriter(client)
                    {
                        AutoFlush = true
                    };

                    writer.WriteLine("OPEN_SETTINGS");
                    return;
                }
                catch (TimeoutException)
                {
                    Thread.Sleep(250);
                }
                catch (IOException)
                {
                    Thread.Sleep(250);
                }
            }
        }

        private async Task ListenForCommandsAsync(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    await using var server = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.In,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                    await server.WaitForConnectionAsync(cancellationToken);

                    using var reader = new StreamReader(server);
                    string? command = await reader.ReadLineAsync(cancellationToken);

                    if (command == "OPEN_SETTINGS")
                    {
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _dockWindow?.OpenSettingsFromExternalRequest();
                        });
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal shutdown.
            }
            catch (Exception ex)
            {
                LogCrash("Single-instance IPC exception", ex);
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _ipcCancellation?.Cancel();
            _ipcCancellation?.Dispose();
            _ipcCancellation = null;

            if (_ownsMutex && _instanceMutex != null)
            {
                _instanceMutex.ReleaseMutex();
                _ownsMutex = false;
            }

            _instanceMutex?.Dispose();
            _instanceMutex = null;

            _spotifyMediaSessionService?.Dispose();
            _spotifyMediaSessionService = null;

            base.OnExit(e);
        }
    }
}
