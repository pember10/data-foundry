using EnvDTE;
using Microsoft.VisualStudio.Shell;
using WTW.Diffusion.Core.Abstractions;

namespace data_foundry.Services.Adapters
{
    /// <summary>
    /// IProjectManager adapter for the Visual Studio host.
    /// Delegates to ProjectFileManager which uses the DTE API.
    /// </summary>
    public class VsProjectManager : IProjectManager
    {
        private readonly ProjectFileManager _inner;

        public VsProjectManager(DTE environment)
        {
            _inner = new ProjectFileManager(environment);
        }

        public bool AddFileToProject(string projectName, string filePath, string folderPath = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inner.AddFileToProject(projectName, filePath, folderPath);
        }

        public string GetProjectPath(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _inner.GetProjectPath(projectName);
        }

        public string GetRelativeFolderPath(string projectPath, string filePath)
        {
            return ProjectFileManager.GetRelativeFolderPath(projectPath, filePath);
        }
    }
}
