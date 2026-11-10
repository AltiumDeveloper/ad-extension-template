// Altium.Designer.HotReload (source package, compiled into Debug shims only).
using System;
using System.Collections.Concurrent;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Altium.Designer.HotReload
{
    /// <summary>
    /// Line-based named pipe server, <b>current user only</b> (<see cref="PipeOptions.CurrentUserOnly"/>).
    /// One request per connection: the client sends one line, the server streams output lines and
    /// ends with <c>"\u0004&lt;exitCode&gt;"</c>.
    /// </summary>
    internal sealed class DevPipeServer : IDisposable
    {
        public const char EndMarker = '\u0004';

        private readonly string _pipeName;
        private readonly Func<string, Action<string>, Task<int>> _handler;
        private readonly Action<string> _log;
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        public DevPipeServer(string pipeName, Func<string, Action<string>, Task<int>> handler, Action<string> log)
        {
            _pipeName = pipeName;
            _handler = handler;
            _log = log;
            Task.Run(AcceptLoop);
        }

        private async Task AcceptLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    await server.WaitForConnectionAsync(_cts.Token).ConfigureAwait(false);
                    var connected = server;
                    server = null;
                    _ = Task.Run(() => Serve(connected));
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _log("Pipe accept failed: " + ex.Message);
                    await Task.Delay(1000).ConfigureAwait(false);
                }
                finally
                {
                    server?.Dispose();
                }
            }
        }

        private async Task Serve(NamedPipeServerStream pipe)
        {
            using (pipe)
            {
                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(pipe, utf8, false, 4096, leaveOpen: true);
                using var writer = new StreamWriter(pipe, utf8, 4096, leaveOpen: true) { AutoFlush = true, NewLine = "\n" };

                // Output is produced on AD's UI thread; queue it so a slow client never blocks AD.
                using var queue = new BlockingCollection<string>();
                var pump = Task.Run(() =>
                {
                    try
                    {
                        foreach (var line in queue.GetConsumingEnumerable())
                            writer.WriteLine(line);
                    }
                    catch (IOException)
                    {
                        // client went away
                    }
                });

                var exitCode = 1;
                try
                {
                    var request = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(request))
                    {
                        _log("pipe> " + request);
                        exitCode = await _handler(request.Trim(), text =>
                        {
                            foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                                queue.TryAdd(line);
                        }).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    queue.TryAdd("ERROR: " + ex);
                }
                finally
                {
                    queue.TryAdd(EndMarker + exitCode.ToString());
                    queue.CompleteAdding();
                    await pump.ConfigureAwait(false);
                }
            }
        }

        public void Dispose() => _cts.Cancel();
    }
}
