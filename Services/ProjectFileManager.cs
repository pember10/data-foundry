using System;
using System.Collections;
using System.IO;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Services
{
    /// <summary>
    /// Manages adding and removing files from Visual Studio projects.
    /// </summary>
    public class ProjectFileManager
    {
        private readonly DTE _environment;

        public ProjectFileManager(DTE environment)
        {
            _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        }

        /// <summary>
        /// Adds a file to the specified project and folder.
        /// </summary>
        /// <param name="projectName">Name of the SQL project</param>
        /// <param name="filePath">Full path to the file to add</param>
        /// <param name="folderPath">Optional folder path within the project (e.g., "Migrations\2025")</param>
        /// <returns>True if file was added successfully, false otherwise</returns>
        public bool AddFileToProject(string projectName, string filePath, string folderPath = null)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Attempting to add file: {filePath}");
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Target project: {projectName}");
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Target folder: {folderPath ?? "(root)"}");

                if (!File.Exists(filePath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] ERROR: File not found: {filePath}");
                    return false;
                }

                var project = FindProject(projectName);
                if (project == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] ERROR: Project not found: {projectName}");
                    return false;
                }

                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Found project: {project.Name}");

                // If folder path specified, navigate to that folder
                ProjectItems targetItems = project.ProjectItems;
                
                if (!string.IsNullOrWhiteSpace(folderPath))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Navigating to folder: {folderPath}");
                    targetItems = NavigateToFolder(project, folderPath);
                    if (targetItems == null)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] ERROR: Folder not found: {folderPath}");
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Will NOT add to project - folder must exist");
                        return false;  // CHANGED: Don't fallback to root, fail instead
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Successfully navigated to folder");
                    }
                }

                // Check if file already exists in project
                var fileName = Path.GetFileName(filePath);
                if (FileExistsInProjectItems(targetItems, fileName))
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] File already exists in project: {fileName}");
                    return true; // Already added, consider it success
                }

                // Add the file to the project
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Calling AddFromFile...");
                var addedItem = targetItems.AddFromFile(filePath);
                
                if (addedItem != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] SUCCESS: Added file to project: {fileName}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] AddedItem Name: {addedItem.Name}");
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] AddedItem Kind: {addedItem.Kind}");
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] WARNING: AddFromFile returned null");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] ERROR: {ex.Message}");
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Finds a project by name in the current solution.
        /// </summary>
        private Project FindProject(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (_environment?.Solution?.Projects == null)
                return null;

            foreach (Project project in _environment.Solution.Projects)
            {
                try
                {
                    if (project == null || string.IsNullOrEmpty(project.Name))
                        continue;

                    if (string.Equals(project.Name, projectName, StringComparison.OrdinalIgnoreCase))
                    {
                        return project;
                    }
                }
                catch (Exception ex)
                {
                    // Some project types throw NotImplementedException
                    System.Diagnostics.Debug.WriteLine($"Error accessing project: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Navigates to a folder within a project, creating it if necessary.
        /// </summary>
        /// <param name="project">The project</param>
        /// <param name="folderPath">Folder path (e.g., "Migrations\222_Sprint")</param>
        /// <returns>ProjectItems collection for the folder</returns>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Major Code Smell", "S1168:Empty arrays and collections should be returned instead of null", Justification = "ProjectItems is a COM interface with no empty instantiation; null is the correct sentinel and is handled by the caller.")]
        private static ProjectItems NavigateToFolder(Project project, string folderPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (string.IsNullOrWhiteSpace(folderPath))
                return project.ProjectItems;

            var parts = folderPath.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);
            ProjectItems current = project.ProjectItems;

            System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Navigating folder path parts: {string.Join(" -> ", parts)}");
            System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Total project items count: {project.ProjectItems.Count}");

            foreach (var part in parts)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Looking for folder: {part}");
                ProjectItem folder = FindFolderInProjectItems(current, part);

                // If folder doesn't exist, try to create it
                if (folder == null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Folder '{part}' not found in project items");
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] Attempting to add folder to project...");
                    
                    try
                    {
                        folder = current.AddFolder(part);
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] - Successfully added folder: {part}");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager] ERROR: Failed to add folder '{part}': {ex.Message}");
                        return null; // NOSONAR - ProjectItems is a COM interface; null is the correct sentinel for "folder not found" and is handled by the caller
                    }
                }

                current = folder.ProjectItems;
            }

            return current;
        }

        /// <summary>
        /// Finds a folder by name in the given ProjectItems collection.
        /// </summary>
        /// <param name="items">The ProjectItems collection to search.</param>
        /// <param name="folderName">The name of the folder to find.</param>
        /// <returns>The ProjectItem representing the folder, or null if not found.</returns>
        private static ProjectItem FindFolderInProjectItems(ProjectItems items, string folderName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (items == null || items.Count == 0)
            {
                // S1168: Return an empty collection instead of null.
                // But this method returns ProjectItem, not a collection, so returning null is correct here.
                // No change needed for this method.
                return null;
            }

            for (var itemIndex = 0; itemIndex < items.Count; itemIndex++)
            {
                var item = items.Item(itemIndex + 1); // DTE collections are 1-based

                try
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager]   Item {itemIndex}: Name='{item.Name}', Kind='{item.Kind}'");

                    // Check if this is a physical folder by comparing the Kind GUID
                    // vsProjectItemKindPhysicalFolder = "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}"
                    bool isFolder = string.Equals(item.Kind, "{6BB5F8EF-4483-11D3-8BCF-00C04F8EC28C}", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(item.Kind, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase);

                    if (isFolder && string.Equals(item.Name, folderName, StringComparison.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"[ProjectFileManager]   - Found matching folder: {folderName}");
                        return item;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager]   Error reading item {itemIndex}: {ex.Message}");
                }
            }

            return null;
        }

        /// <summary>
        /// Checks if a file already exists in the project items collection.
        /// </summary>
        private static bool FileExistsInProjectItems(ProjectItems items, string fileName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (items == null)
                return false;

            foreach (ProjectItem item in items)
            {
                try
                {
                    if (string.Equals(item.Name, fileName, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    // Some items may throw exceptions when accessing Name
                    System.Diagnostics.Debug.WriteLine($"[ProjectFileManager]   Error reading item {item.Name}: {ex.Message}");
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the full path to a project file by project name.
        /// </summary>
        /// <param name="projectName">Name of the project</param>
        /// <returns>Full path to the project file, or null if not found</returns>
        public string GetProjectPath(string projectName)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var project = FindProject(projectName);
            if (project == null)
                return null;

            try
            {
                return project.FullName;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Gets the relative folder path for a file within a project.
        /// </summary>
        /// <param name="projectPath">Full path to the .sqlproj file</param>
        /// <param name="filePath">Full path to the file</param>
        /// <returns>Relative folder path (e.g., "Migrations\2025") or null</returns>
        public static string GetRelativeFolderPath(string projectPath, string filePath)
        {
            try
            {
                var projectDir = Path.GetDirectoryName(projectPath);
                var fileDir = Path.GetDirectoryName(filePath);

                if (string.IsNullOrEmpty(projectDir) || string.IsNullOrEmpty(fileDir))
                    return null;

                // Make paths absolute and comparable
                projectDir = Path.GetFullPath(projectDir);
                fileDir = Path.GetFullPath(fileDir);

                if (!fileDir.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                    return null; // File is outside project directory

                // Get relative path
                var relativePath = fileDir.Substring(projectDir.Length).TrimStart('\\', '/');
                return string.IsNullOrEmpty(relativePath) ? null : relativePath;
            }
            catch
            {
                return null;
            }
        }
    }
}
