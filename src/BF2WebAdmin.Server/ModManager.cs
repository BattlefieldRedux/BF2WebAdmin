﻿using System.Data.SqlClient;
using System.Net;
using System.Text.RegularExpressions;
using BF2WebAdmin.Common;
using BF2WebAdmin.Common.Entities.Game;
using BF2WebAdmin.Data.Abstractions;
using BF2WebAdmin.Data.Entities;
using BF2WebAdmin.Data.Repositories;
using BF2WebAdmin.Server.Abstractions;
using BF2WebAdmin.Server.Configuration.Models;
using BF2WebAdmin.Server.Constants;
using BF2WebAdmin.Server.Extensions;
using BF2WebAdmin.Server.Hubs;
using BF2WebAdmin.Server.Logging;
using BF2WebAdmin.Server.Modules.BF2;
using BF2WebAdmin.Server.Services;
using Microsoft.AspNetCore.SignalR;
using Polly;
using Polly.Registry;
using Serilog;

namespace BF2WebAdmin.Server
{
    // TODO: Move commands into new module folders? (like Features/)
    public class ModManager : IModManager
    {
        private static readonly IReadOnlyPolicyRegistry<string> PolicyRegistry;

        private const string CommandPrefix = ".";

        private readonly IModuleResolver _moduleResolver;
        private readonly IGameServer _gameServer;
        private readonly IServiceProvider _globalServices;

        private IConfiguration Configuration { get; set; }
        private IServiceProvider _services;

        public ILookup<string, ServerPlayerAuth> AuthPlayers { get; private set; }

        public Data.Entities.Server ServerSettings { get; private set; }

        public IMediator Mediator { get; private set; }

        static ModManager()
        {
            // Exponential back-off plus some jitter
            var jitterer = new Random();

            var retryPolicyAsync = Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 6,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 100)),
                    onRetry: (exception, retryAttempt, timespan) => Log.Warning("Failed attempt #{Attempt}. Retrying in {TimeSpan}.\n{ExceptionMessage} - {ExceptionStacktrace}", retryAttempt, timespan, exception.Message, exception.StackTrace)
                );

            var retryPolicyLongAsync = Policy
                .Handle<SqlException>()
                .WaitAndRetryAsync(
                    retryCount: 10, // ~17 min
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 100)),
                    onRetry: (exception, retryAttempt, timespan) => Log.Warning("Failed attempt #{Attempt}. Retrying in {TimeSpan}.\n{ExceptionMessage} - {ExceptionStacktrace}", retryAttempt, timespan, exception.Message, exception.StackTrace)
                );

            var httpStatusCodesWorthRetrying = new[] {
                HttpStatusCode.RequestTimeout, // 408
                HttpStatusCode.InternalServerError, // 500
                HttpStatusCode.BadGateway, // 502
                HttpStatusCode.ServiceUnavailable, // 503
                HttpStatusCode.GatewayTimeout // 504
            };
            var retryPolicyHttpAsync = Policy
                .Handle<HttpRequestException>()
                .OrResult<HttpResponseMessage>(r => httpStatusCodesWorthRetrying.Contains(r.StatusCode))
                .WaitAndRetryAsync(
                    retryCount: 6,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 100)),
                    onRetry: (exception, retryAttempt, timespan) => Log.Warning("Failed attempt #{Attempt}. Retrying in {TimeSpan}.\n{ExceptionMessage} - {ExceptionStacktrace}", retryAttempt, timespan, exception.Exception.Message, exception.Exception.StackTrace)
                );

            var retryPolicy = Policy
                .Handle<SqlException>()
                .WaitAndRetry(
                    retryCount: 6,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)) + TimeSpan.FromMilliseconds(jitterer.Next(0, 100)),
                    onRetry: (exception, retryAttempt, timespan) => Log.Warning("Failed attempt #{Attempts}. Retrying in {TimeSpan}.\n{ExceptionMessage} - {ExceptionStacktrace}", retryAttempt, timespan, exception.Message, exception.StackTrace)
                );

            PolicyRegistry = new PolicyRegistry
            {
                { PolicyNames.RetryPolicy, retryPolicy },
                { PolicyNames.RetryPolicyAsync, retryPolicyAsync },
                { PolicyNames.RetryPolicyLongAsync, retryPolicyLongAsync },
                { PolicyNames.RetryPolicyHttpAsync, retryPolicyHttpAsync }
            };
        }

        private ModManager(IGameServer gameServer, IServiceProvider globalServices)
        {
            _gameServer = gameServer;
            _globalServices = globalServices;
            _moduleResolver = new ModuleResolver();
            Mediator = new Mediator(_moduleResolver);

            BuildConfiguration();
            ConfigureDependencies();
        }

        public static async Task<ModManager> CreateAsync(IGameServer gameServer, IServiceProvider globalServices)
        {
            var modManager = new ModManager(gameServer, globalServices);

            await PolicyRegistry.Get<IAsyncPolicy>(PolicyNames.RetryPolicyAsync).ExecuteAsync(() => Task.WhenAll(
                modManager.GetServerSettingsAsync(),
                modManager.CreateModulesAsync(),
                modManager.GetAuthPlayersAsync()
            ));

            return modManager;
        }

        public T GetModule<T>() where T : IModule
        {
            return (T)_moduleResolver.Modules[typeof(T)];
        }

        public T GetGlobalService<T>() where T : notnull
        {
            return _globalServices.GetRequiredService<T>();
        }

        private void BuildConfiguration()
        {
            var profile = ProjectContext.GetEnvironmentName();
            var configPath = Path.Combine(ProjectContext.GetDirectory(), "Configuration");
            var builder = new ConfigurationBuilder()
                .SetBasePath(configPath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsettings.{profile}.json", optional: false, reloadOnChange: false)
                .AddJsonFile("appsecrets.json", optional: false, reloadOnChange: false)
                .AddJsonFile($"appsecrets.{profile}.json", optional: false, reloadOnChange: false);

            Configuration = builder.Build();
        }

        private void ConfigureDependencies()
        {
            var serviceCollection = new ServiceCollection();

            // Raven
            //serviceCollection.AddSingleton(DocumentStoreHolder.Store);

            // Options
            var geoipConfig = Configuration.GetSection("Geoip");
            serviceCollection.AddOptions();
            serviceCollection.Configure<TwitterConfig>(Configuration.GetSection("Twitter"));
            serviceCollection.Configure<MashapeConfig>(Configuration.GetSection("Mashape"));
            serviceCollection.Configure<DiscordConfig>(Configuration.GetSection("Discord"));
            serviceCollection.Configure<LogConfig>(Configuration.GetSection("ServerSettings"));
            serviceCollection.Configure<GeoipConfig>(geoipConfig);

            // Services
            var connectionString = Configuration.GetConnectionString("BF2DB");
            //var connectionString = Configuration.GetValue<DatabaseConfig>("Database").ConnectionStrings.BF2DB;
            serviceCollection.AddSingleton(_gameServer);
            //serviceCollection.AddSingleton<IScriptRepository>(c => new SqlScriptRepository(c.GetRequiredService<DatabaseConfig>().ConnectionStrings.BF2DB));
            serviceCollection.AddSingleton<IMapRepository>(c => new SqlMapRepository(connectionString));
            serviceCollection.AddSingleton<IServerSettingsRepository>(c => new ServerSettingsRepository(connectionString));
            //serviceCollection.AddSingleton<IServerSettingsRepository>(c => new FakeServerSettingsRepository());
            serviceCollection.AddSingleton<IMatchRepository>(c => new MatchRepository(connectionString));
            serviceCollection.AddSingleton<ICountryResolver>(c => new CountryResolver(geoipConfig["DatabasePath"]));
            serviceCollection.AddSingleton<IReadOnlyPolicyRegistry<string>>(PolicyRegistry);
            serviceCollection.AddSingleton<IChatLogger, DiscordChatLogger>();
            serviceCollection.AddSingleton<IMediator>(c => Mediator);
            serviceCollection.AddSingleton<IHubContext<ServerHub, IServerHubClient>>(sp => _globalServices.GetRequiredService<IHubContext<ServerHub, IServerHubClient>>());
            serviceCollection.AddSingleton<MassTransit.IBus>(sp => _globalServices.GetRequiredService<MassTransit.IBus>());
            serviceCollection.AddSingleton<IGameStreamService>(sp => _globalServices.GetRequiredService<RabbitMqGameStreamService>());
            //serviceCollection.AddSingleton<IGameStreamService>(sp => _globalServices.GetRequiredService<RedisGameStreamService>());

            // Modules
            foreach (var moduleType in _moduleResolver.ModuleTypes)
            {
                serviceCollection.AddSingleton(moduleType);
            }

            _services = serviceCollection.BuildServiceProvider();
        }

        private async Task CreateModulesAsync()
        {
            var serverSettingsRepository = _services.GetService<IServerSettingsRepository>();
            var moduleNames = await serverSettingsRepository.GetModsAsync(_gameServer.Id);
            var enabledModuleTypes = _moduleResolver.ModuleTypes.Where(t => moduleNames.Contains(t.Name));

            // Instantiate modules
            foreach (var moduleType in enabledModuleTypes)
            {
                try
                {
                    var moduleInstance = (IModule)_services.GetRequiredService(moduleType);
                    _moduleResolver.Modules.Add(moduleType, moduleInstance);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Resolve module error");
                    throw;
                }
            }
        }

        private async Task GetServerSettingsAsync()
        {
            var serverSettingsRepository = _services.GetService<IServerSettingsRepository>();
            ServerSettings = await serverSettingsRepository.GetServerAsync(_gameServer.Id);
        }

        public async Task GetAuthPlayersAsync()
        {
            var serverSettingsRepository = _services.GetService<IServerSettingsRepository>();
            var authPlayers = (await serverSettingsRepository.GetPlayerAuthAsync(_gameServer.Id)).ToList();

            // Add dummy admins so we can send commands from Discord
            foreach (var group in authPlayers.GroupBy(p => p.ServerGroup).Select(g => g.Key))
            {
                authPlayers.Add(new ServerPlayerAuth { AuthLevel = (int)Auth.God, PlayerHash = DiscordModule.DiscordBotHashGod, ServerGroup = group });
                authPlayers.Add(new ServerPlayerAuth { AuthLevel = (int)Auth.SuperAdmin, PlayerHash = DiscordModule.DiscordBotHashSuperAdmin, ServerGroup = group });
                authPlayers.Add(new ServerPlayerAuth { AuthLevel = (int)Auth.Admin, PlayerHash = DiscordModule.DiscordBotHashAdmin, ServerGroup = group });
                authPlayers.Add(new ServerPlayerAuth { AuthLevel = (int)Auth.God, PlayerHash = WebModule.WebAdminHashGod, ServerGroup = group });
            }

            AuthPlayers = authPlayers.ToLookup(p => p.PlayerHash);
        }

        public async Task HandleFakeChatMessageAsync(Message message)
        {
            await HandleChatMessageAsync(message);
        }

        public ValueTask HandleChatMessageAsync(Message message)
        {
            if (message.Type != MessageType.Player)
                return ValueTask.CompletedTask;

            // Cleanup *DEAD*
            message.Text = Regex.Replace(message.Text, @"^\*[^\*]+\*", string.Empty, RegexOptions.Compiled);
            if (message.Text.StartsWith(CommandPrefix))
                return HandleCommandAsync(message);

            return ValueTask.CompletedTask;
        }

        private async ValueTask HandleCommandAsync(Message message)
        {
            // TODO: rate limit low users
            var cmd = message.Text[CommandPrefix.Length..];
            var parts = cmd.Split(' ');
            var alias = parts[0];
            var parameters = parts.Skip(1).ToArray();

            // Unknown command
            if (!_moduleResolver.CommandParsers.ContainsKey(alias))
                return;

            // Try to parse the message into different commands
            var insufficientPermissionsCount = 0;
            var handledCount = 0;
            var parsers = _moduleResolver.CommandParsers[alias];
            foreach (var parser in parsers)
            {
                try
                {
                    var command = parser(parameters);
                    var commandType = command.GetType();

                    // Check if player is authorized for this command
                    var playerAuthLevel = AuthPlayers[message.Player.Hash].FirstOrDefault()?.AuthLevel ?? (int)Auth.All;
                    var requiredAuthLevel = (int)_moduleResolver.AuthLevels[commandType];
                    if (playerAuthLevel < requiredAuthLevel)
                    {
                        insufficientPermissionsCount++;
                        continue;
                    }

                    // TODO: move the below part to Mediator?
                    // Append the message if it's a BaseCommand
                    if (command is BaseCommand baseCommand)
                        baseCommand.Message = message;
                    else
                        Log.Warning("{CommandName} is not a {BaseCommand}", commandType.Name, nameof(BaseCommand));

                    await Mediator.HandleAsync(command);
                }
                catch (CommandArgumentCountException ex)
                {
                    //Logger.LogError(ex, $"{parser} Wrong argument count: {ex.Message}");
                    Log.Debug("Wrong argument count: {Message}", ex.Message);
                }
                catch (FormatException ex)
                {
                    Log.Error(ex, "{Parser} Format exception: {ExceptionMessage}", parser, ex.Message);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "{Parser} Command handle exception: {ExceptionMessage}", parser, ex.Message);
                }
            }

            if (insufficientPermissionsCount > 0 && handledCount == 0)
            {
                _gameServer.GameWriter.SendText("Insufficient permissions");
            }
        }
    }
}
