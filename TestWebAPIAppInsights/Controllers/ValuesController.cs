//*********************************************************
//
// Copyright (c) Microsoft. All rights reserved.
// This code is licensed under the MIT License (MIT).
// THIS CODE IS PROVIDED *AS IS* WITHOUT WARRANTY OF
// ANY KIND, EITHER EXPRESS OR IMPLIED, INCLUDING ANY
// IMPLIED WARRANTIES OF FITNESS FOR A PARTICULAR
// PURPOSE, MERCHANTABILITY, OR NON-INFRINGEMENT.
//
//*********************************************************
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace TestWebAPIAppInsights.Controllers
{
    public class HttpPostTimer : IDisposable
    {
        DateTime start;
        TelemetryClient _telemetryClient;


        public HttpPostTimer(TelemetryClient telemetryClient)
        {

            _telemetryClient = telemetryClient;
            start = DateTime.Now;
        }
        public void Dispose()
        {
            TimeSpan ts = DateTime.Now - start;
            _telemetryClient.GetMetric("RequestDurationMs").TrackValue(ts.TotalMilliseconds);
        }

    }
    public class TestResponse
    {
        public string name { get; set; }
        public string value { get; set; }

    };

    [ApiController]
    [Route("[controller]")]
    public class ValuesController : ControllerBase
    {

        private readonly ILogger<ValuesController> _logger;
        private readonly TelemetryClient _telemetryClient;
        public ValuesController(ILogger<ValuesController> logger, TelemetryClient tc)
        {
            _telemetryClient = tc;
            _logger = logger;
        }
        private void SetMaxTelemetryItemsPerSecond(int value)
        {
#pragma warning disable CS0618 
            var builder = TelemetryConfiguration.Active.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
#pragma warning restore CS0618
            if (value > 1000)
                builder.UseAdaptiveSampling(excludedTypes: "Exception");
            else
                builder.UseAdaptiveSampling(excludedTypes: "Exception,Event");

            builder.UseAdaptiveSampling(maxTelemetryItemsPerSecond: value);
            builder.Build();
        }
        private static string GetIpFromRequestHeaders(HttpRequest request)
        {
            return (request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "").Split(new char[] { ':' }).FirstOrDefault();
        }

        static int requestCounter = 0;
        [HttpPost]
        public async System.Threading.Tasks.Task<IActionResult>  Post()
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            // Log Event 
            var evt = new EventTelemetry("Function values called");
            evt.Context.Device.Id = GetIpFromRequestHeaders(this.Request);
            this._telemetryClient.TrackEvent(evt);
            string value = await (new StreamReader(this.Request.Body)).ReadToEndAsync();
            dynamic inputdata = JsonConvert.DeserializeObject(value);

            // If more than 10000 requests change value MaxTelemetryItemsPerSecond
            if (requestCounter++ > 10000)
                SetMaxTelemetryItemsPerSecond(100);

            if (inputdata != null)
            {
                using (HttpPostTimer hpt = new HttpPostTimer(_telemetryClient))
                {
                    TestResponse t = new TestResponse();
                    t.name = "testResponse";
                    t.value = inputdata.ToString();
                    return await Task.FromResult(new JsonResult(t));
                }
            }
            return new BadRequestResult();

        }
        [HttpGet]
        public async System.Threading.Tasks.Task<IActionResult> Get()
        {
            _logger.LogInformation("C# HTTP trigger function processed a request.");
            TestResponse t = new TestResponse();
            t.name = "testResponse";
            t.value = requestCounter.ToString();
            return await Task.FromResult(new JsonResult(t));

        }
    }
}
