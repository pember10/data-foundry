using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Options
{
    public class MigrationsFolderListConverter : StringConverter
    {
        private static readonly string[] ExcludedFolders = { "Properties", "bin", "obj", "References" };

        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var environment = Package.GetGlobalService(typeof(DTE)) as DTE;
            var list = new List<string>();
            var selectedProject = string.Empty;

            if (context?.Instance is DataFoundryOptions options)
            {
                selectedProject = options.SqlProject;
            }

            if (environment?.Solution?.Projects != null)
            {
                AddProjectFoldersToList(environment?.Solution?.Projects, list, selectedProject);
            }

            return new StandardValuesCollection(list);
        }

        private static void AddProjectFoldersToList(Projects projects, List<string> list, string selectedProject)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            foreach (Project project in projects)
            {
                if (project == null || string.IsNullOrEmpty(project.FullName) ||
                    !project.FullName.ToLowerInvariant().EndsWith(WTW.Diffusion.Core.Constants.FileExtensions.SqlProj))
                {
                    continue;
                }

                if (!string.IsNullOrEmpty(selectedProject) && !string.Equals(project.Name, selectedProject, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (ProjectItem item in project.ProjectItems)
                {
                    if (string.Equals(item.Kind, EnvDTE.Constants.vsProjectItemKindPhysicalFolder, StringComparison.OrdinalIgnoreCase) &&
                        !ExcludedFolders.Any(f =>
                        {
                            ThreadHelper.ThrowIfNotOnUIThread();
                            return string.Equals(f, item.Name, StringComparison.OrdinalIgnoreCase);
                        }))
                    {
                        list.Add(item.Name);
                    }
                }
                break; // Only use the first matching project
            }
        }
    }
}
