using System;
using System.Diagnostics;
using Azure.Core;
using Azure.Identity;

namespace WTW.Diffusion.Core.Services.Database
{
    /// <summary>
    /// Provides Azure SQL access token for authentication.
    /// </summary>
    public class AzureSqlAuthenticationProvider
    {
        private readonly DefaultAzureCredential _credential;

        public AzureSqlAuthenticationProvider()
        {
            _credential = new DefaultAzureCredential();
        }

        /// <summary>
        /// Retrieves an Azure SQL access token if the target server is an Azure SQL instance.
        /// </summary>
        /// <param name="targetServer">SQL Server instance name or hostname.</param>
        /// <returns>Access token string, or null if not an Azure SQL instance or if retrieval fails.</returns>
        public string GetAccessToken(string targetServer)
        {
            if (string.IsNullOrWhiteSpace(targetServer) || !targetServer.Contains(Constants.Azure.AzureSqlDomain))
            {
                return null;
            }

            try
            {
                var tokenRequestContext = new TokenRequestContext(new[] { Constants.Azure.DefaultTokenScope });
                var accessToken = _credential.GetToken(tokenRequestContext, default);
                return accessToken.Token;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to acquire Azure SQL access token for server '{targetServer}': {ex.Message}");
            }

            return null;
        }
    }
}
