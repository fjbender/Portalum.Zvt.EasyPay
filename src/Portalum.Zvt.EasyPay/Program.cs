using CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Portalum.Zvt;
using Portalum.Zvt.EasyPay.Models;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Portalum.Zvt.EasyPay
{
    class Program
    {
        private static readonly string ConfigurationFile = "appsettings.json";

        static async Task<int> Main(string[] args)
        {
            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddFile("default.log", LogLevel.Debug, outputTemplate: "{Timestamp:HH:mm:ss.fff} {Level:u3} {SourceContext} {Message:lj}{NewLine}{Exception}").SetMinimumLevel(LogLevel.Debug);
            });

            var logger = loggerFactory.CreateLogger<Program>();
            logger.LogInformation("Start");

            if (!File.Exists(ConfigurationFile))
            {
                logger.LogError($"Configuration file not available, {ConfigurationFile}");
                return -3;
            }

            int exitCode = -2;

            Parser.Default.ParseArguments<CommandLineOptions>(args)
                .WithParsed(options => { exitCode = RunPaymentAsync(loggerFactory, logger, options.Amount).GetAwaiter().GetResult(); })
                .WithNotParsed(_ => { exitCode = -2; });

            logger.LogInformation("Exit");
            return exitCode;
        }

        private static PaymentTerminalConfig GetConfiguration(ILogger logger)
        {
            var builder = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile(ConfigurationFile, optional: false);

            IConfigurationRoot configuration = builder.Build();

            if (!int.TryParse(configuration["Port"], out var port))
            {
                logger.LogError("Cannot parse port from configuration file");
            }

            return new PaymentTerminalConfig
            {
                IpAddress = configuration["IpAddress"]!,
                Port = port
            };
        }

        private static async Task<int> RunPaymentAsync(ILoggerFactory loggerFactory, ILogger logger, decimal amount)
        {
            logger.LogInformation($"Starting payment process with amount {amount}");
            Console.WriteLine($"Amount: {amount:C2}");

            var config = GetConfiguration(logger);

            var zvtClientConfig = new ZvtClientConfig
            {
                Encoding = ZvtEncoding.CodePage437,
                Language = Language.German,
                Password = 000000
            };

            var deviceCommunicationLogger = loggerFactory.CreateLogger<TcpNetworkDeviceCommunication>();
            var zvtClientLogger = loggerFactory.CreateLogger<ZvtClient>();

            using var deviceCommunication = new TcpNetworkDeviceCommunication(
                config.IpAddress,
                port: config.Port,
                enableKeepAlive: false,
                logger: deviceCommunicationLogger);

            Console.WriteLine("Status: Connecting to payment terminal...");

            if (!await deviceCommunication.ConnectAsync())
            {
                Console.WriteLine("Status: Cannot connect to payment terminal");
                await Task.Delay(3000);
                logger.LogError($"Cannot connect to {config.IpAddress}:{config.Port}");
                return -4;
            }

            var zvtClient = new ZvtClient(deviceCommunication, logger: zvtClientLogger, clientConfig: zvtClientConfig);
            try
            {
                zvtClient.IntermediateStatusInformationReceived += status => Console.WriteLine($"Status: {status}");

                var response = await zvtClient.PaymentAsync(amount);
                if (response.State == CommandResponseState.Successful)
                {
                    logger.LogInformation("Payment successful");
                    Console.WriteLine("Status: Payment successful");
                    await Task.Delay(1000);
                    return 0;
                }

                logger.LogInformation("Payment not successful");
                Console.WriteLine("Status: Payment not successful");
                await Task.Delay(1000);
                return -1;
            }
            finally
            {
                zvtClient.IntermediateStatusInformationReceived -= status => Console.WriteLine($"Status: {status}");
                zvtClient.Dispose();
            }
        }
    }
}
