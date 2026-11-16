using System.Runtime.InteropServices;
using DXP;
using EDP;

[assembly: ComVisible(true)]
[assembly: Guid("4df6edd3-5f58-4e2e-a221-3b28d4692955")]

namespace CSharpPlugin
{
    // AD finds the extension through CSharpPlugin.PluginFactory: keep these names.
    public interface IPluginFactory
    {
        object InvokePluginFactory(IClient a);
    }

    // AutoDispatch: AD also calls members by name, such as InvokeOutputGenerator for an OutJob outputer
    // (ReportOutput.cs, when the extension was created with --outputer).
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public partial class PluginFactory : IPluginFactory
    {
        public object InvokePluginFactory(IClient a) => new PluginServerModule(a, PluginServerModule.ServerName);
    }

    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public partial class PluginServerModule : ServerModule
    {
        public const string ServerName = "BoardTools";

        public PluginServerModule(IClient argClient, string argModuleName)
            : base(argClient, argModuleName) // calls InitializeCommands, before the code below
        {
            RegisterViews(argClient);
            OnCreated(argClient);
        }

        /// <summary>
        /// Creates the extension's panels (server views), once. Implemented in another part of this class, such
        /// as Panel.cs when the extension was created with --panel; without one, the call is compiled away.
        /// </summary>
        partial void RegisterViews(IClient client);

        /// <summary>
        /// Your own one-time setup in the shim, after <see cref="RegisterViews"/>: for example registering a
        /// notification handler (<c>client.RegisterNotificationHandler(new NotificationHandler(...))</c>, kept in a
        /// field). Code here is never unloaded, so it outlives hot reloads. Implement it in another part of this
        /// class; without one, the call is compiled away.
        /// </summary>
        partial void OnCreated(IClient client);

        /// <summary>
        /// Registers processes of your own, after Run: for example one with its own GetState procedure, for a menu
        /// item that is enabled or visible only in some contexts. Declare each in Installation\*.ins (Command Name).
        /// Implement it in another part of this class; without one, the call is compiled away.
        /// </summary>
        partial void RegisterCommands(CommandLauncher launcher);

        protected override void InitializeCommands()
        {
            // AD calls this on its UI thread, which the hot-reload host needs.
            Commands.Start();

            var launcher = (CommandLauncher)CommandLauncher;
            launcher.RegisterCommand("Run", Commands.Run, Commands.GetState_Always);
#if DEBUG
            launcher.RegisterCommand("Reload", Commands.Reload, Commands.GetState_Always);
            launcher.RegisterCommand("Status", Commands.Status, Commands.GetState_Always);
#endif
            RegisterCommands(launcher);
        }

        protected override IServerDocument NewDocumentInstance(string kind, string fileName) => null;
    }
}
