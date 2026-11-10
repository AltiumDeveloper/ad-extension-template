using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Xunit;

namespace Altium.Designer.HotReload.Tests
{
    /// <summary>
    /// Each test hosts a real <see cref="HotReloadHost"/> on its own STA thread with a message loop
    /// (standing in for AD's UI thread) and a tiny module compiled on the fly.
    /// </summary>
    public sealed class HotReloadHostTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "Altium.Designer.HotReload.Tests", Guid.NewGuid().ToString("N"));
        private readonly string _name = "HotReloadTest" + Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string _modulePath;
        private readonly Thread _uiThread;
        private HotReloadHost _host;

        public HotReloadHostTests()
        {
            Directory.CreateDirectory(_directory);
            _modulePath = Path.Combine(_directory, "TestModule.dll");
            WriteModule("v1");

            using var ready = new ManualResetEventSlim();
            _uiThread = new Thread(() =>
            {
                _host = HotReloadHost.Start(_name, _modulePath);
                ready.Set();
                Application.Run();
            });
            _uiThread.SetApartmentState(ApartmentState.STA);
            _uiThread.Start();
            ready.Wait();
        }

        [Fact]
        public void Loads_invokes_and_swaps_generations_and_collects_the_old_one()
        {
            Assert.Equal("v1:hi:1", Invoke("hi"));

            WriteModule("v2");
            Assert.Equal("v2:hi:2", Invoke("hi"));

            Assert.Empty(OnUi(() => _host.PendingUnload(collect: true)));
        }

        [Fact]
        public void Bad_build_is_rejected_and_the_old_generation_keeps_running()
        {
            Assert.Equal("v1:x:1", Invoke("x"));

            File.WriteAllBytes(_modulePath, Compile("public static class NotTheEntry { }"));
            var error = Assert.Throws<HotReloadException>(() => OnUi(() => _host.Reload()));
            Assert.Contains("HotEntry", error.Message);

            Assert.Equal("v1:x:1", Invoke("x"));
            Assert.Equal(1, _host.Generation);
        }

        [Fact]
        public void Pipe_run_fails_while_the_latest_build_is_rejected()
        {
            Assert.Equal("v1:x:1", Invoke("x"));

            // The file watcher, not the pipe request, sees the bad build first.
            File.WriteAllBytes(_modulePath, Compile("public static class NotTheEntry { }"));
            var timeout = Stopwatch.StartNew();
            while (_host.LastError == null && timeout.Elapsed < TimeSpan.FromSeconds(10))
                Thread.Sleep(50);

            var (exitCode, output) = RunClient($"-Name {_name} run Echo hello");
            Assert.Equal(1, exitCode);
            Assert.Contains("rejected", output);

            WriteModule("v2");
            (exitCode, output) = RunClient($"-Name {_name} run Echo hello");
            Assert.Equal(0, exitCode);
            Assert.Contains("v2:hello:2", output);
        }

        [Fact]
        public void Rebuilding_the_file_reloads_without_a_call()
        {
            Invoke("");
            WriteModule("v2");

            var timeout = Stopwatch.StartNew();
            while (_host.Generation < 2 && timeout.Elapsed < TimeSpan.FromSeconds(10))
                Thread.Sleep(50);
            Assert.Equal(2, _host.Generation);
        }

        [Fact]
        public void Client_script_finds_the_instance_and_runs_a_command_over_the_pipe()
        {
            Assert.EndsWith($".HotReload.{Environment.ProcessId}", _host.PipeName);

            var (exitCode, output) = RunClient($"-Name {_name} run Echo hello");
            Assert.Equal(0, exitCode);
            Assert.Contains("v1:hello:1", output);

            (exitCode, output) = RunClient($"-Name {_name} list");
            Assert.Equal(0, exitCode);
            Assert.Contains("Echo\tEchoes its parameters", output);
        }

        public void Dispose()
        {
            OnUi(() =>
            {
                _host.Dispose();
                Application.ExitThread();
                return 0;
            });
            _uiThread.Join();
            try { Directory.Delete(_directory, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        private string Invoke(string parameters) => OnUi(() => _host.Invoke("Echo", parameters, null));

        private T OnUi<T>(Func<T> func)
        {
            var task = _host.OnUi(func);
            if (!((IAsyncResult)task).AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(30)))
                throw new TimeoutException("The UI thread did not respond.");
            return task.GetAwaiter().GetResult();
        }

        private void WriteModule(string version) => File.WriteAllBytes(_modulePath, Compile($$"""
            using System.Collections.Generic;
            public static class HotEntry
            {
                public static string Invoke(string command, string parameters, IDictionary<string, object> env) =>
                    "{{version}}:" + parameters + ":" + env["Generation"];
                public static string[] GetCommands() => new[] { "Echo\tEchoes its parameters" };
            }
            """));

        private static byte[] Compile(string source)
        {
            var compilation = CSharpCompilation.Create("TestModule",
                new[] { CSharpSyntaxTree.ParseText(source) },
                new[] { MetadataReference.CreateFromFile(typeof(object).Assembly.Location) },
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
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
    }
}
