using System;
using System.Collections.Generic;
using System.Management.Automation;
using System.Threading.Tasks;

namespace data_foundry.Services
{
    /// <summary>
    /// Executes PowerShell scripts and captures output.
    /// </summary>
    public class PowerShellScriptRunner
    {
        private readonly string _scriptPath;

        public PowerShellScriptRunner(string scriptPath)
        {
            if (string.IsNullOrWhiteSpace(scriptPath))
                throw new ArgumentException("Script path cannot be null or empty", nameof(scriptPath));

            if (!System.IO.File.Exists(scriptPath))
                throw new System.IO.FileNotFoundException($"PowerShell script not found: {scriptPath}");

            _scriptPath = scriptPath;
        }

        public async Task<PowerShellResult> ExecuteAsync(
            Dictionary<string, object> parameters,
            Action<string> logger = null)
        {
            return await Task.Run(() =>
            {
                var result = new PowerShellResult();

                using (var ps = PowerShell.Create())
                {
                    // Add script
                    ps.AddCommand(_scriptPath);

                    // Add parameters
                    if (parameters != null)
                    {
                        foreach (var kvp in parameters)
                        {
                            if (kvp.Value is bool boolValue && boolValue)
                            {
                                // Add as switch parameter
                                ps.AddParameter(kvp.Key);
                            }
                            else if (kvp.Value != null)
                            {
                                ps.AddParameter(kvp.Key, kvp.Value);
                            }
                        }
                    }

                    // Stream output
                    ps.Streams.Information.DataAdded += (s, e) =>
                    {
                        var message = ps.Streams.Information[e.Index].MessageData?.ToString();
                        if (!string.IsNullOrEmpty(message))
                        {
                            result.Output.Add(message);
                            logger?.Invoke(message);
                        }
                    };

                    ps.Streams.Warning.DataAdded += (s, e) =>
                    {
                        var message = ps.Streams.Warning[e.Index].Message;
                        result.Output.Add($"WARNING: {message}");
                        logger?.Invoke($"WARNING: {message}");
                    };

                    ps.Streams.Error.DataAdded += (s, e) =>
                    {
                        var error = ps.Streams.Error[e.Index].ToString();
                        result.Errors.Add(error);
                        logger?.Invoke($"ERROR: {error}");
                    };

                    // Execute
                    var results = ps.Invoke();

                    // Capture output objects
                    foreach (var item in results)
                    {
                        var text = item?.ToString();
                        if (!string.IsNullOrEmpty(text))
                        {
                            result.Output.Add(text);
                            logger?.Invoke(text);
                        }
                    }

                    // Capture any errors
                    if (ps.HadErrors)
                    {
                        foreach (var error in ps.Streams.Error)
                        {
                            var errorText = error.ToString();
                            if (!result.Errors.Contains(errorText))
                            {
                                result.Errors.Add(errorText);
                            }
                        }
                    }

                    result.Success = result.Errors.Count == 0;
                }

                return result;
            });
        }
    }
}
