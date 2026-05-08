using System;
using FluentAssertions;
using Xunit;
using data_foundry.Services;

namespace WTW.Diffusion.Vsix.Tests.Services
{
    public class DatabaseSyncStatusServiceTests
    {
        private readonly DatabaseSyncStatusService _svc;

        public DatabaseSyncStatusServiceTests()
        {
            _svc = (DatabaseSyncStatusService)Activator.CreateInstance(
                typeof(DatabaseSyncStatusService), nonPublic: true)!;
        }

        [Fact]
        public void NotifySyncStatusChanged_FiresEvent()
        {
            bool fired = false;
            _svc.SyncStatusChanged += (_, _) => fired = true;

            _svc.NotifySyncStatusChanged();

            fired.Should().BeTrue();
        }

        [Fact]
        public void NotifySyncStatusChanged_WithNoSubscribers_DoesNotThrow()
        {
            Action act = () => _svc.NotifySyncStatusChanged();
            act.Should().NotThrow();
        }

        [Fact]
        public void NotifySyncStatusChanged_NotifiesMultipleSubscribers()
        {
            int count = 0;
            _svc.SyncStatusChanged += (_, _) => count++;
            _svc.SyncStatusChanged += (_, _) => count++;

            _svc.NotifySyncStatusChanged();

            count.Should().Be(2);
        }
    }

    public class SettingsChangedServiceTests
    {
        private readonly SettingsChangedService _svc;

        public SettingsChangedServiceTests()
        {
            _svc = (SettingsChangedService)Activator.CreateInstance(
                typeof(SettingsChangedService), nonPublic: true)!;
        }

        [Fact]
        public void NotifySettingsChanged_FiresEvent()
        {
            bool fired = false;
            _svc.SettingsChanged += (_, _) => fired = true;

            _svc.NotifySettingsChanged();

            fired.Should().BeTrue();
        }

        [Fact]
        public void NotifySettingsChanged_WithNoSubscribers_DoesNotThrow()
        {
            Action act = () => _svc.NotifySettingsChanged();
            act.Should().NotThrow();
        }

        [Fact]
        public void NotifySettingsChanged_AfterUnsubscribe_DoesNotNotify()
        {
            bool fired = false;
            EventHandler handler = (_, _) => fired = true;
            _svc.SettingsChanged += handler;
            _svc.SettingsChanged -= handler;

            _svc.NotifySettingsChanged();

            fired.Should().BeFalse();
        }
    }
}
