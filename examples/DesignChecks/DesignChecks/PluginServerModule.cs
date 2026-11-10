using System.Runtime.InteropServices;
using DXP;
using EDP;

[assembly: ComVisible(true)]
[assembly: Guid("70f6dabf-607e-4205-b6f1-198e97ff74cd")]

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
        public const string ServerName = "DesignChecks";

        public PluginServerModule(IClient argClient, string argModuleName)
            : base(argClient, argModuleName) // calls InitializeCommands, before the code below
        {
            RegisterViews(argClient);
        }

        /// <summary>
        /// Creates the extension's panels (server views), once. Implemented in another part of this class, such
        /// as Panel.cs when the extension was created with --panel; without one, the call is compiled away.
        /// </summary>
        partial void RegisterViews(IClient client);

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
        }

        protected override IServerDocument NewDocumentInstance(string kind, string fileName) => null;
    }
}
