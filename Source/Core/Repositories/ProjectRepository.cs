﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Exceptionless.Core.Extensions;
using Exceptionless.Core.Models;
using Exceptionless.Core.Repositories.Configuration;
using Exceptionless.Core.Repositories.Queries;
using FluentValidation;
using Foundatio.Repositories.Elasticsearch.Queries;
using Foundatio.Repositories.Elasticsearch.Queries.Builders;
using Foundatio.Repositories.Models;
using Foundatio.Repositories.Queries;
using Foundatio.Utility;
using Nest;

namespace Exceptionless.Core.Repositories {
    public class ProjectRepository : RepositoryOwnedByOrganization<Project>, IProjectRepository {
        public ProjectRepository(ExceptionlessElasticConfiguration configuration, IValidator<Project> validator) 
            : base(configuration.Organizations.Project, validator) {
        }

        public Task<CountResult> GetCountByOrganizationIdAsync(string organizationId) {
            if (String.IsNullOrEmpty(organizationId))
                throw new ArgumentNullException(nameof(organizationId));

            return CountAsync(new ExceptionlessQuery()
                .WithOrganizationId(organizationId)
                .WithCacheKey(String.Concat("Organization:", organizationId)));
        }

        public Task<IFindResults<Project>> GetByOrganizationIdsAsync(ICollection<string> organizationIds, PagingOptions paging = null) {
            if (organizationIds == null)
                throw new ArgumentNullException(nameof(organizationIds));

            if (organizationIds.Count == 0)
                return Task.FromResult<IFindResults<Project>>(new FindResults<Project>());

            string cacheKey = organizationIds.Count == 1 ?  String.Concat("paged:Organization:", organizationIds.Single()) : null;
            return FindAsync(new ExceptionlessQuery()
                .WithOrganizationIds(organizationIds)
                .WithPaging(paging)
                .WithCacheKey(cacheKey));
        }

        public Task<IFindResults<Project>> GetByNextSummaryNotificationOffsetAsync(byte hourToSendNotificationsAfterUtcMidnight, int limit = 10) {
            var filter = Filter<Project>.Range(r => r.OnField(o => o.NextSummaryEndOfDayTicks).Lower(SystemClock.UtcNow.Ticks - (TimeSpan.TicksPerHour * hourToSendNotificationsAfterUtcMidnight)));
            return FindAsync(new ExceptionlessQuery().WithElasticFilter(filter).WithSelectedFields("id", "next_summary_end_of_day_ticks").WithLimit(limit));
        }

        public async Task<CountResult> IncrementNextSummaryEndOfDayTicksAsync(IReadOnlyCollection<Project> projects) {
            if (projects == null)
                throw new ArgumentNullException(nameof(projects));

            if (projects.Count == 0)
                return new CountResult();

            string script = $"ctx._source.next_summary_end_of_day_ticks += {TimeSpan.TicksPerDay};";
            var recordsAffected = await PatchAllAsync(new ExceptionlessQuery().WithIds(projects.Select(p => p.Id)), script, false).AnyContext();
            if (recordsAffected > 0)
                await InvalidateCacheAsync(projects).AnyContext();
            
            return new CountResult(recordsAffected);
        }

        protected override async Task InvalidateCachedQueriesAsync(IReadOnlyCollection<Project> documents) {
            var organizations = documents.Select(d => d.OrganizationId).Distinct().Where(id => !String.IsNullOrEmpty(id));
            await Cache.RemoveAllAsync(organizations.Select(id => $"count:Organization:{id}")).AnyContext();
            await base.InvalidateCachedQueriesAsync(documents).AnyContext();
        }
    }
}
