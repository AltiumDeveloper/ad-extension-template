using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace DesignChecks.Impl
{
    /// <summary>Marks a static method <c>string M(CommandContext)</c> or <c>void M(CommandContext)</c> as a hot command.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class HotCommandAttribute : Attribute
    {
        public HotCommandAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        public string Name { get; }
        public string Description { get; }
    }

    public sealed class CommandInfo
    {
        public string Name { get; init; }
        public string Description { get; init; }
        public MethodInfo Method { get; init; }
    }

    /// <summary>Discovers [HotCommand] methods in this assembly. Rebuilt on every load, so adding a command is just adding a method.</summary>
    public static class CommandRegistry
    {
        private static readonly Lazy<IReadOnlyList<CommandInfo>> Commands = new Lazy<IReadOnlyList<CommandInfo>>(Discover);

        public static IReadOnlyList<CommandInfo> All => Commands.Value;

        public static CommandInfo Find(string name) =>
            All.FirstOrDefault(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

        public static string Execute(CommandContext context)
        {
            var command = Find(context.Command)
                          ?? throw new ArgumentException($"Unknown command '{context.Command}'. Available: {string.Join(", ", All.Select(c => c.Name))}");
            try
            {
                return command.Method.Invoke(null, new object[] { context }) as string;
            }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }

        private static IReadOnlyList<CommandInfo> Discover() =>
            LoadableTypes()
                .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
                .Select(m => (Method: m, Attribute: m.GetCustomAttribute<HotCommandAttribute>()))
                .Where(x => x.Attribute != null)
                .Select(x => new CommandInfo { Name = x.Attribute.Name, Description = x.Attribute.Description, Method = x.Method })
                .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        /// <summary>
        /// The types of this assembly. Inside AD all of them load. Outside AD (tests) the Altium SDK assemblies may not
        /// load, for example in an ARM64 process (they are x64): types built on SDK types are then skipped.
        /// </summary>
        private static IEnumerable<Type> LoadableTypes()
        {
            try
            {
                return typeof(CommandRegistry).Assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t != null);
            }
        }
    }
}
