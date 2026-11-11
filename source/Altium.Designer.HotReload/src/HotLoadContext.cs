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
    /// <item><c>Altium.*</c> assemblies that AD provides (already loaded, or resolvable by the host's
    /// context) resolve to the host's instance, so COM interface types unify. An <c>Altium.*</c>
    /// assembly AD doesn't have (the extension's own library) loads privately like any other dependency.</item>
    /// <item>Private dependencies are located with the module's <c>.deps.json</c>
    /// (<see cref="AssemblyDependencyResolver"/>: RID-specific assets, satellite assemblies), falling back to
    /// <c>&lt;moduleDir&gt;\&lt;name&gt;.dll</c>. Managed ones are loaded <b>from bytes</b>: no file locks,
    /// the build can overwrite them while AD runs.</item>
    /// <item>Native DLLs are resolved the same way but loaded from their path (Windows cannot load them
    /// from memory): they stay locked and loaded until AD exits.</item>
    /// <item>Everything else (BCL, WinForms, WPF) falls back to the default context.</item>
    /// </list>
    /// </summary>
    internal sealed class HotLoadContext : AssemblyLoadContext
    {
        private readonly string _directory;
        private readonly AssemblyLoadContext _host;
        private readonly AssemblyDependencyResolver _resolver;

        public HotLoadContext(string name, string modulePath, AssemblyLoadContext host)
            : base(name, isCollectible: true)
        {
            _directory = Path.GetDirectoryName(modulePath);
            _host = host;
            try { _resolver = new AssemblyDependencyResolver(modulePath); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException || ex is IOException)
            {
                // No hostpolicy in this process, or an unreadable .deps.json: probe the module folder only.
                ResolverError = ex.Message;
            }
        }

        /// <summary>Why the <c>.deps.json</c> resolver is unavailable (probing falls back to the module folder), or null.</summary>
        public string ResolverError { get; }

        public Assembly LoadMain(string path, byte[] image) => LoadFromBytes(path, image);

        protected override Assembly Load(AssemblyName name)
        {
            if (name.Name.StartsWith("Altium.", StringComparison.OrdinalIgnoreCase))
            {
                // Provided by AD (or the SDK): share the host's instance so types unify.
                var shared = FindInHost(name.Name) ?? TryLoadFromHost(name);
                if (shared != null)
                    return shared;
            }

            var path = ResolveManaged(name);
            return path != null ? LoadFromBytes(path, ReadAllBytes(path)) : null;
        }

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var path = ResolveNative(unmanagedDllName);
            return path != null ? LoadUnmanagedDllFromPath(path) : IntPtr.Zero;
        }

        private string ResolveManaged(AssemblyName name)
        {
            string path = null;
            try { path = _resolver?.ResolveAssemblyToPath(name); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { }
            if (path != null && File.Exists(path))
                return path;

            var candidate = Path.Combine(_directory, name.Name + ".dll");
            return File.Exists(candidate) ? candidate : null;
        }

        private string ResolveNative(string name)
        {
            string path = null;
            try { path = _resolver?.ResolveUnmanagedDllToPath(name); }
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException) { }
            if (path != null && File.Exists(path))
                return path;

            if (Path.IsPathRooted(name))
                return null;
            foreach (var file in new[] { name, name + ".dll" })
            {
                var candidate = Path.Combine(_directory, file);
                if (File.Exists(candidate))
                    return candidate;
            }
            return null;
        }

        private Assembly TryLoadFromHost(AssemblyName name)
        {
            try { return _host.LoadFromAssemblyName(name); }
            catch (Exception ex) when (ex is FileNotFoundException || ex is FileLoadException || ex is BadImageFormatException)
            {
                return null; // not an AD assembly: the extension's own
            }
        }

        private static Assembly FindInHost(string simpleName) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a =>
                string.Equals(a.GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase) &&
                GetLoadContext(a) is { IsCollectible: false });

        private Assembly LoadFromBytes(string path, byte[] image)
        {
            var pdbPath = Path.ChangeExtension(path, ".pdb");
            byte[] pdb = null;
            try { if (File.Exists(pdbPath)) pdb = ReadAllBytes(pdbPath, attempts: 3); }
            catch (IOException) { /* symbols are optional */ }

            using var imageStream = new MemoryStream(image, writable: false);
            using var pdbStream = pdb != null ? new MemoryStream(pdb, writable: false) : null;
            return LoadFromStream(imageStream, pdbStream);
        }

        /// <summary>
        /// Reads the whole file into memory and closes it at once, so AD never keeps it locked.
        /// Opened with <see cref="FileShare.Read"/> | <see cref="FileShare.Delete"/>: while a writer still has the
        /// file open the open fails with a sharing violation and is retried (up to about 2 s), instead of
        /// reading a half-written image. Throws <see cref="IOException"/> if the file stays locked.
        /// </summary>
        public static byte[] ReadAllBytes(string path, int attempts = 20)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete);
                    var buffer = new byte[stream.Length];
                    stream.ReadExactly(buffer);
                    return buffer;
                }
                catch (IOException ex) when (attempt < attempts && !(ex is FileNotFoundException || ex is DirectoryNotFoundException))
                {
                    Thread.Sleep(100);
                }
            }
        }
    }
}
