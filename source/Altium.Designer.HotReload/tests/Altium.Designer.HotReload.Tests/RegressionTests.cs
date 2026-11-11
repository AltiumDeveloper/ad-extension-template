using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Altium.Designer.HotReload.Tests
{
    /// <summary>Regression tests for the 1.0.1 fixes. Same set-up as <see cref="HotReloadHostTests"/>.</summary>
    public sealed class RegressionTests : IDisposable
    {
        private const string Usings = "using System; using System.Collections.Generic; using System.Threading; using System.Threading.Tasks; ";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Altium.Designer.HotReload.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _name = "HotReloadRegression" + Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string _modulePath;
        private readonly Thread _uiThread;
        private HotReloadHost _host;
        private Exception _uiCrash;

        public RegressionTests()
        {
            Directory.CreateDirectory(_directory);
            _modulePath = Path.Combine(_directory, "TestModule.dll");

            using var ready = new ManualResetEventSlim();
            _uiThread = new Thread(() =>
            {
                // Like AD: an exception escaping into the message loop is a crash.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                _host = HotReloadHost.Start(_name, _modulePath);
                ready.Set();
                try { Application.Run(); }
                catch (Exception ex) { _uiCrash = ex; }
            });
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            ready.Wait();
        }

        // Issue: the pipe's Log delegate threw ObjectDisposedException / InvalidOperationException when the
        // module kept it and called it after the request (possibly off the UI thread, crashing AD).
        [Fact]
        public void Pipe_log_kept_by_the_module_is_safe_after_the_request()
        {
            WriteModule(Usings + @"public static class HotEntry {
                static Action<string> s_log;
                public static string Invoke(string c, string p, IDictionary<string, object> env) {
                    if (c == ""store"") { s_log = (Action<string>)env[""Log""]; s_log(""during""); return ""stored""; }
                    s_log(""late-ui"");
                    Task.Run(() => s_log(""late-pool"")).Wait();
                    return ""called""; } }");

            var (exitCode, output) = RunClient($"-Name {_name} run store");
            Assert.Equal(0, exitCode);
            Assert.Contains("during", output);
            Assert.Contains("stored", output);

            Assert.Equal("called", OnUi(() => _host.Invoke("call", "", null)));
            var log = File.ReadAllText(_host.LogPath);
            Assert.Contains("late-ui", log);
            Assert.Contains("late-pool", log);
            Assert.Null(_uiCrash);
        }

        // Issue: a module pumping messages in OnUnload let the file watcher's reload run re-entrantly.
        [Fact]
        public void Reload_requested_while_OnUnload_pumps_messages_is_deferred_and_then_runs()
        {
            var key = _name + ".";
            WriteModule(Usings + @"public static class HotEntry {
                public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v1"";
                public static void OnUnload() {
                    var d = AppDomain.CurrentDomain;
                    d.SetData(""" + key + @"unloads"", (int)(d.GetData(""" + key + @"unloads"") ?? 0) + 1);
                    d.SetData(""" + key + @"unloading"", ""yes"");
                    var timeout = DateTime.UtcNow.AddSeconds(15);
                    while (d.GetData(""" + key + @"release"") == null && DateTime.UtcNow < timeout) {
                        System.Windows.Forms.Application.DoEvents(); Thread.Sleep(20); } } }",
                typeof(Form).Assembly.Location, typeof(System.ComponentModel.Component).Assembly.Location);
            Assert.Equal("v1", OnUi(() => _host.Invoke("x", "", null)));

            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v2""; }");
            var reload = _host.OnUi(() => _host.Reload());
            WaitFor(() => AppDomain.CurrentDomain.GetData(key + "unloading") != null, "OnUnload of generation 1 did not start");

            // A newer build lands while OnUnload pumps messages: the watcher's reload is dispatched inside it.
            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v3""; }");
            Thread.Sleep(1500);
            AppDomain.CurrentDomain.SetData(key + "release", "yes");

            var reloaded = Result(reload);
            Assert.Contains("1 -> 2", reloaded);
            // The deferred reload ran when the outer one completed.
            Assert.Equal(3, _host.Generation);
            Assert.Equal("v3", OnUi(() => _host.Invoke("x", "", null)));
            Assert.Equal(1, (int)AppDomain.CurrentDomain.GetData(key + "unloads"));
            Assert.Null(_host.LastError);
            Assert.Contains("Deferred reload", File.ReadAllText(_host.LogPath));
            Assert.Null(_uiCrash);
        }

        // Issue: a DLL still being written was read (FileShare.ReadWrite) and rejected as a bad image.
        [Fact]
        public void A_build_still_being_written_is_waited_for_not_rejected()
        {
            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v1""; }");
            Assert.Equal("v1", OnUi(() => _host.Invoke("x", "", null)));

            var image = Compile(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v2""; }");
            Task<string> call;
            using (var writer = new FileStream(_modulePath, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                writer.SetLength(image.Length);
                writer.Write(image, 0, image.Length / 2);
                writer.Flush(true);
                call = _host.OnUi(() => _host.Invoke("x", "", null));
                Thread.Sleep(500);
                writer.Write(image, image.Length / 2, image.Length - image.Length / 2);
            }

            var result = Result(call);
            Assert.Equal("v2", result);
            Assert.Null(_host.LastError);
        }

        // Issue: dependencies were probed as <moduleDir>\<name>.dll only and every Altium.* name was forced to the
        // host (rejecting the extension's own Altium.* library); a dependency-only change did not reload.
        [Fact]
        public void Private_dependencies_load_unlocked_and_a_dependency_change_reloads()
        {
            var dependency = WriteLibrary("TestDependency", "dep1");
            var ownAltium = WriteLibrary("Altium.HotReloadTests.OwnLibrary", "own1");
            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) =>
                    TestDependency.Lib.Value() + ""+"" + Altium.HotReloadTests.OwnLibrary.Lib.Value() + "":"" + env[""Generation""]; }",
                dependency, ownAltium);

            Assert.Equal("dep1+own1:1", OnUi(() => _host.Invoke("x", "", null)));

            // Loaded from bytes: the build can overwrite it. Only the dependency changes, not the module.
            Thread.Sleep(50);
            WriteLibrary("TestDependency", "dep2");
            Assert.Equal("dep2+own1:2", OnUi(() => _host.Invoke("x", "", null)));
            Assert.Null(_host.LastError);
        }

        // RID-specific assets live under runtimes\<rid>\lib\<tfm>\ and are found through the module's .deps.json.
        [Fact]
        public void RID_specific_dependency_is_resolved_through_the_deps_json()
        {
            var relative = "runtimes/win/lib/net8.0/RidDependency.dll";
            var built = WriteLibrary("RidDependency", "rid1");
            var target = Path.Combine(_directory, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(target));
            File.Move(built, target);
            File.WriteAllText(Path.Combine(_directory, "TestModule.deps.json"), $$"""
                {
                  "runtimeTarget": { "name": ".NETCoreApp,Version=v8.0" },
                  "targets": {
                    ".NETCoreApp,Version=v8.0": {
                      "TestModule/1.0.0": { "dependencies": { "RidDependency": "1.0.0" }, "runtime": { "TestModule.dll": {} } },
                      "RidDependency/1.0.0": { "runtimeTargets": { "{{relative}}": { "rid": "win", "assetType": "runtime" } } }
                    }
                  },
                  "libraries": {
                    "TestModule/1.0.0": { "type": "project", "serviceable": false, "sha512": "" },
                    "RidDependency/1.0.0": { "type": "project", "serviceable": false, "sha512": "" }
                  }
                }
                """);
            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) =>
                    RidDependency.Lib.Value(); }", target);

            Assert.Equal("rid1", OnUi(() => _host.Invoke("x", "", null)));
        }

        [Fact]
        public void Pipe_accept_failures_back_off_log_once_and_show_in_status()
        {
            var log = new ConcurrentQueue<string>();
            // "anonymous" is reserved: creating the server pipe always fails.
            using var server = new DevPipeServer("anonymous", (_, __) => Task.FromResult(0), log.Enqueue);
            Thread.Sleep(3500); // attempts at about 0 s, 1 s, 3 s
            Assert.NotNull(server.LastError);
            Assert.Single(log, l => l.StartsWith("Pipe accept failed"));
        }

        [Fact]
        public void Client_script_validates_the_verb_before_looking_for_an_instance()
        {
            var (exitCode, output) = RunClient("-Name NoSuchExtension bogus");
            Assert.Equal(2, exitCode);
            Assert.Contains("Unknown verb", output);

            (exitCode, output) = RunClient("-Name NoSuchExtension run");
            Assert.Equal(2, exitCode);

            (exitCode, output) = RunClient("help");
            Assert.Equal(0, exitCode);
            Assert.Contains("Verbs", output);

            (exitCode, output) = RunClient("-Name NoSuchExtension status");
            Assert.Equal(3, exitCode);
            Assert.Contains("deploy.ps1", output);
        }

        [Fact]
        public void Client_script_times_out_when_the_UI_thread_is_busy()
        {
            WriteModule(Usings + @"public static class HotEntry { public static string Invoke(string c, string p, IDictionary<string, object> env) => ""v1""; }");
            using var release = new ManualResetEventSlim();
            var blocked = _host.OnUi(() => release.Wait(TimeSpan.FromSeconds(20)));
            try
            {
                var timer = Stopwatch.StartNew();
                var (exitCode, output) = RunClient($"-Name {_name} status -ReadTimeoutMs 1000");
                Assert.Equal(3, exitCode);
                Assert.Contains("No response", output);
                Assert.True(timer.Elapsed < TimeSpan.FromSeconds(15), "the client should not wait for the UI thread");

                // ping is answered off the UI thread.
                (exitCode, output) = RunClient($"-Name {_name} ping");
                Assert.Equal(0, exitCode);
            }
            finally
            {
                release.Set();
                Result(blocked);
            }
        }

        public void Dispose()
        {
            try
            {
                OnUi(() =>
                {
                    _host.Dispose();
                    Application.ExitThread();
                    return 0;
                });
            }
            catch (TimeoutException) { }
            _uiThread.Join(TimeSpan.FromSeconds(30));
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private T OnUi<T>(Func<T> func) => Result(_host.OnUi(func));

        private static T Result<T>(Task<T> task)
        {
            if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The UI thread did not respond.");
            return task.GetAwaiter().GetResult();
        }

        private static void WaitFor(Func<bool> condition, string message)
        {
            var timeout = Stopwatch.StartNew();
            while (!condition())
            {
                Assert.True(timeout.Elapsed < TimeSpan.FromSeconds(15), message);
                Thread.Sleep(20);
            }
        }

        private void WriteModule(string source, params string[] references) =>
            File.WriteAllBytes(_modulePath, Compile(source, references));

        private string WriteLibrary(string assemblyName, string value)
        {
            var path = Path.Combine(_directory, assemblyName + ".dll");
            File.WriteAllBytes(path, Compile($"namespace {assemblyName} {{ public static class Lib {{ public static string Value() => \"{value}\"; }} }}",
                assemblyName, Array.Empty<string>()));
            return path;
        }

        private static byte[] Compile(string source, params string[] references) => Compile(source, "TestModule", references);

        private static byte[] Compile(string source, string assemblyName, string[] references)
        {
            var runtime = Path.GetDirectoryName(typeof(object).Assembly.Location);
            var all = new[]
                {
                    typeof(object).Assembly.Location,
                    Path.Combine(runtime, "System.Runtime.dll"),
                    Path.Combine(runtime, "System.Collections.dll"),
                    Path.Combine(runtime, "System.Threading.dll"),
                    Path.Combine(runtime, "System.Threading.Thread.dll"),
                    Path.Combine(runtime, "System.Threading.Tasks.dll"),
                }
                .Concat(references).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(p => (MetadataReference)MetadataReference.CreateFromImage(File.ReadAllBytes(p)));
            var compilation = CSharpCompilation.Create(assemblyName,
                new[] { CSharpSyntaxTree.ParseText(source) }, all,
                new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
            using var image = new MemoryStream();
            var result = compilation.Emit(image);
            Assert.True(result.Success, string.Join(Environment.NewLine, result.Diagnostics));
            return image.ToArray();
        }

        private static (int ExitCode, string Output) RunClient(string arguments)
        {
            var script = Path.Combine(AppContext.BaseDirectory, "hotreload.ps1");
            var start = new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{script}\" {arguments}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var process = Process.Start(start);
            var stderr = process.StandardError.ReadToEndAsync();
            var output = process.StandardOutput.ReadToEnd() + stderr.Result;
            process.WaitForExit();
            return (process.ExitCode, output);
        }
    }
}
