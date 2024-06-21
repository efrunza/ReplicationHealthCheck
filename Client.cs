using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Extensions.Logging;
using System.Linq;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ReplicationHealthCheck
{
    public static class Client
    {
        private static readonly int _timeIntervalInMinutes = 1;

        [FunctionName("TestHealthCheckOrchestrator")]
        public static async Task<HttpResponseMessage> HealthCheckOrchestrator(
           [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
           [DurableClient] IDurableOrchestrationClient starter,
           ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("HealthCheckOrchestrator", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("UpdateCounter")]
        public static async Task<HttpResponseMessage> UpdateCounter(
          [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
          [DurableClient] IDurableOrchestrationClient starter,
          ILogger log)
        {
            // Function input comes from the request content.
            string instanceId = await starter.StartNewAsync("UpdateCounter1", null);

            log.LogInformation($"Started orchestration with ID = '{instanceId}'.");

            return starter.CreateCheckStatusResponse(req, instanceId);
        }

        [FunctionName("StatusCheck")]
        public static async Task<IActionResult> Run(
          [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post")] HttpRequestMessage req,
           [DurableClient(TaskHub = "TestingHub9")] IDurableOrchestrationClient client,
           ILogger log)
        {
            var query = System.Web.HttpUtility.ParseQueryString(req.RequestUri.Query);
            string name = query.Get("name");

            var statusName = string.Empty;

            bool isLastCompletedInstanceHealthy = false;
            bool justStarted = false;

            DateTime lastUpdatedTime = DateTime.UtcNow;
            DateTime curentTime = DateTime.UtcNow;

            // Step 1 - Get the last completed orchestration's instances ordered descending based on the lastUpdatedTime
            var resultCompleted = await GetQueryResults(client, name, OrchestrationRuntimeStatus.Completed);

            var countCompleted = resultCompleted.ToList().Count;

            if (countCompleted > 0)
            {
                // Step 2 - get the last completed instance based on the lastUpdatedTime descending
                var item = resultCompleted.ToList().FirstOrDefault();

                if (item != null && item.Name == name)
                {
                    DurableOrchestrationStatus status = await client.GetStatusAsync(item.InstanceId);

                    lastUpdatedTime = status.LastUpdatedTime;

                    if (status.Output.Type != JTokenType.Null)
                    {
                        // Step 3 - get the status from the last completed instance
                        isLastCompletedInstanceHealthy = (bool)((JValue)status.Output).Value;
                    }
                }
            }

            // Step 4 - calculate the time elapsed from when the last completed instance has been updated
            var elapsedTimeInMinutes = (curentTime - lastUpdatedTime).TotalMinutes;

            // Step 5 - check if there are instances running
            var resultRunning = await GetQueryResults(client, name, OrchestrationRuntimeStatus.Running);
            var countRunning = resultRunning.ToList().Count;

            if (countCompleted == 0 && countRunning == 0)
            {
                // Step 6 - no instances found
                statusName = "No orchestrations found. Health Check has been started.";

                // Step 7 - start the orchestration
                await client.StartNewAsync("HealthCheckOrchestrator", null);

                justStarted = true;
            }
            else
            {
                // Step 6. 1 - there are instances completed and no instances running
                if (countCompleted == 1 && countRunning == 0)
                {
                    // Step 7. 1 - the last completed instance is older than 1 minute
                    if (elapsedTimeInMinutes > _timeIntervalInMinutes)
                    {
                        statusName = "Health Check Status it is too old. Health Check has been restarted.";

                        // Step 7. 2 - restart the orchestration
                        await client.StartNewAsync("HealthCheckOrchestrator", null);

                        justStarted = true;
                    }
                }
            }

            if (countRunning > 0 || justStarted == true)
            {
                // Step 8 - there are instances already running from the past
                if (countRunning > 0)
                {
                    statusName = "Health Check it is running.";
                }
            }
            else
            {
                // Step 8.1 - there is an existing completed instance processed in the last 1 minute
                if (elapsedTimeInMinutes < _timeIntervalInMinutes)
                {
                    statusName = "The last completed health check in the last 1 minute";

                    if (isLastCompletedInstanceHealthy)
                    {
                        statusName = statusName + " is healthy.";
                    }
                    else
                    {
                        statusName = statusName + " is not healthy.";
                    }
                }
            }

            if (string.IsNullOrEmpty(statusName))
            {
                // Step 9 - this means that a use case has not been implemented
                statusName = "This should not happened.";
            }

            return new OkObjectResult(new { HasRunning = statusName });


        }

        private static async Task<System.Collections.Generic.IEnumerable<DurableOrchestrationStatus>> GetQueryResults(IDurableOrchestrationClient client, string name, OrchestrationRuntimeStatus status)
        {
            var queryFilterCompleted = new OrchestrationStatusQueryCondition
            {
                RuntimeStatus = new[]
                {
                      status
                },
                CreatedTimeFrom = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(2)),
                CreatedTimeTo = DateTime.UtcNow.Subtract(TimeSpan.FromMinutes(0)),
                PageSize = 100,
            };

            OrchestrationStatusQueryResult resultCompleted = await client.ListInstancesAsync(queryFilterCompleted, CancellationToken.None);

            resultCompleted.DurableOrchestrationState = resultCompleted.DurableOrchestrationState.Where(x => x.Name == name).OrderByDescending(x => x.LastUpdatedTime).ToList();

            return resultCompleted.DurableOrchestrationState;
        }
    }
}
