using WTW.Diffusion.Core.Abstractions;

namespace WTW.Diffusion.Cli.Adapters;

/// <summary>
/// IProjectManager implementation for the CLI.
/// Adds generated script files to the .sqlproj XML directly via the file system —
/// no Visual Studio DTE dependency required.
/// </summary>
internal sealed class FileSystemProjectManager : IProjectManager
{
    private readonly string _solutionRoot;

    /// <param name="solutionRoot">
    /// Root directory to search for .sqlproj files when resolving project names.
    /// Typically the directory that contains the .sln / .slnx file.
    /// </param>
    public FileSystemProjectManager(string solutionRoot)
    {
        _solutionRoot = solutionRoot ?? throw new ArgumentNullException(nameof(solutionRoot));
    }

    /// <inheritdoc/>
    public string? GetProjectPath(string projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
            return null;

        return Directory
            .EnumerateFiles(_solutionRoot, "*.sqlproj", SearchOption.AllDirectories)
            .FirstOrDefault(p =>
                string.Equals(Path.GetFileNameWithoutExtension(p), projectName,
                    StringComparison.OrdinalIgnoreCase));
    }

    /// <inheritdoc/>
    public string? GetRelativeFolderPath(string projectPath, string filePath)
    {
        if (string.IsNullOrWhiteSpace(projectPath) || string.IsNullOrWhiteSpace(filePath))
            return null;

        string projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
        string relative = Path.GetRelativePath(projectDir, filePath);
        return Path.GetDirectoryName(relative);
    }

    /// <summary>
    /// Adds a &lt;Build Include="…" /&gt; entry to the .sqlproj file if not already present.
    /// The project file is standard MSBuild XML so we manipulate it with XDocument.
    /// </summary>
    /// <inheritdoc/>
    public bool AddFileToProject(string projectName, string filePath, string? folderPath = null)
    {
        var projectPath = GetProjectPath(projectName);
        if (projectPath is null)
            return false;

        string projectDir = Path.GetDirectoryName(projectPath) ?? string.Empty;
        string relativePath = Path.GetRelativePath(projectDir, filePath);

        try
        {
            var doc = System.Xml.Linq.XDocument.Load(projectPath);
            var ns = doc.Root?.Name.Namespace ?? System.Xml.Linq.XNamespace.None;

            // Check whether the item is already present.
            bool alreadyIncluded = doc.Descendants(ns + "Build")
                .Any(e => string.Equals(
                    e.Attribute("Include")?.Value,
                    relativePath,
                    StringComparison.OrdinalIgnoreCase));

            if (alreadyIncluded)
                return true;

            // Find or create an ItemGroup to host the new entry.
            var itemGroup = doc.Descendants(ns + "ItemGroup").FirstOrDefault()
                ?? new System.Xml.Linq.XElement(ns + "ItemGroup");

            if (!doc.Descendants(ns + "ItemGroup").Any())
                doc.Root?.Add(itemGroup);

            itemGroup.Add(new System.Xml.Linq.XElement(ns + "Build",
                new System.Xml.Linq.XAttribute("Include", relativePath)));

            doc.Save(projectPath);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
