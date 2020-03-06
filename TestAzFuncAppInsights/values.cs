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
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Metrics;
using Microsoft.ApplicationInsights.DataContracts;
using  Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Azure.Functions.Extensions.DependencyInjection;
using Microsoft.ApplicationInsights.Channel;

[assembly: FunctionsStartup(typeof(Company.Function.Startup))]
namespace Company.Function
{

    // Turn around to inject TelemetryClient
    public class Startup : FunctionsStartup
    {
        public override void Configure(IFunctionsHostBuilder builder)
        {
            if (IsAppInsightsRegistrationRequired(builder.Services))
            {
                builder.Services.AddSingleton(sp =>
                {
                    var key = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
                    var telemetryConfiguration = (!string.IsNullOrWhiteSpace(key))
                        ? new TelemetryConfiguration(key)
                        : new TelemetryConfiguration();

                    telemetryConfiguration.TelemetryInitializers.Add(new OperationCorrelationTelemetryInitializer());
                    return new TelemetryClient(telemetryConfiguration);
                });
            }
        }
        // Temporary workaround until the client tools (ie. VS support) includes the fixes from 3.0.13130
        static bool IsAppInsightsRegistrationRequired(IEnumerable<ServiceDescriptor> services) =>
            !IsServiceRegistered<TelemetryClient>(services);

        static bool IsServiceRegistered<T>(IEnumerable<ServiceDescriptor> services) =>
            services.Any(descriptor => descriptor.ServiceType == typeof(T));

    }

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

    public class ValuesController
    {

        public ValuesController(TelemetryClient telemetryClient)
        {

            this._telemetryClient = telemetryClient;
#pragma warning disable CS0618 
            TelemetryProcessorChainBuilder builder = TelemetryConfiguration.Active.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
#pragma warning restore CS0618
            // some custom telemetry processor for Application Insights that filters out synthetic traffic (e.g traffic that comes from Azure to keep the server awake).
            builder.UseAdaptiveSampling(excludedTypes: "Exception");
            builder.UseAdaptiveSampling(maxTelemetryItemsPerSecond: 10000);
            builder.Build();
        }

        private TelemetryClient _telemetryClient;
        public  double HttpPostCounter;
        public  double HttpPostTimer;
        public  DateTime HttpPostStartTime = DateTime.MinValue;
        public  DateTime HttpPostEndTime = DateTime.MinValue;
        
        private static string GetIpFromRequestHeaders(HttpRequest request)
        {
            return (request.Headers["X-Forwarded-For"].FirstOrDefault() ?? "").Split(new char[] { ':' }).FirstOrDefault();
        }
        
        private void SetMaxTelemetryItemsPerSecond(int value)
        {
#pragma warning disable CS0618 
            var builder = TelemetryConfiguration.Active.DefaultTelemetrySink.TelemetryProcessorChainBuilder;
#pragma warning restore CS0618
            if(value>1000)
                builder.UseAdaptiveSampling(excludedTypes: "Exception");
            else
                builder.UseAdaptiveSampling(excludedTypes: "Exception,Event");

            builder.UseAdaptiveSampling(maxTelemetryItemsPerSecond: value);
            builder.Build();
        }
        static int requestCounter = 0;
        [FunctionName("values")]
        public  async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            
            log.LogInformation("C# HTTP trigger function processed a request.");
            // Log Event 
            var evt = new EventTelemetry("Function values called");
            evt.Context.Device.Id = GetIpFromRequestHeaders(req);
            this._telemetryClient.TrackEvent(evt);
            if (req.Method.Equals("post",StringComparison.OrdinalIgnoreCase))
            {
                string value = await (new StreamReader(req.Body)).ReadToEndAsync();
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
            }
            else if (req.Method.Equals("get",StringComparison.OrdinalIgnoreCase))
            {
                TestResponse t = new TestResponse();
                t.name = "testResponse";
                t.value = requestCounter.ToString();
                return await Task.FromResult(new JsonResult(t));
            }


            return new BadRequestResult();
        }
    }
}
