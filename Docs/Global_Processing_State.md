# Global Processing State & Cancellation

## Overview

The Data Foundry extension implements a global processing state management system that prevents concurrent operations and provides cancellation support.

## Features

### 1. **Global Operation Lock**
- **Only one operation can run at a time** across all tabs (Overview, Changes, Deployment)
- Prevents conflicts between simultaneous deployments, change detections, or script generations
- User-friendly notification when attempting to start a new operation while one is running

### 2. **Cancel Button**
- A **red "Cancel" button** appears next to the loading indicator during any operation
- Allows users to request cancellation of long-running operations
- Available across all tabs consistently

### 3. **Automatic UI Updates**
- All tabs automatically disable their buttons when ANY operation is running
- Loading indicators appear automatically based on global state
- Buttons re-enable when the operation completes or is cancelled

## Architecture

### GlobalProcessingStateService

Located: `Services/GlobalProcessingStateService.cs`

**Singleton Pattern:**
```csharp
GlobalProcessingStateService.Instance
```

**Key Methods:**
- `TryStartProcessing(string operationName)` - Attempts to start an operation; returns false if another is running
- `CompleteProcessing()` - Marks the current operation as complete
- `CancelOperation()` - Requests cancellation via CancellationToken
- `CancellationToken` - Get the cancellation token for the current operation

**Events:**
- `ProcessingStateChanged` - Raised when processing state changes (start, complete, cancel)

## Usage Example

### Starting an Operation

```csharp
if (!GlobalProcessingStateService.Instance.TryStartProcessing("Detecting changes..."))
{
    MessageBox.Show("Another operation is currently in progress...");
    return;
}

try
{
    // Perform operation...
    await DetectChangesAsync();
}
finally
{
    GlobalProcessingStateService.Instance.CompleteProcessing();
}
```

### Supporting Cancellation

To make your operation cancellable, check the cancellation token periodically:

```csharp
var cancellationToken = GlobalProcessingStateService.Instance.CancellationToken;

// Option 1: Pass to async methods
await Task.Run(() => {
    // Your work here
}, cancellationToken);

// Option 2: Manual checks
if (cancellationToken.IsCancellationRequested)
{
    throw new OperationCanceledException("Operation was cancelled by user");
}
```

## Implementation in Tabs

All three tabs (Overview, Changes, Deployment) implement the same pattern:

### Constructor Setup

```csharp
public OverviewTabControl()
{
    // ...
    CancelButton.Click += CancelButton_Click;
    GlobalProcessingStateService.Instance.ProcessingStateChanged += OnProcessingStateChanged;
    UpdateButtonStates();
}
```

### Cancel Button Handler

```csharp
private void CancelButton_Click(object sender, RoutedEventArgs e)
{
    GlobalProcessingStateService.Instance.CancelOperation();
}
```

### State Changed Handler

```csharp
private void OnProcessingStateChanged(object sender, ProcessingStateChangedEventArgs e)
{
    ThreadHelper.JoinableTaskFactory.Run(async () =>
    {
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
        UpdateButtonStates();
        
        if (e.IsProcessing)
        {
            ShowLoadingIndicator(e.CurrentOperation);
        }
        else
        {
            HideLoadingIndicator();
        }
    });
}
```

## UI Elements

### Cancel Button (XAML)

```xaml
<Button Content="Cancel" 
        x:Name="CancelButton" 
        Padding="8,4" 
        Background="#F44336" 
        Foreground="White" 
        BorderThickness="0" 
        Cursor="Hand"/>
```

**Styling:**
- Red background (#F44336 - Material Design Red)
- White text
- No border
- Hand cursor on hover
- Appears next to loading spinner

## User Experience

### Before Operation Starts:
- All buttons enabled
- No loading indicator
- User can click any action button

### During Operation:
- Loading spinner appears with operation name
- All buttons disabled across ALL tabs
- Red "Cancel" button visible
- User can only cancel or wait

### After Operation:
- Loading indicator disappears
- All buttons re-enabled
- User can start new operations

### When Cancellation Requested:
- CancellationToken is triggered
- Operation should clean up and exit gracefully
- UI returns to ready state
- Activity history updated (if tracked)

## Best Practices

### 1. Always Use Try-Finally

```csharp
if (!GlobalProcessingStateService.Instance.TryStartProcessing("..."))
    return;

try
{
    // Your operation
}
finally
{
    GlobalProcessingStateService.Instance.CompleteProcessing();
}
```

### 2. Check Cancellation Token Periodically

```csharp
for (int i = 0; i < 1000; i++)
{
    cancellationToken.ThrowIfCancellationRequested();
    // Do work
}
```

### 3. Clean Up on Cancellation

```csharp
catch (OperationCanceledException)
{
    // Clean up resources
    logger?.Invoke("Operation cancelled by user");
}
```

## Future Enhancements

Potential improvements to consider:

1. **Progress Percentage** - Show progress bar instead of just spinner
2. **Detailed Status** - Show current step (e.g., "Processing table 3 of 10...")
3. **Operation Queue** - Allow queuing operations instead of blocking
4. **Timeout Protection** - Auto-cancel operations that take too long
5. **Cancel Confirmation** - Ask user to confirm cancellation for destructive operations

## Troubleshooting

### Buttons Stay Disabled
- Check if `CompleteProcessing()` is called in the `finally` block
- Check for unhandled exceptions that might skip cleanup

### Cancel Button Doesn't Work
- Ensure the operation checks `CancellationToken.IsCancellationRequested`
- Verify the operation uses async/await properly
- Long-running synchronous code won't respond to cancellation

### Multiple Operations Start
- Ensure `TryStartProcessing()` return value is checked
- Don't bypass the global state in custom code

## Related Files

- `Services/GlobalProcessingStateService.cs` - Core service
- `Views/Controls/OverviewTabControl.xaml(.cs)` - Overview tab implementation
- `Views/Controls/ChangesTabControl.xaml(.cs)` - Changes tab implementation
- `Views/Controls/DeploymentTabControl.xaml(.cs)` - Deployment tab implementation
- `Services/ActivityHistoryService.cs` - Activity tracking (may use cancellation info)

---

**Last Updated:** 2025-01-11  
**Version:** 1.0
