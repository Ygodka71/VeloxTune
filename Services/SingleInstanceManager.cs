using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ModernPlayer.Services
{
    public static class SingleInstanceManager
    {
        private static Mutex? _mutex;
        private static CancellationTokenSource? _cts;
        private static readonly string PipeName = $"VeloxTune_Pipe_{Environment.UserName}";
        private static readonly string MutexName = $"VeloxTune_Mutex_{Environment.UserName}";

        public static event Action<string>? FileReceived;

        public static bool IsFirstInstance()
        {
            _mutex = new Mutex(true, MutexName, out bool createdNew);
            return createdNew;
        }

        public static void StartServer()
        {
            _cts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    try
                    {
                        var server = new NamedPipeServerStream(
                            PipeName,
                            PipeDirection.In,
                            NamedPipeServerStream.MaxAllowedServerInstances,
                            PipeTransmissionMode.Byte,
                            PipeOptions.Asynchronous);

                        await server.WaitForConnectionAsync(_cts.Token);

                        // Process incoming connection asynchronously without blocking subsequent connections!
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using (server)
                                using (var reader = new StreamReader(server, Encoding.UTF8))
                                {
                                    string? filePath = await reader.ReadLineAsync();
                                    if (!string.IsNullOrWhiteSpace(filePath))
                                    {
                                        FileReceived?.Invoke(filePath);
                                    }
                                }
                            }
                            catch
                            {
                            }
                        });
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch
                    {
                        // Short delay before retrying pipe creation if an error occurs
                        await Task.Delay(100);
                    }
                }
            });
        }

        public static bool SendToExistingInstance(string filePath)
        {
            // Retry sending with small delays to ensure rapid sequential clicks never get dropped
            for (int attempt = 0; attempt < 10; attempt++)
            {
                try
                {
                    using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                    client.Connect(800);

                    using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
                    writer.WriteLine(filePath);
                    return true;
                }
                catch
                {
                    Thread.Sleep(80);
                }
            }
            return false;
        }

        public static void Cleanup()
        {
            try
            {
                _cts?.Cancel();
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch
            {
            }
        }
    }
}
