﻿using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Identity.Web;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace CalendarDisplay.Data
{
    public interface ICalendarManager
    {
        Task<List<Event>> GetEventsBetweenDatesAsync(string calendarName, DateTime start, DateTime end, CancellationToken cancellationToken = default);

        Task<List<Event>> GetMonthsEventsAsync(string calendarName, DateTime date, CancellationToken cancellationToken = default);
    }

    public class CalendarManager : ICalendarManager
    {
        private List<Calendar> _cachedCalendars = null;
        private readonly ILogger<CalendarManager> _logger;
        private readonly GraphServiceClient _graphServiceClient;

        public CalendarManager(
            ILogger<CalendarManager> logger,
            GraphServiceClient graphServiceClient)
        {
            _logger = logger;
            _graphServiceClient = graphServiceClient;
        }

        [AuthorizeForScopes(ScopeKeySection = "DownstreamApi:Scopes")]
        public async Task<string> GetCalendarIdAsync(string calendarName, CancellationToken cancellationToken = default)
        {
            // Fetch a list of calendars
            if (_cachedCalendars is null)
            {
                _cachedCalendars = (await _graphServiceClient.Me.Calendars
                    .Request()
                    .GetAsync(cancellationToken)).ToList();
                _logger.LogInformation("Calendar cache was empty, fetched {count} calendars", _cachedCalendars.Count());
            }

            // Return the calendar id
            return _cachedCalendars.Where(o => o.Name == calendarName).Single().Id;
        }

        [AuthorizeForScopes(ScopeKeySection = "DownstreamApi:Scopes")]
        public async Task<List<Event>> GetEventsBetweenDatesAsync(
            string calendarName, DateTime start, DateTime end, CancellationToken cancellationToken = default)
        {
            try
            {
                var calendarId = await GetCalendarIdAsync(calendarName, cancellationToken);

                // Create the DateTime using local time, or system time (from the Raspberry Pi, or your dev machine)
                // This means the date string will include the offset and the search query will be correct for the local timezone
                var sd = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, DateTimeKind.Local);
                var startDate = TimeZoneInfo.ConvertTimeToUtc(sd).ToString("o");
                var ed = new DateTime(end.Year, end.Month, end.Day, 0, 0, 0, DateTimeKind.Local).AddDays(1).AddTicks(-1);
                var endDate = TimeZoneInfo.ConvertTimeToUtc(ed).ToString("o");

                var queryOptions = new List<QueryOption>()
                {
                    new QueryOption("startDateTime", startDate),
                    new QueryOption("endDateTime", endDate)
                };

                ICalendarCalendarViewCollectionPage page = await _graphServiceClient.Me.Calendars[calendarId].CalendarView
                    .Request(queryOptions)
                    .GetAsync(cancellationToken);
                List<Event> events = page.ToList();

                // Query the rest of the pages
                while (page.NextPageRequest is not null)
                {
                    page = await page.NextPageRequest.GetAsync(cancellationToken);
                    events.AddRange(page.ToList());
                }

                _logger.LogInformation("{this} success, {eventCount} events found.",
                    nameof(GetMonthsEventsAsync),
                    events.Count);

                return events;
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "{this} failed.", nameof(GetMonthsEventsAsync));
                return new();
            }
        }

        [AuthorizeForScopes(ScopeKeySection = "DownstreamApi:Scopes")]
        public async Task<List<Event>> GetMonthsEventsAsync(string calendarName, DateTime date, CancellationToken cancellationToken = default)
        {
            var startDate = new DateTime(date.Year, date.Month, 1, 0, 0, 0, DateTimeKind.Local);
            var endDate = startDate.AddMonths(1).AddTicks(-1);
            return await GetEventsBetweenDatesAsync(calendarName, startDate, endDate, cancellationToken);
        }
    }
}