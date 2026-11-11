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
        private volatile string _lastError;

        public DevPipeServer(string pipeName, Func<string, Action<string>, Task<int>> handler, Action<string> log)
        {
            _pipeName = pipeName;
            _handler = handler;
            _log = log;
            Task.Run(AcceptLoop);
        }

        /// <summary>Why the pipe cannot be created or accept connections right now, or null while it works.</summary>
        public string LastError => _lastError;

        private async Task AcceptLoop()
        {
            var failures = 0;
            string lastLogged = null;
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                    if (failures > 0)
                    {
                        _log($"Pipe {_pipeName} available again after {failures} failed attempt(s).");
                        failures = 0;
                        lastLogged = null;
                        _lastError = null;
                    }
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
                    failures++;
                    _lastError = $"Pipe unavailable, retrying ({failures} failed attempt(s)): {ex.Message}";
                    // Log the first failure and each change of cause, not every retry.
                    if (ex.Message != lastLogged)
                    {
                        _log("Pipe accept failed: " + ex.Message + " (retrying with backoff up to 30 s; identical failures are not logged again)");
                        lastLogged = ex.Message;
                    }
                    // 1 s, 2 s, 4 s ... capped at 30 s.
                    var delay = TimeSpan.FromSeconds(Math.Min(30, 1 << Math.Min(failures - 1, 5)));
                    try { await Task.Delay(delay, _cts.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) { break; }
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

                // The module may keep this delegate (env "Log") and call it after the request ended, from any
                // thread. From then on it appends to the host log instead, and it never throws.
                var request = new RequestOutput(queue, _log);
                var exitCode = 1;
                try
                {
                    var line = await reader.ReadLineAsync().ConfigureAwait(false);
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        _log("pipe> " + line);
                        exitCode = await _handler(line.Trim(), request.Emit).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    request.Emit("ERROR: " + ex);
                }
                finally
                {
                    queue.TryAdd(EndMarker + exitCode.ToString());
                    request.Close();
                    queue.CompleteAdding();
                    await pump.ConfigureAwait(false);
                }
            }
        }

        public void Dispose() => _cts.Cancel();

        /// <summary>The emit delegate of one request: streams to the client while the request runs, then logs.</summary>
        private sealed class RequestOutput
        {
            private readonly BlockingCollection<string> _queue;
            private readonly Action<string> _log;
            private volatile bool _closed;

            public RequestOutput(BlockingCollection<string> queue, Action<string> log)
            {
                _queue = queue;
                _log = log;
            }

            public void Close() => _closed = true;

            public void Emit(string text)
            {
                if (!_closed)
                {
                    try
                    {
                        var added = true;
                        foreach (var line in (text ?? string.Empty).Replace("\r\n", "\n").Split('\n'))
                            added &= _queue.TryAdd(line);
                        if (added)
                            return;
                    }
                    catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
                    {
                        // the request completed meanwhile
                    }
                }
                try { _log("(log after its pipe request ended) " + text); }
                catch { /* never throw into the module */ }
            }
        }
    }
}
