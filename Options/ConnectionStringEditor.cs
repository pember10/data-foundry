using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using Microsoft.Data.ConnectionUI;

namespace data_foundry.Options
{
    public class ConnectionStringEditor : UITypeEditor
    {
        public override UITypeEditorEditStyle GetEditStyle(ITypeDescriptorContext context) => UITypeEditorEditStyle.Modal;

        public override object EditValue(ITypeDescriptorContext context, IServiceProvider provider, object value)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            using (var dialog = new DataConnectionDialog())
            {
                DataSource.AddStandardDataSources(dialog);

                // Set default provider (SQL Server)
                var sqlDataSource = dialog.DataSources.FirstOrDefault(ds => ds.Name == "Microsoft SQL Server");
                if (sqlDataSource != null)
                {
                    dialog.SelectedDataSource = sqlDataSource;
                    var sqlProvider = sqlDataSource.Providers.FirstOrDefault(p => p.Name == "System.Data.SqlClient");
                    if (sqlProvider != null)
                    {
                        dialog.SelectedDataProvider = sqlProvider;
                    }
                }
                // Fallback: if still not set, use first available
                if (dialog.SelectedDataSource == null && dialog.DataSources.Count > 0)
                {
                    var firstDataSource = dialog.DataSources.FirstOrDefault();
                    if (firstDataSource != null)
                    {
                        dialog.SelectedDataSource = firstDataSource;
                    }
                }
                if (dialog.SelectedDataProvider == null && dialog.SelectedDataSource != null && dialog.SelectedDataSource.Providers.Count > 0)
                {
                    var firstProvider = dialog.SelectedDataSource.Providers.FirstOrDefault();
                    if (firstProvider != null)
                    {
                        dialog.SelectedDataProvider = firstProvider;
                    }
                }

                // Only set ConnectionString if both are set
                if (dialog.SelectedDataSource != null && dialog.SelectedDataProvider != null
                    && value is string s && !string.IsNullOrEmpty(s))
                {
                    dialog.ConnectionString = s;
                }

                if (DataConnectionDialog.Show(dialog) == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.ConnectionString;
                }
            }
            return value;
        }
    }
}
