using System;
using System.Collections.Generic;
using FluentAssertions;
using Xunit;
using data_foundry.Services;
using WTW.Diffusion.Core.Models;

namespace WTW.Diffusion.Vsix.Tests.Services
{
    public class ChangeDetectionResultsServiceTests
    {
        private readonly ChangeDetectionResultsService _svc;

        public ChangeDetectionResultsServiceTests()
        {
            _svc = (ChangeDetectionResultsService)Activator.CreateInstance(
                typeof(ChangeDetectionResultsService), nonPublic: true)!;
        }

        // ?? Initial state ?????????????????????????????????????????????????????

        [Fact]
        public void LatestResults_InitiallyNull()
        {
            _svc.LatestResults.Should().BeNull();
        }

        // ?? UpdateResults ?????????????????????????????????????????????????????

        [Fact]
        public void UpdateResults_StoresResults()
        {
            var results = new List<TableChangeSummary>
            {
                new TableChangeSummary { Table = "dbo.Users", Inserts = 3 }
            };

            _svc.UpdateResults(results);

            _svc.LatestResults.Should().BeSameAs(results);
        }

        [Fact]
        public void UpdateResults_FiresResultsUpdatedEvent()
        {
            List<TableChangeSummary>? received = null;
            _svc.ResultsUpdated += (_, r) => received = r;

            var results = new List<TableChangeSummary>();
            _svc.UpdateResults(results);

            received.Should().BeSameAs(results);
        }

        [Fact]
        public void UpdateResults_WithNull_StoresNull()
        {
            _svc.UpdateResults(new List<TableChangeSummary>());
            _svc.UpdateResults(null);

            _svc.LatestResults.Should().BeNull();
        }

        // ?? ClearResults ??????????????????????????????????????????????????????

        [Fact]
        public void ClearResults_NullsLatestResults()
        {
            _svc.UpdateResults(new List<TableChangeSummary>());

            _svc.ClearResults();

            _svc.LatestResults.Should().BeNull();
        }

        [Fact]
        public void ClearResults_FiresEventWithNull()
        {
            bool eventFired = false;
            _svc.ResultsUpdated += (_, r) =>
            {
                if (r == null) eventFired = true;
            };

            _svc.ClearResults();

            eventFired.Should().BeTrue();
        }
    }
}
