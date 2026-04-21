using System;
using System.ComponentModel;
using System.Collections.Generic;
using EnvDTE;
using Microsoft.VisualStudio.Shell;

namespace data_foundry.Options
{
    public class SqlProjectListConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext context) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext context) => true;

        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var environment = Package.GetGlobalService(typeof(DTE)) as DTE;
            var list = new List<string>();
            if (environment?.Solution?.Projects != null)
            {
                foreach (Project project in environment.Solution.Projects)
                {
                    if (project != null && !string.IsNullOrEmpty(project.FullName) &&
                        project.FullName.ToLowerInvariant().EndsWith(WTW.Diffusion.Core.Constants.FileExtensions.SqlProj))
                    {
                        list.Add(project.Name);
                    }
                }
            }
            return new StandardValuesCollection(list);
        }
    }
}
