// Altium.Designer.HotReload (source package, compiled into Debug shims only).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Control = System.Windows.Forms.Control;

namespace Altium.Designer.HotReload
{
    /// <summary>
    /// Loads the implementation module of an Altium Designer extension into a collectible
    /// <see cref="AssemblyLoadContext"/>, swaps it when the file is rebuilt, and serves the dev pipe.
    /// Create it once with <see cref="Start"/> on AD's UI thread; call every member on that thread.
    ///
    /// <para><b>Contract</b> (BCL types only). The module exposes a public static class <c>HotEntry</c>:</para>
    /// <code>
    /// public static string   Invoke(string command, string parameters, IDictionary&lt;string, object&gt; env); // required
    /// public static string[] GetCommands();                        // optional, "Name\tDescription"
    /// public static void     OnLoad(IDictionary&lt;string, object&gt; env); // optional
    /// public static void     OnUnload();                           // optional
    /// </code>
    /// <para>env: Origin ("UI" | "Pipe" | "Host"), Context (IServerDocumentView or null),
    /// Log (Action&lt;string&gt;), Generation (int), ContractVersion (int).</para>
    /// </summary>
    internal sealed class HotReloadHost : IDisposable
    {
        /// <summary>Version of the <c>HotEntry</c> contract and env keys. Incremented only on breaking changes.</summary>
        public const int ContractVersion = 1;

        private readonly AssemblyLoadContext _hostContext;
        private readonly Control _ui;
        private readonly List<(int Generation, WeakReference Context)> _retired = new List<(int, WeakReference)>();
        private readonly object _logGate = new object();
        private readonly FileSystemWatcher _watcher;
        private readonly Timer _debounce;
        private readonly DevPipeServer _pipe;

        private HotLoadContext _context;
        private Func<string, string, IDictionary<string, object>, string> _invoke;
        private Func<string[]> _getCommands;
        private Action _onUnload;
        private string _hash;
        private DateTime _writeTimeUtc;
        // Private dependencies (name, size, write time) last seen on disk / of the loaded generation.
        private string _seenDependencies;
        private string _loadedDependencies;
        // Set while the build on disk is rejected (the previous generation keeps running).
        private string _rejection;
        private DateTime _loadedAt;
        private int _generation;
        private int _collected;
        // > 0 while module code runs (Invoke, GetCommands, OnLoad, OnUnload) or a generation is being swapped.
        // Module code may pump messages (modal dialog, DoEvents), which can re-enter the host on this thread.
        private int _depth;
        private bool _reloadPending;
        private bool _forcePending;
        private bool _disposed;

        /// <summary>
        /// Starts the host: watches <paramref name="modulePath"/> and opens the pipe
        /// <c>&lt;name&gt;.HotReload.&lt;pid&gt;</c>. The module itself loads on first use.
        /// </summary>
        /// <param name="name">Extension name (the AD server name); used for the pipe and the log.</param>
        /// <param name="modulePath">Implementation DLL; with the package's MSBuild targets this is <c>HotReloadBuild.ModulePath</c>.</param>
        public static HotReloadHost Start(string name, string modulePath) => new HotReloadHost(name, modulePath);

        private HotReloadHost(string name, string modulePath)
        {
            Name = name;
            ModulePath = Path.GetFullPath(modulePath);
            PipeName = $"{name}.HotReload.{Environment.ProcessId}";
            LogPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Altium.Designer.HotReload", name + ".log");
            _hostContext = AssemblyLoadContext.GetLoadContext(typeof(HotReloadHost).Assembly) ?? AssemblyLoadContext.Default;

            // A hidden window on the UI thread: BeginInvoke posts a message that AD's message loop dispatches.
            _ui = new Control();
            _ = _ui.Handle;

            Log($"--- {name} hot reload host (contract v{ContractVersion}), module {ModulePath}, pipe {PipeName}");

            try
            {
                var directory = Path.GetDirectoryName(ModulePath);
                Directory.CreateDirectory(directory);
                _debounce = new Timer(_ => Post(() =>
                {
                    try { EnsureCurrent(); }
                    catch (Exception ex) { Log("Reload after file change failed: " + ex.Message); }
                }));
                _watcher = new FileSystemWatcher(directory)
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime,
                };
                // The module and its private dependencies (see DependencyStamp).
                _watcher.Filters.Add("*.dll");
                _watcher.Filters.Add("*.deps.json");
                FileSystemEventHandler changed = (_, __) => _debounce.Change(400, Timeout.Infinite);
                _watcher.Changed += changed;
                _watcher.Created += changed;
                _watcher.Renamed += (_, __) => _debounce.Change(400, Timeout.Infinite);
                _watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                LastError = "File watcher failed: " + ex.Message;
                Log(LastError);
            }

            _pipe = new DevPipeServer(PipeName, HandlePipeRequest, Log);
        }

        public string Name { get; }
        public string ModulePath { get; }
        public string PipeName { get; }
        public string LogPath { get; }

        /// <summary>1 for the first load, +1 per successful reload, 0 while nothing is loaded.</summary>
        public int Generation => _generation;

        public string LastError { get; private set; }

        /// <summary>Runs a module command from an AD process (menu item). Reloads first if the file changed.</summary>
        public string Invoke(string command, string parameters, object context) =>
            Invoke(command, parameters, "UI", context, Log);

        /// <summary>Commands of the loaded generation ("Name\tDescription"), if the module implements GetCommands.</summary>
        public IReadOnlyList<string> GetCommands()
        {
            EnsureCurrent();
            var getCommands = _getCommands;
            if (getCommands == null)
                return Array.Empty<string>();
            return Busy(() => getCommands() ?? Array.Empty<string>());
        }

        /// <summary>Loads the module again even if unchanged. A failing build throws and keeps the old generation.</summary>
        public string Reload() => EnsureCurrent(force: true);

        /// <summary>Human-readable diagnostics. <paramref name="collect"/> forces a GC first, to prove old generations unload.</summary>
        public string Status(bool collect)
        {
            var pending = PendingUnload(collect);
            var sb = new StringBuilder();
            sb.AppendLine($"Host       : {Name} (pid {Environment.ProcessId}, .NET {Environment.Version}, contract v{ContractVersion})");
            sb.AppendLine($"Pipe       : \\\\.\\pipe\\{PipeName}");
            sb.AppendLine($"Module     : {ModulePath}");
            sb.AppendLine(_invoke != null
                ? $"Loaded     : generation {_generation}, hash {_hash}, at {_loadedAt:HH:mm:ss}"
                : "Loaded     : (nothing yet)");
            sb.AppendLine($"Unloaded   : {_collected} previous generation(s) collected");
            if (pending.Length > 0)
                sb.AppendLine($"Pending    : generation(s) {string.Join(", ", pending)} " + (collect
                    ? "survived a full GC: something still references them (see README, Unload pitfalls)"
                    : "(run 'status -gc' to force a collection)"));
            if (_depth > 0 || _reloadPending)
                sb.AppendLine($"Busy       : module code is running" + (_reloadPending ? "; a reload is deferred until it completes" : ""));
            sb.AppendLine($"Log        : {LogPath}");
            if (LastError != null)
                sb.AppendLine($"Last error : {LastError}");
            if (_pipe?.LastError != null)
                sb.AppendLine($"Pipe error : {_pipe.LastError}");
            return sb.ToString();
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _pipe.Dispose();
            _watcher?.Dispose();
            _debounce?.Dispose();
            UnloadCurrent();
            _ui.Dispose();
        }

        /// <summary>Generations unloaded but not yet garbage collected.</summary>
        internal int[] PendingUnload(bool collect)
        {
            if (collect)
            {
                // An unloaded context goes away over several collections (its loader allocator is freed by a
                // finalizer, which frees more): keep collecting while something is still pending, as the .NET docs
                // on verifying unloadability recommend, rather than a fixed number of times.
                for (var i = 0; i < 10 && (i < 2 || _retired.Any(r => r.Context.IsAlive)); i++)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                }
            }
            _collected += _retired.RemoveAll(r => !r.Context.IsAlive);
            return _retired.Select(r => r.Generation).ToArray();
        }

        /// <summary>Runs <paramref name="func"/> on the UI thread (from any thread).</summary>
        internal Task<T> OnUi<T>(Func<T> func)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            if (!Post(() =>
                {
                    try { tcs.SetResult(func()); }
                    catch (Exception ex) { tcs.SetException(ex); }
                }))
                tcs.SetException(new ObjectDisposedException(nameof(HotReloadHost)));
            return tcs.Task;
        }

        /// <summary>
        /// Loads the module if the file or one of its private dependencies changed (or <paramref name="force"/>).
        /// Returns what happened, or null when already current. A failing load throws and keeps the previous generation.
        /// </summary>
        internal string EnsureCurrent(bool force = false)
        {
            if (_disposed)
                return null;
            if (_depth > 0)
            {
                // Re-entered while module code runs or a generation is being swapped (it pumps messages,
                // e.g. a modal dialog): never swap code under it. Runs once the outer operation completes.
                _reloadPending = true;
                _forcePending |= force;
                return "Reload deferred until the running module code completes.";
            }

            var file = new FileInfo(ModulePath);
            if (!file.Exists)
            {
                if (_invoke != null)
                    return null;
                throw new FileNotFoundException("Implementation module not found. Build it first.", ModulePath);
            }
            var dependencies = DependencyStamp();
            if (!force && _invoke != null && file.LastWriteTimeUtc == _writeTimeUtc && dependencies == _seenDependencies)
                return null;

            byte[] image;
            try
            {
                image = HotLoadContext.ReadAllBytes(ModulePath);
            }
            catch (IOException ex) when (!force && _invoke != null && !(ex is FileNotFoundException))
            {
                // Still being written (the build holds it open). The watcher fires again when the writer closes it.
                var busy = $"The build on disk is still being written ({ex.Message.Trim()}); still running generation {_generation}.";
                Log(busy);
                return busy;
            }
            var hash = Convert.ToHexString(SHA256.HashData(image)).Substring(0, 12);
            _writeTimeUtc = file.LastWriteTimeUtc;
            _seenDependencies = dependencies;
            if (!force && _invoke != null && hash == _hash && dependencies == _loadedDependencies)
            {
                _rejection = null; // back to the build that is running
                return null;
            }

            var reason = !force && _invoke != null && hash == _hash ? " (dependencies changed)" : "";
            return Busy(() => LoadGeneration(image, hash, dependencies)) + reason;
        }

        /// <summary>Name, size and write time of the private dependencies next to the module (<c>*.dll</c>, <c>*.deps.json</c>).</summary>
        private string DependencyStamp()
        {
            try
            {
                var directory = new DirectoryInfo(Path.GetDirectoryName(ModulePath));
                return string.Join("|", directory.EnumerateFiles("*.dll").Concat(directory.EnumerateFiles("*.deps.json"))
                    .Where(f => !string.Equals(f.FullName, ModulePath, StringComparison.OrdinalIgnoreCase))
                    .Select(f => $"{f.Name.ToLowerInvariant()}:{f.Length}:{f.LastWriteTimeUtc.Ticks}")
                    .OrderBy(s => s, StringComparer.Ordinal));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return _seenDependencies; // can't tell: assume unchanged
            }
        }

        /// <summary>
        /// Runs <paramref name="body"/> as module code: a reload requested meanwhile (re-entrantly, through a
        /// message pump in the module) is deferred and runs once the outermost such operation completes.
        /// </summary>
        private T Busy<T>(Func<T> body)
        {
            _depth++;
            try
            {
                return body();
            }
            finally
            {
                if (--_depth == 0 && _reloadPending && !_disposed)
                {
                    var force = _forcePending;
                    _reloadPending = false;
                    _forcePending = false;
                    try
                    {
                        var result = EnsureCurrent(force);
                        if (result != null)
                            Log("Deferred reload: " + result);
                    }
                    catch (Exception ex) { Log("Deferred reload failed: " + ex.Message); }
                }
            }
        }

        private string LoadGeneration(byte[] image, string hash, string dependencies)
        {
            var next = _generation + 1;
            var context = new HotLoadContext($"{Name}.Hot#{next}", ModulePath, _hostContext);
            if (context.ResolverError != null && next == 1)
                Log("No .deps.json resolution, probing the module folder only: " + context.ResolverError);
            Func<string, string, IDictionary<string, object>, string> invoke;
            Func<string[]> getCommands;
            Action<IDictionary<string, object>> onLoad;
            Action onUnload;
            try
            {
                var entry = context.LoadMain(ModulePath, image).GetExportedTypes()
                                .FirstOrDefault(t => t.Name == "HotEntry" && t.IsAbstract && t.IsSealed)
                            ?? throw new InvalidOperationException("No public static class 'HotEntry' found.");
                invoke = Bind<Func<string, string, IDictionary<string, object>, string>>(entry, "Invoke")
                         ?? throw new InvalidOperationException("HotEntry.Invoke(string, string, IDictionary<string, object>) returning string not found.");
                getCommands = Bind<Func<string[]>>(entry, "GetCommands");
                onLoad = Bind<Action<IDictionary<string, object>>>(entry, "OnLoad");
                onUnload = Bind<Action>(entry, "OnUnload");
            }
            catch (Exception ex)
            {
                context.Unload();
                LastError = $"Build {hash} rejected: {Describe(ex)}" + (_invoke != null ? $" Still running generation {_generation}." : "");
                _rejection = LastError;
                Log(LastError);
                throw new HotReloadException(LastError, ex);
            }

            var previous = UnloadCurrent();
            _context = context;
            _invoke = invoke;
            _getCommands = getCommands;
            _onUnload = onUnload;
            _generation = next;
            _hash = hash;
            _loadedDependencies = dependencies;
            _loadedAt = DateTime.Now;
            LastError = null;
            _rejection = null;

            try { onLoad?.Invoke(CreateEnv("Host", null, Log)); }
            catch (Exception ex)
            {
                LastError = $"OnLoad of generation {next} failed: {Describe(ex)}";
                Log(LastError);
            }

            var message = previous > 0
                ? $"Reloaded {Name}: generation {previous} -> {next} ({hash})."
                : $"Loaded {Name} generation {next} ({hash}).";
            Log(message);
            return message;
        }

        private string Invoke(string command, string parameters, string origin, object context, Action<string> log)
        {
            EnsureCurrent();
            var invoke = _invoke ?? throw new HotReloadException(
                $"{Name} is loading or unloading a generation (its OnLoad/OnUnload is still running): try again when it completes.", null);
            return Busy(() =>
            {
                try
                {
                    return invoke(command, parameters ?? string.Empty, CreateEnv(origin, context, log));
                }
                catch (Exception ex)
                {
                    Log($"Command '{command}' failed: {ex}");
                    throw;
                }
            });
        }

        private int UnloadCurrent()
        {
            if (_context == null)
                return 0;

            // Detach first: module code re-entered from OnUnload (a message pump) must not call into this generation.
            var onUnload = _onUnload;
            _invoke = null;
            _getCommands = null;
            _onUnload = null;
            _depth++;
            try { onUnload?.Invoke(); }
            catch (Exception ex) { Log($"OnUnload of generation {_generation} failed: {Describe(ex)}"); }
            finally { _depth--; }

            FrameworkCaches.Clear(Log);
            _context.Unload();
            _retired.Add((_generation, new WeakReference(_context)));
            _context = null;
            return _generation;
        }

        private async Task<int> HandlePipeRequest(string request, Action<string> emit)
        {
            var (verb, rest) = SplitFirst(request);
            switch (verb.ToLowerInvariant())
            {
                case "ping":
                    emit($"pong from {Name} (pid {Environment.ProcessId}, generation {_generation}, contract v{ContractVersion})");
                    return 0;
                case "status":
                    return await OnUiOrError(emit, () => emit(Status(collect: rest.Contains("-gc"))));
                case "reload":
                    return await OnUiOrError(emit, () => emit(Reload()));
                case "list":
                    return await OnUiOrError(emit, () => { foreach (var line in GetCommands()) emit(line); });
                case "run":
                    var (command, parameters) = SplitFirst(rest);
                    if (command.Length == 0)
                    {
                        emit("usage: run <command> [Key=Value|Key=Value]");
                        return 2;
                    }
                    return await OnUiOrError(emit, () =>
                    {
                        var reloaded = EnsureCurrent();
                        if (reloaded != null)
                            emit("[hot] " + reloaded);
                        // The file watcher may have rejected the latest build already: never pass off
                        // the previous generation's result as the new build's.
                        if (_rejection != null)
                            throw new HotReloadException(_rejection, null);
                        var result = Invoke(command, parameters, "Pipe", null, emit);
                        if (!string.IsNullOrEmpty(result))
                            emit(result);
                    });
                default:
                    emit("Verbs: ping | status [-gc] | reload | list | run <command> [Key=Value|Key=Value]");
                    return verb == "help" ? 0 : 2;
            }
        }

        private async Task<int> OnUiOrError(Action<string> emit, Action action)
        {
            try
            {
                await OnUi(() => { action(); return 0; }).ConfigureAwait(false);
                return 0;
            }
            catch (Exception ex)
            {
                // Errors meant for the user (matched by name, so the module needs no shared type): message only.
                var expected = ex is ArgumentException || ex is HotReloadException || ex is FileNotFoundException || ex.GetType().Name == "UserException";
                emit("ERROR: " + (expected ? ex.Message : ex.ToString()));
                return 1;
            }
        }

        private bool Post(Action action)
        {
            try
            {
                _ui.BeginInvoke(action);
                return true;
            }
            catch (Exception ex) when (ex is ObjectDisposedException || ex is InvalidOperationException)
            {
                return false; // shutting down
            }
        }

        private Dictionary<string, object> CreateEnv(string origin, object context, Action<string> log) => new Dictionary<string, object>
        {
            ["Origin"] = origin,
            ["Context"] = context,
            ["Log"] = log,
            ["Generation"] = _generation,
            ["ContractVersion"] = ContractVersion,
        };

        private static T Bind<T>(Type entry, string name) where T : Delegate
        {
            var signature = typeof(T).GetMethod("Invoke");
            var method = entry.GetMethod(name, BindingFlags.Public | BindingFlags.Static,
                signature.GetParameters().Select(p => p.ParameterType).ToArray());
            return method != null && method.ReturnType == signature.ReturnType
                ? (T)Delegate.CreateDelegate(typeof(T), method)
                : null;
        }

        private static (string Head, string Tail) SplitFirst(string text)
        {
            text = (text ?? string.Empty).Trim();
            var space = text.IndexOf(' ');
            return space < 0 ? (text, string.Empty) : (text.Substring(0, space), text.Substring(space + 1).Trim());
        }

        private static string Describe(Exception ex) => ex is ReflectionTypeLoadException rtle
            ? string.Join("; ", rtle.LoaderExceptions.Where(e => e != null).Select(e => e.Message))
            : ex.Message;

        /// <summary>Appends to the log file. Never throws: logging must not take AD down.</summary>
        private void Log(string message)
        {
            try
            {
                lock (_logGate)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {message}{Environment.NewLine}");
                }
            }
            catch
            {
                // ignored
            }
        }
    }

    /// <summary>A build was rejected; the previous generation keeps running.</summary>
    internal sealed class HotReloadException : Exception
    {
        public HotReloadException(string message, Exception inner) : base(message, inner) { }
    }
}
