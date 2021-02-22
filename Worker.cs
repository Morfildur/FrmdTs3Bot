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
            string message = Environment.GetEnvironmentVariable("TS3_MESSAGE")
                             ?? configuration["Message"]
                             ?? "Outfit Wars on Saturday 27th, 19:30 UTC/20:30 CET (See Discord #announcements)";

            if (message?.Length <= 0)
            {
                logger.LogCritical("{Time} No message configured", DateTime.Now);
            }
            
            logger.LogInformation("{Time} Worker started", DateTimeOffset.Now);

            List<int> frmdGroups = new List<int>();

            logger.LogInformation("{Time} Trying to Connect to {Host}", DateTimeOffset.Now, ts3Host);
            using TeamSpeakClient rc = new TeamSpeakClient(ts3Host);
            await rc.Connect();
            logger.LogInformation("{Time} Connected", DateTimeOffset.Now);

            await rc.Login(ts3User, ts3Pass);
            await rc.UseServer(1);

            logger.LogInformation("{Time} Logged in", DateTimeOffset.Now);

            IReadOnlyList<GetServerGroupListInfo> serverGroups = await rc.GetServerGroups();
            frmdGroups.AddRange(serverGroups
                .Where(g => g.Name == "FRMD")
                .Select(g => g.Id));

            frmdGroups.ForEach(g => logger.LogInformation("{Time} Found FRMD Group with Id {FrmdGroupId}", DateTimeOffset.Now, g));

            if (frmdGroups.Count == 0)
            {
                logger.LogCritical("{Time} No FRMD Group found", DateTimeOffset.Now);
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
                    logger.LogInformation("{Time} Found User: {User} {Id}", DateTimeOffset.Now, clientInfo.NickName, clientInfo.Id);
                    GetClientDetailedInfo clientDetails = await rc.GetClientInfo(clientInfo);
                    if (pokedClients.Contains(clientDetails.UniqueIdentifier))
                    {
                        logger.LogInformation("{Time} Already messaged {User} {Id} {UniqueId}", DateTimeOffset.Now, clientDetails.NickName,
                            clientInfo.Id, clientDetails.UniqueIdentifier);
                    }
                    else
                    {
                        if (clientDetails.ServerGroupIds.Any(gid => frmdGroups.Contains(gid)))
                        {
                            logger.LogInformation("{Time} Messaging {Client}", DateTimeOffset.Now, clientDetails.UniqueIdentifier);
                            await rc.PokeClient(clientInfo, message);
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

                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }
}