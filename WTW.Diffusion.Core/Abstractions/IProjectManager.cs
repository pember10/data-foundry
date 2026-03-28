namespace WTW.Diffusion.Core.Abstractions
{
    /// <summary>
    /// Abstraction for project file management functionality.
    /// Implementations can target Visual Studio DTE API, file system, or be no-op for CLI scenarios.
    /// </summary>
    public interface IProjectManager
    {
        /// <summary>
        /// Adds a file to the specified project.
        /// </summary>
        /// <param name="projectName">Name of the project (e.g., SQL project name)</param>
        /// <param name="filePath">Full path to the file to add</param>
        /// <param name="folderPath">Optional folder path within the project (e.g., "Migrations\2025")</param>
        /// <returns>True if file was added successfully, false otherwise</returns>
        bool AddFileToProject(string projectName, string filePath, string folderPath = null);

        /// <summary>
        /// Gets the full path to a project by name.
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>Full path to the project file, or null if not found</returns>
        string GetProjectPath(string projectName);

        /// <summary>
        /// Gets the relative folder path for a file within a project.
        /// </summary>
        /// <param name="projectPath">Full path to the project file</param>
        /// <param name="filePath">Full path to the file</param>
        /// <returns>Relative folder path (e.g., "Migrations\2025") or null</returns>
        string GetRelativeFolderPath(string projectPath, string filePath);
    }
}
