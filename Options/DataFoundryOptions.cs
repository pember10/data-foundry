using System;
using System.ComponentModel;
using System.Drawing.Design;
using System.Linq;
using Microsoft.VisualStudio.Shell;
using static data_foundry.Constants;

namespace data_foundry.Options
{
    public class DataFoundryOptions : DialogPage
    {
        [Editor(typeof(ConnectionStringEditor), typeof(UITypeEditor))]
        [DefaultValue("Data Source=(LocalDb)\\Core;Initial Catalog=Core.Db;Integrated Security=True")]
        [Category("Database")]
        [DisplayName("Local Database Connection")]
        [Description("The connection for the database to use in your development environment.")]
        public string LocalDatabaseConnection { get; set; }

        [Editor(typeof(ConnectionStringEditor), typeof(UITypeEditor))]
        [Category("Database")]
        [DisplayName("Shadow Database Connection")]
        [Description("The connection for the shadow database to use in your development environment. Unless explicitly set, the shadow database connection will be derived from the source database connection.")]
        public string ShadowDatabaseConnection { get; set; }

        [DefaultValue(false)]
        [Category("General")]
        [DisplayName("Auto-refresh changes on solution open")]
        [Description("Automatically detect database changes when the solution is opened. When disabled, you can manually refresh using the Changes tab.")]
        public bool AutoRefresh { get; set; }

        [DefaultValue(true)]
        [Category("General")]
        [DisplayName("Show notifications on deployment completion")]
        [Description("Show notifications when deployment completes.")]
        public bool ShowNotifications { get; set; }

        [DefaultValue(false)]
        [Category("General")]
        [DisplayName("Enable verbose logging")]
        [Description("Enable verbose logging for troubleshooting.")]
        public bool VerboseLogging { get; set; }

        [DefaultValue("RemoteSigned")]
        [Category("General")]
        [DisplayName("Execution Policy")]
        [Description("PowerShell execution policy.")]
        public string ExecutionPolicy { get; set; }

        [TypeConverter(typeof(SqlProjectListConverter))]
        [DefaultValue("Api.Db")]
        [Category("Deployment")]
        [DisplayName("SQL Project")]
        [Description("The currently selected SQL project.")]
        public string SqlProject { get; set; }

        [TypeConverter(typeof(MigrationsFolderListConverter))]
        [DefaultValue("Migrations")]
        [Category("Deployment")]
        [DisplayName("Migrations Folder")]
        [Description("The root folder where the Migration scripts are located.")]
        public string MigrationsFolder { get; set; }

        [DefaultValue("Programmables")]
        [Category("Deployment")]
        [DisplayName("Programmables Folder")]
        [Description("The root folder where the Programmables scripts are located.")]
        public string ProgrammablesFolder { get; set; }

        [DefaultValue("UnsupportedProgrammables")]
        [Category("Deployment")]
        [DisplayName("Unsupported Programmables Folder")]
        [Description("The root folder where the Unsupported Programmables scripts are located.")]
        public string UnsupportedProgrammablesFolder { get; set; }

        [DefaultValue("PreDeployment")]
        [Category("Deployment")]
        [DisplayName("Pre-Deployment Folder")]
        [Description("The root folder where the Pre-Deployment scripts are located.")]
        public string PreDeploymentFolder { get; set; }

        [DefaultValue("PostDeployment")]
        [Category("Deployment")]
        [DisplayName("Post-Deployment Folder")]
        [Description("The root folder where the Post-Deployment scripts are located.")]
        public string PostDeploymentFolder { get; set; }

        [DefaultValue("dbo")]
        [Category("Deployment")]
        [DisplayName("Root Schema Folder")]
        [Description("The root folder where the offline schema models are located.")]
        public string RootSchemaFolder { get; set; }

        [DefaultValue(false)]
        [Category("Deployment")]
        [DisplayName("Skip Baseline Check")]
        [Description("Allows builds to succeed even if the target database contains data and no baseline script is present. For best results, provide a baseline script or configure filters so the target is treated as empty, rather than disabling this check.")]
        public bool SkipBaselineCheck { get; set; }

        [Category("Build Options")]
        [DisplayName("SQL Compatibility Version")]
        [Description("SQL Server version that scripts need to be compatible with. This will be derived from the target if baselining is performed.")]
        public string SqlCompatibilityVersion { get; set; }

        [DefaultValue(false)]
        [Category("Build Options")]
        [DisplayName("Fail Build On Errors")]
        [Description("When enabled, the build will fail if constraint naming or script containment issues are detected during verify and build. When disabled, these issues will only generate warnings.")]
        public bool FailBuildOnErrors { get; set; }

        [DefaultValue(false)]
        [Category("Build Options")]
        [DisplayName("Generate Package Script")]
        [Description("Generates a package script during a build.")]
        public bool GeneratePackageScript { get; set; }

        [Category("Change Detection")]
        [DisplayName("Tracked Tables")]
        [Description("Comma-separated list of tables to track for data changes (e.g., Users,Orders,Products). Changes to the extension's tablelist.json file.")]
        [Editor(typeof(System.ComponentModel.Design.MultilineStringEditor), typeof(UITypeEditor))]
        public string TrackedTables
        {
            get
            {
                // Read from extension's tablelist.json
                var config = Config.DataFoundryConfig.LoadTableList();
                return config.Tables != null ? string.Join(", ", config.Tables) : string.Empty;
            }
            set
            {
                // Write to extension's tablelist.json
                var tables = value?.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(t => t.Trim())
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .ToList() ?? new System.Collections.Generic.List<string>();

                var config = new Config.TableListConfig { Tables = tables };
                
                var assemblyPath = typeof(Config.TableListConfig).Assembly.Location;
                var installDir = System.IO.Path.GetDirectoryName(assemblyPath);
                var configPath = System.IO.Path.Combine(installDir, Folders.Config, "tablelist.json");

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(config, Newtonsoft.Json.Formatting.Indented);
                
                // Ensure directory exists
                var directory = System.IO.Path.GetDirectoryName(configPath);
                if (!System.IO.Directory.Exists(directory))
                    System.IO.Directory.CreateDirectory(directory);

                System.IO.File.WriteAllText(configPath, json);
            }
        }

        protected override void OnApply(PageApplyEventArgs e)
        {
            base.OnApply(e);
            
            // Notify all tabs that settings have been saved
            if (e.ApplyBehavior == ApplyKind.Apply)
            {
                Services.SettingsChangedService.Instance.NotifySettingsChanged();
                Services.DatabaseSyncStatusService.Instance.NotifySyncStatusChanged();
            }
        }
    }
}
