
using System;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.DurableTask;
using Microsoft.Extensions.Logging;
using System.Threading;

namespace TestDistributedTransactions
{
    public class Orchestrator
    {
        private readonly int timeIntervalInMinutes = 1;

        [FunctionName("HealthCheckOrchestrator")]
        public async Task<bool> Run([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            try
            {

                await context.CallActivityAsync("Activity1", null);
                var counter = 0;

                for (int i = 0; i < timeIntervalInMinutes && counter == 0; i++)
                {
                    TimeSpan timeout = TimeSpan.FromSeconds(60);
                    DateTime deadline = context.CurrentUtcDateTime.Add(timeout);

                    await context.CreateTimer(deadline, CancellationToken.None);
                    counter = await context.CallActivityAsync<int>("Activity2", null);

                    Console.WriteLine($"Iteration value is: {i}");

                   // Console.WriteLine($"Counter value returned from Activity2 is: {counter}");
                }

                if(counter >0)
                {
                    Console.WriteLine($"Activity2:Replication is healthy.");

                    return true;
                }
                else
                {
                    Console.WriteLine($"Activity 2:Replication is not healthy.");

                    return false;
                }
            }
            catch (Exception e)
            {
                Console.WriteLine($"Caught an error from an activity: {e.Message}");
            }

            return false;
        }

        [FunctionName("UpdateCounter1")]
        public async Task<bool> UpdateCounter1([OrchestrationTrigger] IDurableOrchestrationContext context)
        {
            await context.CallActivityAsync("Activity3", null);           

            return true;
        }       

        [FunctionName(nameof(Activity1))]
        public static async Task<string> Activity1([ActivityTrigger] ILogger log)
        {
            await Task.Run(() => { }).ConfigureAwait(false);

            LogHelpers.ResetCounter();           

            return "Activity 1 performed successfully";
        }

        [FunctionName(nameof(Activity2))]
        public static async Task<int> Activity2([ActivityTrigger] ILogger log)
        {
            await Task.Run(() => { }).ConfigureAwait(false);

            var counter = LogHelpers.GetCounter();

            return counter;
        }

        [FunctionName(nameof(Activity3))]
        public static async Task Activity3([ActivityTrigger] ILogger log)
        {
            await Task.Run(() => { }).ConfigureAwait(false);

            LogHelpers.IncrementCounter();
        }

    }
}