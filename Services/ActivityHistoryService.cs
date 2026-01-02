using data_foundry.Helpers;
using data_foundry.Models;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using static data_foundry.Constants;

namespace data_foundry.Services
{
    /// <summary>
    /// Manages activity history for Data Foundry operations.
    /// Provides in-memory storage with optional persistence to JSON.
    /// </summary>
    public class ActivityHistoryService
    {
        private static readonly Lazy<ActivityHistoryService> _instance = 
            new Lazy<ActivityHistoryService>(() => new ActivityHistoryService());

        private readonly ObservableCollection<ActivityEntry> _activities;
        private readonly object _lock = new object();
        private readonly int _maxEntries;
        private readonly string _historyFilePath;

        /// <summary>
        /// Singleton instance of the ActivityHistoryService.
        /// </summary>
        public static ActivityHistoryService Instance => _instance.Value;

        /// <summary>
        /// Event raised when a new activity is added.
        /// </summary>
        public event EventHandler<ActivityEntry> ActivityAdded;

        /// <summary>
        /// Gets the observable collection of activities.
        /// </summary>
        public ObservableCollection<ActivityEntry> Activities => _activities;

        private ActivityHistoryService(int maxEntries = 100)
        {
            _maxEntries = maxEntries;
            _activities = new ObservableCollection<ActivityEntry>();

            // Set up history file path
            var installDir = PathHelper.GetExtensionInstallDirectory(typeof(ActivityHistoryService));
            var configDir = PathHelper.SafeCombine(installDir, Folders.Config);
            
            if (!string.IsNullOrEmpty(configDir))
            {
                PathHelper.EnsureDirectoryExists(configDir);
                _historyFilePath = PathHelper.SafeCombine(configDir, "activity-history.json");
            }

            // Load existing history
            LoadHistory();
        }

        /// <summary>
        /// Adds a new activity to the history.
        /// </summary>
        public void AddActivity(ActivityEntry activity)
        {
            if (activity == null)
                return;

            // Check if we need to marshal to UI thread
            if (System.Windows.Application.Current?.Dispatcher != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                // Use SwitchToMainThreadAsync to avoid deadlocks
                Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    AddActivity(activity);
                });
                return;
            }

            lock (_lock)
            {
                // Add to beginning (most recent first)
                _activities.Insert(0, activity);

                // Trim if exceeds max entries
                while (_activities.Count > _maxEntries)
                {
                    _activities.RemoveAt(_activities.Count - 1);
                }

                // Save to file
                SaveHistory();
            }

            // Raise event
            ActivityAdded?.Invoke(this, activity);
        }

        /// <summary>
        /// Creates and adds a success activity.
        /// </summary>
        public void LogSuccess(ActivityType type, string message, string details = null, TimeSpan? duration = null)
        {
            AddActivity(new ActivityEntry
            {
                OperationType = type,
                Status = ActivityStatus.Success,
                Message = message,
                Details = details,
                Duration = duration
            });
        }

        /// <summary>
        /// Creates and adds a failure activity.
        /// </summary>
        public void LogFailure(ActivityType type, string message, string details = null)
        {
            AddActivity(new ActivityEntry
            {
                OperationType = type,
                Status = ActivityStatus.Failed,
                Message = message,
                Details = details
            });
        }

        /// <summary>
        /// Creates and adds a warning activity.
        /// </summary>
        public void LogWarning(ActivityType type, string message, string details = null)
        {
            AddActivity(new ActivityEntry
            {
                OperationType = type,
                Status = ActivityStatus.Warning,
                Message = message,
                Details = details
            });
        }

        /// <summary>
        /// Creates and adds an in-progress activity.
        /// Returns the activity entry so it can be updated later.
        /// </summary>
        public ActivityEntry StartActivity(ActivityType type, string message)
        {
            var activity = new ActivityEntry
            {
                OperationType = type,
                Status = ActivityStatus.InProgress,
                Message = message
            };

            AddActivity(activity);
            return activity;
        }

        /// <summary>
        /// Updates an existing activity's status and details.
        /// </summary>
        public void UpdateActivity(ActivityEntry activity, ActivityStatus status, string details = null, TimeSpan? duration = null)
        {
            if (activity == null)
                return;

            // Ensure we're on the UI thread for property change notifications
            if (System.Windows.Application.Current?.Dispatcher != null &&
                !System.Windows.Application.Current.Dispatcher.CheckAccess())
            {
                Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.Run(async () =>
                {
                    await Microsoft.VisualStudio.Shell.ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                    UpdateActivity(activity, status, details, duration);
                });
                return;
            }

            lock (_lock)
            {
                activity.Status = status;
                
                if (details != null)
                    activity.Details = details;

                if (duration.HasValue)
                    activity.Duration = duration;

                SaveHistory();
            }

            // Raise ActivityAdded event for status changes too
            if (status == ActivityStatus.Success || status == ActivityStatus.Failed || status == ActivityStatus.Cancelled)
            {
                ActivityAdded?.Invoke(this, activity);
            }
        }

        /// <summary>
        /// Gets recent activities filtered by type.
        /// </summary>
        public List<ActivityEntry> GetRecentActivities(int count = 10, ActivityType? filterType = null)
        {
            lock (_lock)
            {
                var query = _activities.AsEnumerable();

                if (filterType.HasValue)
                    query = query.Where(a => a.OperationType == filterType.Value);

                return query.Take(count).ToList();
            }
        }

        /// <summary>
        /// Clears all activity history.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _activities.Clear();
                SaveHistory();
            }
        }

        /// <summary>
        /// Saves the activity history to a JSON file.
        /// </summary>
        private void SaveHistory()
        {
            if (string.IsNullOrEmpty(_historyFilePath))
                return;

            try
            {
                var json = JsonConvert.SerializeObject(_activities, Formatting.Indented);
                File.WriteAllText(_historyFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save activity history: {ex.Message}");
            }
        }

        /// <summary>
        /// Loads the activity history from a JSON file.
        /// </summary>
        private void LoadHistory()
        {
            if (string.IsNullOrEmpty(_historyFilePath) || !File.Exists(_historyFilePath))
                return;

            try
            {
                var json = File.ReadAllText(_historyFilePath);
                var activities = JsonConvert.DeserializeObject<List<ActivityEntry>>(json);

                if (activities != null)
                {
                    lock (_lock)
                    {
                        _activities.Clear();
                        foreach (var activity in activities.Take(_maxEntries))
                        {
                            _activities.Add(activity);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load activity history: {ex.Message}");
            }
        }

        /// <summary>
        /// Gets activity statistics.
        /// </summary>
        public ActivityStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new ActivityStatistics
                {
                    TotalActivities = _activities.Count,
                    SuccessCount = _activities.Count(a => a.Status == ActivityStatus.Success),
                    FailedCount = _activities.Count(a => a.Status == ActivityStatus.Failed),
                    WarningCount = _activities.Count(a => a.Status == ActivityStatus.Warning),
                    LastActivity = _activities.FirstOrDefault()
                };
            }
        }
    }

    /// <summary>
    /// Statistics about activity history.
    /// </summary>
    public class ActivityStatistics
    {
        public int TotalActivities { get; set; }
        public int SuccessCount { get; set; }
        public int FailedCount { get; set; }
        public int WarningCount { get; set; }
        public ActivityEntry LastActivity { get; set; }
    }
}
