// Altium.Designer.HotReload (source package, compiled into Debug shims only).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using System.Threading;

namespace Altium.Designer.HotReload
{
    /// <summary>
    /// Collectible load context for one generation of the implementation module.
    /// <list type="bullet">
    /// <item><c>Altium.*</c> assemblies resolve to the host's instance, so COM interface types unify.</item>
    /// <item>Private dependencies in the module folder are loaded <b>from bytes</b>: no file locks,
    /// the build can overwrite them while AD runs.</item>
    /// <item>Everything else (BCL, WinForms, WPF) falls back to the default context.</item>
    /// </list>
    /// </summary>
    internal sealed class HotLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        private readonly AssemblyLoadContext _host;

        public HotLoadContext(string name, string directory, AssemblyLoadContext host)
            : base(name, isCollectible: true)
        {
            _directory = directory;
            _host = host;
        }

        public Assembly LoadMain(string path, byte[] image) => LoadFromBytes(path, image);

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name.StartsWith("Altium.", StringComparison.OrdinalIgnoreCase))
                return FindInHost(name.Name) ?? _host.LoadFromAssemblyName(name);

            var candidate = Path.Combine(_directory, name.Name + ".dll");
            return File.Exists(candidate) ? LoadFromBytes(candidate, ReadAllBytes(candidate)) : null;
        }

        private static Assembly FindInHost(string simpleName) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase) &&
                GetLoadContext(a) is { IsCollectible: false });

        private Assembly LoadFromBytes(string path, byte[] image)
        {
            var pdbPath = Path.ChangeExtension(path, ".pdb");
            byte[] pdb = null;
            try { if (File.Exists(pdbPath)) pdb = ReadAllBytes(pdbPath); }
            catch (IOException) { /* symbols are optional */ }

            using var imageStream = new MemoryStream(image, writable: false);
            using var pdbStream = pdb != null ? new MemoryStream(pdb, writable: false) : null;
            return LoadFromStream(imageStream, pdbStream);
        }

        /// <summary>Reads without locking; retries while the compiler is still writing the file.</summary>
        public static byte[] ReadAllBytes(string path)
        {
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    var buffer = new byte[stream.Length];
                    stream.ReadExactly(buffer);
                    return buffer;
                }
                catch (IOException) when (attempt < 20)
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
