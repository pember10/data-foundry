using System;
using System.ComponentModel;

namespace data_foundry.Models
{
    /// <summary>
    /// Represents an activity/operation performed in the Data Foundry extension.
    /// </summary>
    public class ActivityEntry : INotifyPropertyChanged
    {
        private ActivityStatus _status;
        private string _message;
        private string _details;
        private TimeSpan? _duration;

        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Unique identifier for the activity.
        /// </summary>
        public Guid Id { get; set; }

        /// <summary>
        /// Timestamp when the activity occurred.
        /// </summary>
        public DateTime Timestamp { get; set; } = DateTime.Now;

        /// <summary>
        /// Type of operation performed.
        /// </summary>
        public ActivityType OperationType { get; set; }

        /// <summary>
        /// Current status of the activity.
        /// </summary>
        public ActivityStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        /// <summary>
        /// Activity message.
        /// </summary>
        public string Message
        {
            get => _message;
            set
            {
                if (_message != value)
                {
                    _message = value;
                    OnPropertyChanged(nameof(Message));
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        /// <summary>
        /// Additional details about the activity.
        /// </summary>
        public string Details
        {
            get => _details;
            set
            {
                if (_details != value)
                {
                    _details = value;
                    OnPropertyChanged(nameof(Details));
                }
            }
        }

        /// <summary>
        /// Duration of the activity (if completed).
        /// </summary>
        public TimeSpan? Duration
        {
            get => _duration;
            set
            {
                if (_duration != value)
                {
                    _duration = value;
                    OnPropertyChanged(nameof(Duration));
                    OnPropertyChanged(nameof(DisplayText));
                }
            }
        }

        /// <summary>
        /// Display text for the UI (formatted with timestamp and duration).
        /// </summary>
        public string DisplayText
        {
            get
            {
                var text = $"[{Timestamp:HH:mm:ss}] {GetOperationTypeName()} - {Message}";
                if (Duration.HasValue && Status != ActivityStatus.InProgress)
                {
                    text += $" ({Duration.Value.TotalSeconds:F1}s)";
                }
                return text;
            }
        }

        private string GetOperationTypeName()
        {
            switch (OperationType)
            {
                case ActivityType.Deployment:
                    return "Deployment";
                case ActivityType.ChangeDetection:
                    return "Change Detection";
                case ActivityType.MigrationGeneration:
                    return "Script Generation";
                default:
                    return "Operation";
            }
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    /// <summary>
    /// Types of activities that can be tracked.
    /// </summary>
    public enum ActivityType
    {
        Deployment,
        ChangeDetection,
        DatabaseComparison,
        MigrationGeneration,
        SchemaValidation,
        Revert,
        Other
    }

    /// <summary>
    /// Status of an activity.
    /// </summary>
    public enum ActivityStatus
    {
        Success,
        Failed,
        Warning,
        InProgress,
        Cancelled
    }
}
