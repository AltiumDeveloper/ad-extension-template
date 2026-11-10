// Altium.Designer.HotReload (source package, compiled into Debug shims only).
using System;
using System.Reflection;
using System.Reflection.Metadata;

namespace Altium.Designer.HotReload
{
    /// <summary>
    /// Framework libraries (System.Text.Json, System.ComponentModel, ...) cache reflection data per
    /// type, which keeps an unloaded generation alive. They expose the <c>[MetadataUpdateHandler]</c>
    /// <c>ClearCache</c> hook that .NET Hot Reload uses; the host calls it before every unload.
    /// </summary>
    internal static class FrameworkCaches
    {
        public static void Clear(Action<string> log)
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                MetadataUpdateHandlerAttribute[] handlers;
                try { handlers = (MetadataUpdateHandlerAttribute[])assembly.GetCustomAttributes(typeof(MetadataUpdateHandlerAttribute), false); }
                catch { continue; }

                foreach (var handler in handlers)
                {
                    var clear = handler.HandlerType.GetMethod("ClearCache",
                        BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(Type[]) }, null);
                    try { clear?.Invoke(null, new object[] { null }); }
                    catch (Exception ex) { log($"ClearCache {handler.HandlerType.FullName} failed: {ex.InnerException?.Message ?? ex.Message}"); }
                }
            }
        }
    }
}
