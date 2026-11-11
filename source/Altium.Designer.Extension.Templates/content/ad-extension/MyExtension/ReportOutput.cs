using System;
using System.IO;
using System.Runtime.InteropServices;
using Altium.Edp.Classes;
using DXP;

namespace CSharpPlugin
{
    // Installation\MyExtension.out names this class (ClassName) and this assembly (Module): AD calls
    // InvokeOutputGenerator to create the outputer of an OutJob's "MyExtension Report" output.
    public partial class PluginFactory
    {
        public object InvokeOutputGenerator() => new ReportGenerator();
    }

    /// <summary>
    /// The OutJob outputer (Report category). It stays small and stable: AD drives the lifecycle here,
    /// and the content is written by the "Report" command of MyExtension.Impl, which hot-reloads in Debug
    /// builds like any other command. The file name is decided here, because AD asks for it
    /// (InternalPredictOutputFilenames) before generating.
    /// </summary>
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public class ReportGenerator : OutputGenerator
    {
        public const string OutputName = "MyExtension Report"; // = Name in MyExtension.out
        public const string FileExtension = ".txt";

        private string _folder;

        public ReportGenerator() : base(OutputName) { }

        protected override void InternalSetOutputPath(string targetFolder) => _folder = targetFolder;

        // No configuration dialog: accept the OutJob's "Configure" command as is.
        protected override bool InternalRunPropertiesForm() => true;

        protected override void InternalPredictOutputFilenames(IStrings filenames)
        {
            var file = OutputFile();
            if (filenames != null && file != null)
                filenames.Add(file);
        }

        protected override bool InternalRunGenerator()
        {
            var file = OutputFile();
            if (file == null)
            {
                HandleOutputerError(OutputName + ": there is no project to generate the output for.");
                return false;
            }

            Notify_BeginGeneratingOutputFile(file);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file));
                // The project goes to the implementation as the command's context (env "Context").
                Commands.Invoke("Report", "File=" + file, Project);
                return true;
            }
            catch (Exception ex)
            {
                // AD shows the error (a dialog, unless a release is running) and adds it to the Messages panel.
                HandleOutputerError($"{OutputName} failed: {ex.Message}");
                return false;
            }
            finally
            {
                Notify_FinishGeneratingOutputFile(file); // pairs with Begin, whatever the outcome
            }
        }

        /// <summary>[Output folder]\[TargetPrefix]&lt;project name&gt;.txt, or the OutJob's explicit file name.</summary>
        private string OutputFile()
        {
            var projectPath = Project?.DM_ProjectFullPath();
            if (string.IsNullOrEmpty(projectPath))
                return null;

            var name = !string.IsNullOrWhiteSpace(ExplicitTargetFilename)
                ? ExplicitTargetFilename
                : (TargetPrefix ?? string.Empty) + Path.GetFileNameWithoutExtension(projectPath);
            if (!name.EndsWith(FileExtension, StringComparison.OrdinalIgnoreCase))
                name += FileExtension;

            var folder = string.IsNullOrWhiteSpace(_folder) ? Path.GetDirectoryName(projectPath) : _folder;
            return Path.Combine(folder, name);
        }
    }
}
