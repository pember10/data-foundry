using System;
using System.Threading;
using FluentAssertions;
using Xunit;
using data_foundry.Services;

namespace WTW.Diffusion.Vsix.Tests.Services
{
    /// <summary>
    /// Each test creates a fresh instance to avoid singleton state leaking between tests.
    /// </summary>
    public class GlobalProcessingStateServiceTests : IDisposable
    {
        private readonly GlobalProcessingStateService _svc;

        public GlobalProcessingStateServiceTests()
        {
            _svc = (GlobalProcessingStateService)Activator.CreateInstance(
                typeof(GlobalProcessingStateService), nonPublic: true)!;
        }

        public void Dispose()
        {
            if (_svc.IsProcessing)
                _svc.CompleteProcessing();
        }

        // ?? TryStartProcessing ????????????????????????????????????????????????

        [Fact]
        public void TryStartProcessing_WhenIdle_ReturnsTrueAndSetsIsProcessing()
        {
            var result = _svc.TryStartProcessing("TestOp");

            result.Should().BeTrue();
            _svc.IsProcessing.Should().BeTrue();
            _svc.CurrentOperation.Should().Be("TestOp");
        }

        [Fact]
        public void TryStartProcessing_WhenAlreadyProcessing_ReturnsFalse()
        {
            _svc.TryStartProcessing("First");

            var result = _svc.TryStartProcessing("Second");

            result.Should().BeFalse();
            _svc.CurrentOperation.Should().Be("First");
        }

        // ?? CompleteProcessing ????????????????????????????????????????????????

        [Fact]
        public void CompleteProcessing_AfterStart_SetsIsProcessingFalse()
        {
            _svc.TryStartProcessing("TestOp");
            _svc.CompleteProcessing();

            _svc.IsProcessing.Should().BeFalse();
            _svc.CurrentOperation.Should().BeNull();
        }

        [Fact]
        public void CompleteProcessing_WhenIdle_DoesNotThrow()
        {
            Action act = () => _svc.CompleteProcessing();
            act.Should().NotThrow();
        }

        [Fact]
        public void CompleteProcessing_AllowsNewOperationToStart()
        {
            _svc.TryStartProcessing("First");
            _svc.CompleteProcessing();

            var result = _svc.TryStartProcessing("Second");

            result.Should().BeTrue();
            _svc.CurrentOperation.Should().Be("Second");
        }

        // ?? CancellationToken ?????????????????????????????????????????????????

        [Fact]
        public void CancellationToken_WhenProcessing_IsNotCancelled()
        {
            _svc.TryStartProcessing("TestOp");

            _svc.CancellationToken.IsCancellationRequested.Should().BeFalse();
        }

        [Fact]
        public void CancelOperation_WhenProcessing_SetsCancellationRequested()
        {
            _svc.TryStartProcessing("TestOp");

            _svc.CancelOperation();

            _svc.IsCancellationRequested.Should().BeTrue();
        }

        [Fact]
        public void CancellationToken_WhenIdle_IsNone()
        {
            _svc.CancellationToken.Should().Be(CancellationToken.None);
        }

        // ?? ProcessingStateChanged event ??????????????????????????????????????

        [Fact]
        public void ProcessingStateChanged_FiresOnStart()
        {
            ProcessingStateChangedEventArgs? received = null;
            _svc.ProcessingStateChanged += (_, args) => received = args;

            _svc.TryStartProcessing("TestOp");

            received.Should().NotBeNull();
            received!.IsProcessing.Should().BeTrue();
            received.CurrentOperation.Should().Be("TestOp");
        }

        [Fact]
        public void ProcessingStateChanged_FiresOnComplete()
        {
            _svc.TryStartProcessing("TestOp");
            ProcessingStateChangedEventArgs? received = null;
            _svc.ProcessingStateChanged += (_, args) => received = args;

            _svc.CompleteProcessing(ProcessingCompletionStatus.Success);

            received.Should().NotBeNull();
            received!.IsProcessing.Should().BeFalse();
            received.CompletionStatus.Should().Be(ProcessingCompletionStatus.Success);
        }

        [Fact]
        public void ProcessingStateChanged_FiresOnCancel()
        {
            _svc.TryStartProcessing("TestOp");
            ProcessingStateChangedEventArgs? received = null;
            _svc.ProcessingStateChanged += (_, args) => received = args;

            _svc.CancelOperation();

            received.Should().NotBeNull();
            received!.IsCancellationRequested.Should().BeTrue();
        }
    }
}
