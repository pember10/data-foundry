using WTW.Diffusion.Core.Models;
using System;
using System.Collections.Generic;

namespace data_foundry.Services
{
    /// <summary>
    /// Shared service to communicate change detection results between tabs.
    /// </summary>
    public class ChangeDetectionResultsService
    {
        private static readonly Lazy<ChangeDetectionResultsService> _instance =
            new Lazy<ChangeDetectionResultsService>(() => new ChangeDetectionResultsService());

        public static ChangeDetectionResultsService Instance => _instance.Value;

        /// <summary>
        /// Event raised when change detection results are updated.
        /// </summary>
        public event EventHandler<List<TableChangeSummary>> ResultsUpdated;

        /// <summary>
        /// The most recent change detection results.
        /// </summary>
        public List<TableChangeSummary> LatestResults { get; private set; }

        private ChangeDetectionResultsService()
        {
        }

        /// <summary>
        /// Updates the change detection results and notifies subscribers.
        /// </summary>
        public void UpdateResults(List<TableChangeSummary> results)
        {
            LatestResults = results;
            ResultsUpdated?.Invoke(this, results);
        }

        /// <summary>
        /// Clears the current results.
        /// </summary>
        public void ClearResults()
        {
            LatestResults = null;
            ResultsUpdated?.Invoke(this, null);
        }
    }
}
