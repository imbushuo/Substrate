// Copyright 2019 The Lawrence Industry and its affiliates. All rights reserved.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Substrate.ContributionGraph.Timeseries;

namespace Substrate.Edge.Controllers
{
    [Controller]
    public class ContributionTsController : Controller
    {
        private ContributionTsdb _tsdb;

        public ContributionTsController(
            ContributionTsdb tsdb
        )
        {
            _tsdb = tsdb;
        }

        [HttpGet]
        public async Task<IActionResult> GetTimeSeries(string username, string tz)
        {
            if (username == null) return BadRequest();

            var timeZoneString  = tz ?? "Etc/UTC";
            TimeZoneInfo tzi;
            try
            {
                tzi = TimeZoneInfo.FindSystemTimeZoneById(timeZoneString);
            }
            catch (TimeZoneNotFoundException)
            {
                return BadRequest("Invalid time zone");
            }

            var searchTimeNotBefore = TimeZoneInfo.ConvertTime(DateTimeOffset.Now, tzi).Subtract(TimeSpan.FromDays(14));
            var tsPoints = await _tsdb.GetSamplesAsync(username, searchTimeNotBefore, HttpContext.RequestAborted);

            var datapointDict = new ConcurrentDictionary<long, long>();
            foreach (var datapoint in tsPoints)
            {
                var t = new DateTimeOffset(datapoint.MetricTimeStampUtc, TimeSpan.Zero).ToUnixTimeSeconds();
                datapointDict.AddOrUpdate(t, datapoint.Count, (t, c) => datapoint.Count + c);
            }

            // This serialization is only supported by the JSON.NET library
            return Content(JsonConvert.SerializeObject(datapointDict), "application/json");
        }
    }
}
