using System;
using System.Threading;

namespace data_foundry.Services
{
    /// <summary>
    /// Manages global processing state across all tabs to prevent concurrent operations.
    /// </summary>
    public class GlobalProcessingStateService
    {
        private static readonly Lazy<GlobalProcessingStateService> _instance =
            new Lazy<GlobalProcessingStateService>(() => new GlobalProcessingStateService());

        public static GlobalProcessingStateService Instance => _instance.Value;

        private CancellationTokenSource _cancellationTokenSource;
        private string _currentOperation;
        private readonly object _lock = new object();

        /// <summary>
        /// Event raised when processing state changes.
        /// </summary>
        public event EventHandler<ProcessingStateChangedEventArgs> ProcessingStateChanged;

        /// <summary>
        /// Gets whether any operation is currently processing.
        /// </summary>
        public bool IsProcessing { get; private set; }

        /// <summary>
        /// Gets the current operation description.
        /// </summary>
        public string CurrentOperation => _currentOperation;

        /// <summary>
        /// Gets the cancellation token for the current operation.
        /// </summary>
        public CancellationToken CancellationToken => _cancellationTokenSource?.Token ?? CancellationToken.None;

        private GlobalProcessingStateService()
        {
        }

        /// <summary>
        /// Starts a new processing operation if none is currently running.
        /// </summary>
        /// <param name="operationName">Name/description of the operation</param>
        /// <returns>True if the operation was started, false if another operation is already running</returns>
        public bool TryStartProcessing(string operationName)
        {
            lock (_lock)
            {
                if (IsProcessing)
                    return false;

                IsProcessing = true;
                _currentOperation = operationName;
                _cancellationTokenSource = new CancellationTokenSource();

                RaiseStateChanged();
                return true;
            }
        }

        /// <summary>
        /// Completes the current processing operation.
        /// </summary>
        /// <param name="status">Success, Error, or Cancelled</param>
        /// <param name="message">Optional completion message</param>
        public void CompleteProcessing(ProcessingCompletionStatus status = ProcessingCompletionStatus.Success, string message = null)
        {
            lock (_lock)
            {
                if (!IsProcessing)
                    return;

                IsProcessing = false;
                _currentOperation = null;
                
                _cancellationTokenSource?.Dispose();
                _cancellationTokenSource = null;

                RaiseStateChanged(status, message);
            }
        }

        /// <summary>
        /// Requests cancellation of the current operation.
        /// </summary>
        public void CancelOperation()
        {
            lock (_lock)
            {
                if (!IsProcessing || _cancellationTokenSource == null)
                    return;

                _cancellationTokenSource.Cancel();
                RaiseStateChanged();
            }
        }

        /// <summary>
        /// Gets whether cancellation has been requested for the current operation.
        /// </summary>
        public bool IsCancellationRequested
        {
            get
            {
                lock (_lock)
                {
                    return _cancellationTokenSource?.IsCancellationRequested ?? false;
                }
            }
        }

        private void RaiseStateChanged(ProcessingCompletionStatus? completionStatus = null, string completionMessage = null)
        {
            ProcessingStateChanged?.Invoke(this, new ProcessingStateChangedEventArgs
            {
                IsProcessing = IsProcessing,
                CurrentOperation = _currentOperation,
                IsCancellationRequested = IsCancellationRequested,
                CompletionStatus = completionStatus,
                CompletionMessage = completionMessage
            });
        }
    }

    /// <summary>
    /// Event args for processing state changes.
    /// </summary>
    public class ProcessingStateChangedEventArgs : EventArgs
    {
        public bool IsProcessing { get; set; }
        public string CurrentOperation { get; set; }
        public bool IsCancellationRequested { get; set; }
        public ProcessingCompletionStatus? CompletionStatus { get; set; }
        public string CompletionMessage { get; set; }
    }

    /// <summary>
    /// Status of a completed operation.
    /// </summary>
    public enum ProcessingCompletionStatus
    {
        Success,
        Error,
        Cancelled
    }
}
