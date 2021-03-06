using System;
using System.Threading.Tasks;
using AI4E.Routing.SignalR.Sample.Common;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AI4E.Routing.SignalR.Client.Sample
{
    internal static class Program
    {
        internal static async Task Main(string[] args)
        {
            var services = new ServiceCollection();
            ConfigureServices(services);

            using (var serviceProvider = services.BuildServiceProvider())
            {
                var messageDispatcher = serviceProvider.GetRequiredService<IMessageDispatcher>();

                await RunAsync(messageDispatcher);
            }
        }

        private static void ConfigureServices(IServiceCollection services)
        {
            ConfigureLogging(services);

            services.AddHubConnection()
                    .WithUrl("http://localhost:5000/MessageDispatcherHub")
                    .Build();

            services.AddRemoteMessageDispatcher();
            services.AddSingleton<IClientEndPoint, ClientEndPoint>();
            services.AddSingleton<IRequestReplyClientEndPoint, RequestReplyClientEndPoint>();
            services.AddSingleton<IMessageRouterFactory, RemoteMessageRouterFactory>();
        }

        private static void ConfigureLogging(IServiceCollection services)
        {
            services.AddLogging(builder =>
            {
                builder.SetMinimumLevel(LogLevel.Debug);
                builder.AddConsole();
            });
        }

        private static async Task RunAsync(IMessageDispatcher messageDispatcher)
        {
            var queryResult = await messageDispatcher.DispatchAsync(new TestQuery());

            await Console.Out.WriteLineAsync(queryResult.ToString());

            await Console.In.ReadLineAsync();
        }
    }
}
