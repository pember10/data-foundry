using System;
using System.IO;
using System.Threading.Tasks;
using EnvDTE;
using Microsoft.VisualStudio.Shell;
using WTW.Diffusion.Core.Abstractions;

namespace data_foundry.Services
{
    /// <summary>
    /// Handles adding generated migration scripts into the Visual Studio SQL project.
    /// Isolates all DTE/VS project interaction related to file registration.
    /// </summary>
    public class ProjectIntegrationService
    {
        private readonly ProjectFileManager _projectFileManager;
        private readonly string _sqlProjectName;
        private readonly ILogger _logger;

        public ProjectIntegrationService(ProjectFileManager projectFileManager, string sqlProjectName, ILogger logger = null)
        {
            _projectFileManager = projectFileManager ?? throw new ArgumentNullException(nameof(projectFileManager));
            _sqlProjectName = sqlProjectName ?? throw new ArgumentNullException(nameof(sqlProjectName));
            _logger = logger;
        }

        /// <summary>
        /// Adds a generated script file to the SQL project asynchronously.
        /// Fire-and-forget safe — logs warnings on failure rather than throwing.
        /// </summary>
        public void AddScriptToProject(string scriptPath)
        {
            _ = AddScriptToProjectAsync(scriptPath);
        }

        private async Task AddScriptToProjectAsync(string scriptPath)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            try
            {
                string projectPath = GetSqlProjectPath(_sqlProjectName);
                if (string.IsNullOrEmpty(projectPath))
                {
                    _logger?.LogWarning($"Could not find project '{_sqlProjectName}' to add script");
                    return;
                }

                string folderPath = ProjectFileManager.GetRelativeFolderPath(projectPath, scriptPath);

                if (_projectFileManager.AddFileToProject(_sqlProjectName, scriptPath, folderPath))
                    _logger?.Log($"Added script to project: {Path.GetFileName(scriptPath)}");
                else
                    _logger?.LogWarning("Could not add script to project (check Debug output for details)");
            }
            catch (Exception ex)
            {
                _logger?.LogWarning($"Error adding script to project: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"ProjectIntegrationService error: {ex}");
            }
        }

        private static string GetSqlProjectPath(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (!(Package.GetGlobalService(typeof(DTE)) is DTE dte))
                    return null;

                foreach (Project project in dte.Solution.Projects)
                {
                    try
                    {
                        if (project == null || string.IsNullOrEmpty(project.FullName))
                            continue;

                        if (!project.FullName.EndsWith(".sqlproj", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
                            return project.FullName;
                    }
                    catch { }
                }
            }
            catch { }

            return null;
        }
    }
}
