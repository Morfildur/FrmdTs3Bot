using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TeamSpeak3QueryApi.Net.Specialized;
using TeamSpeak3QueryApi.Net.Specialized.Notifications;
using TeamSpeak3QueryApi.Net.Specialized.Responses;

namespace Ts3Bot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly IConfiguration configuration;
        private readonly IHostApplicationLifetime hostApplicationLifetime;

        public Worker(
            ILogger<Worker> logger,
            IConfiguration configuration,
            IHostApplicationLifetime hostApplicationLifetime
        )
        {
            this.logger = logger;
            this.configuration = configuration;
            this.hostApplicationLifetime = hostApplicationLifetime;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            List<string> pokedClients = new List<string>();
            List<string> nameBlacklist = new List<string>
            {
                "Vanguard Radio"
            };

            const string pokedCacheDirectory = "/srv/ts3";
            const string pokedCacheFile = pokedCacheDirectory + "/pokedCache";
            if (Directory.Exists(pokedCacheDirectory))
            {
                if (File.Exists(pokedCacheFile))
                {
                    pokedClients.AddRange(
                        File.ReadLines(pokedCacheFile)
                            .Select(l => l.Trim())
                            .Where(l => l.Length > 0)
                    );
                }
            }

            string ts3Host = Environment.GetEnvironmentVariable("TS3_HOST")
                             ?? configuration["Ts3:Host"]
                             ?? "localhost";
            string ts3User = Environment.GetEnvironmentVariable("TS3_USER")
                             ?? configuration["Ts3:Username"]
                             ?? "serveradmin";
            string ts3Pass = Environment.GetEnvironmentVariable("TS3_PASS")
                             ?? configuration["Ts3:Password"]
                             ?? "";
            string message = Environment.GetEnvironmentVariable("TS3_MESSAGE")
                             ?? configuration["Message"]
                             ?? "Outfit Wars on Saturday 27th, 19:30 UTC/20:30 CET (See Discord #announcements)";

            if (message?.Length <= 0)
            {
                // Critical error. Kill application
                logger.LogCritical("{Time} No message configured", DateTime.Now);
                hostApplicationLifetime.StopApplication();
            }

            logger.LogInformation("{Time} Worker started", DateTimeOffset.Now);

            List<int> frmdGroups = new List<int>();

            logger.LogInformation("{Time} Trying to Connect to {Host}", DateTimeOffset.Now, ts3Host);
            using TeamSpeakClient rc = new TeamSpeakClient(ts3Host);
            await rc.Connect();
            logger.LogInformation("{Time} Connected", DateTimeOffset.Now);

            await rc.Login(ts3User, ts3Pass);
            logger.LogInformation("{Time} Logged in", DateTimeOffset.Now);

            await rc.UseServer(1);

            IReadOnlyList<GetServerGroupListInfo> serverGroups = await rc.GetServerGroups();
            frmdGroups.AddRange(serverGroups
                .Where(g => g.Name == "FRMD")
                .Select(g => g.Id));

            frmdGroups.ForEach(g =>
                logger.LogInformation("{Time} Found FRMD Group with Id {FrmdGroupId}", DateTimeOffset.Now, g));

            if (frmdGroups.Count == 0)
            {
                // Critical error. Kill application
                logger.LogCritical("{Time} No FRMD Group found", DateTimeOffset.Now);
                hostApplicationLifetime.StopApplication();
            }

            ConcurrentQueue<ClientEnterView> toPoke = new ConcurrentQueue<ClientEnterView>();

            await rc.RegisterServerNotification();
            rc.Subscribe<ClientEnterView>(data =>
            {
                foreach (ClientEnterView d in data)
                {
                    bool isFrmdGroup = d.ServerGroups.Split(",")
                        .Any(s => frmdGroups.Contains(int.Parse(s)));

                    logger.LogInformation("{Time} {Name} {Uid} {IsFrmd}", DateTime.Now, d.NickName, d.Uid, isFrmdGroup);

                    if (isFrmdGroup && !pokedClients.Contains(d.Uid) && !nameBlacklist.Contains(d.NickName))
                    {
                        toPoke.Enqueue(d);
                    }
                }
            });

            DateTime lastKeepAlive = DateTime.Now;

            while (!stoppingToken.IsCancellationRequested)
            {
                if (lastKeepAlive.AddSeconds(30) < DateTime.Now)
                {
                    // Keepalive
                    await rc.WhoAmI();
                    lastKeepAlive = DateTime.Now;
                }

                while (toPoke.TryDequeue(out ClientEnterView current))
                {
                    logger.LogInformation("{Time} Messaging {Client}", DateTimeOffset.Now, current.Uid);

                    await rc.PokeClient(current.Id, message);

                    pokedClients.Add(current.Uid);
                }

                await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
        }
    }
}