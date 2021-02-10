using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Ts3Bot
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine(Environment.GetEnvironmentVariable("TS3_HOST"));
            Console.WriteLine(Environment.GetEnvironmentVariable("TS3_USER"));
            Console.WriteLine(Environment.GetEnvironmentVariable("TS3_PASS"));
            
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) => { services.AddHostedService<Worker>(); });
    }
}