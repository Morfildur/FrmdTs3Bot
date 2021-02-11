using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TeamSpeak3QueryApi.Net.Specialized;
using TeamSpeak3QueryApi.Net.Specialized.Responses;

namespace Ts3Bot
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly IConfiguration configuration;

        public Worker(ILogger<Worker> logger, IConfiguration configuration)
        {
            this.logger = logger;
            this.configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            List<int> checkedClientIds = new List<int>();
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

            logger.LogInformation("Worker started at: {Time}", DateTimeOffset.Now);

            List<int> frmdGroups = new List<int>();

            logger.LogInformation("Trying to Connect to {Host}", ts3Host);
            using TeamSpeakClient rc = new TeamSpeakClient(ts3Host);
            await rc.Connect();
            logger.LogInformation("Connected at: {Time}", DateTimeOffset.Now);

            await rc.Login(ts3User, ts3Pass);
            await rc.UseServer(1);

            logger.LogInformation("Logged in at: {Time}", DateTimeOffset.Now);

            IReadOnlyList<GetServerGroupListInfo> serverGroups = await rc.GetServerGroups();
            frmdGroups.AddRange(serverGroups
                .Where(g => g.Name == "FRMD")
                .Select(g => g.Id));

            frmdGroups.ForEach(g => logger.LogInformation("Found FRMD Group with Id {FrmdGroupId}", g));

            if (frmdGroups.Count == 0)
            {
                logger.LogCritical("No FRMD Group found");
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                IReadOnlyList<GetClientInfo> rawClientInfoList = await rc.GetClients();

                List<GetClientInfo> clientInfoList = rawClientInfoList
                    .Where(c => c.Type == ClientType.FullClient)
                    .Where(c => !nameBlacklist.Contains(c.NickName))
                    .Where(c => !checkedClientIds.Contains(c.Id))
                    .ToList();

                foreach (GetClientInfo clientInfo in clientInfoList)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(1));
                    logger.LogInformation("Found User: {User} {Id}", clientInfo.NickName, clientInfo.Id);
                    GetClientDetailedInfo clientDetails = await rc.GetClientInfo(clientInfo);
                    if (pokedClients.Contains(clientDetails.UniqueIdentifier))
                    {
                        logger.LogInformation("Already messaged {User} {Id} {UniqueId}", clientDetails.NickName,
                            clientInfo.Id, clientDetails.UniqueIdentifier);
                    }
                    else
                    {
                        if (clientDetails.ServerGroupIds.Any(gid => frmdGroups.Contains(gid)))
                        {
                            logger.LogInformation("Messaging {Client}", clientDetails.UniqueIdentifier);
                            await rc.PokeClient(clientInfo,
                                "Please sign up for Outfit Wars (See Discord #announcements)");
                            pokedClients.Add(clientDetails.UniqueIdentifier);


                            if (Directory.Exists(pokedCacheDirectory))
                            {
                                await File.AppendAllLinesAsync(
                                    pokedCacheFile,
                                    new[] {clientDetails.UniqueIdentifier},
                                    stoppingToken
                                );
                            }
                        }
                    }


                    checkedClientIds.Add(clientInfo.Id);
                }
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}