using DXP;
using EDP;

namespace BoardTools.Impl
{
    /// <summary>Thin helpers over the Altium workspace. Re-query on every call; never cache COM objects across commands.</summary>
    internal static class AltiumSession
    {
        public static IWorkspace Workspace =>
            GlobalVars.DXPWorkSpace as IWorkspace ?? throw new UserException("The Altium workspace is not available.");

        public static IDXPMessagesManager Messages => Workspace.DM_MessagesManager();

        /// <summary>The focused PCB project, compiled. Throws a user-readable error when there is none.</summary>
        public static IProject RequireCompiledProject() => Compiled(Workspace.DM_FocusedProject());

        /// <summary>The open project at <paramref name="path"/> (or the focused one when it is not open), compiled.</summary>
        public static IProject RequireCompiledProject(string path) =>
            Compiled((string.IsNullOrEmpty(path) ? null : Workspace.DM_GetProjectFromPath(path)) ?? Workspace.DM_FocusedProject());

        /// <summary><paramref name="project"/>, compiled if needed. Throws a user-readable error for none or Free Documents.</summary>
        public static IProject Compiled(IProject project)
        {
            if (project == null || Equals(project, Workspace.DM_FreeDocumentsProject()))
                throw new UserException("No project is focused. Select a PCB project (or one of its documents) in the Projects panel.");
            if (project.DM_NeedsCompile())
                project.DM_Compile();
            return project;
        }
    }
}
