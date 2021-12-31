﻿using System.Net;
using BF2WebAdmin.Common;
using BF2WebAdmin.Common.Entities.Game;

namespace BF2WebAdmin.Server.Abstractions
{
    public interface IGameServer
    {
        DateTime StartTime { get; }

        IModManager ModManager { get; }
        IGameWriter GameWriter { get; }
        ServerInfo ServerInfo { get; }

        string Id { get; }
        string Name { get; }
        IPAddress IpAddress { get; }
        int GamePort { get; }
        int QueryPort { get; }
        int RconPort { get; }
        int MaxPlayers { get; }
        GameState State { get; }
        SocketState SocketState { get; }

        Map Map { get; }
        IEnumerable<Map> Maps { get; }
        IEnumerable<Team> Teams { get; }
        IEnumerable<Player> Players { get; }
        IEnumerable<Vehicle> Vehicles { get; }
        IEnumerable<Projectile> Projectiles { get; }
        IEnumerable<Message> Messages { get; }

        //event Action<string, int, int, int> ServerUpdate;
        //event Action<GameState> GameStateChanged;
        //event Action<SocketState> SocketStateChanged;
        //event Action<IEnumerable<Map>> MapsChanged;
        //event Action<Map> MapChanged;
        //event Action<Message> ChatMessage;
        //event Action<Player> PlayerJoin;
        //event Action<Player> PlayerLeft;
        //event Action<Player, Position, Rotation> PlayerSpawn;
        //event Action<Player, Position, Player, Position, string> PlayerKill;
        //event Action<Player, Position> PlayerDeath;
        //event Action<Player, Vehicle> PlayerVehicle;
        //event Action<Player, Team> PlayerTeam;
        //event Action<Player, int, int, int, int> PlayerScore;
        //event Action<Player, Position, Rotation, int> PlayerPosition;
        //event Action<Projectile, Position, Rotation> ProjectilePosition;

        event Func<string, int, int, int, Task> ServerUpdate;
        event Func<GameState, Task> GameStateChanged;
        event Func<SocketState, Task> SocketStateChanged;
        event Func<IEnumerable<Map>, Task> MapsChanged;
        event Func<Map, Task> MapChanged;
        event Func<Message, Task> ChatMessage;
        event Func<Player, Task> PlayerJoin;
        event Func<Player, Task> PlayerLeft;
        event Func<Player, Position, Rotation, Task> PlayerSpawn;
        event Func<Player, Position, Player, Position, string, Task> PlayerKill;
        event Func<Player, Position, bool, Task> PlayerDeath;
        event Func<Player, Vehicle, Task> PlayerVehicle;
        event Func<Player, Team, Task> PlayerTeam;
        event Func<Player, int, int, int, int, Task> PlayerScore;
        event Func<Player, Position, Rotation, int, Task> PlayerPosition;
        event Func<Projectile, Position, Rotation, Task> ProjectilePosition;

        Task UpdateServerInfoAsync(string name, int gamePort, int queryPort, int maxPlayers);
        Task UpdateGameStateAsync(GameState state);
        Task UpdateSocketStateAsync(SocketState state);
        Task SetReconnectedAsync(IGameWriter gameWriter);
        Task UpdateMapsAsync(IList<string> maps);
        Task UpdateMapAsync(string mapName, string team1Name, string team2Name);
        Task AddPlayerAsync(Player player);
        Task UpdatePlayerAsync(Player player, Position position, Rotation rotation, int ping);
        Task UpdatePlayerVehicleAsync(Player player, Vehicle vehicle, string subVehicleTemplate);
        Task UpdatePlayerTeamAsync(Player player, int teamId);
        Task UpdatePlayerScoreAsync(Player player, int teamScore, int kills, int deaths, int totalScore);
        Task UpdateProjectileAsync(Projectile projectile, Position position, Rotation rotation);
        Task RemovePlayerAsync(Player player);
        Task SetPlayerSpawnAsync(Player player, Position position, Rotation rotation);
        Task SetPlayerKillAsync(Player attacker, Position attackerPosition, Player victim, Position victimPosition, string weapon);
        Task SetPlayerDeathAsync(Player player, Position position);
        Task AddMessageAsync(Message message);
        void SetRconResponse(string responseCode, string value);
        Vehicle GetVehicle(Player player, int rootVehicleId, string rootVehicleName, string vehicleName);
        Player GetPlayer(int index);
        Player GetPlayer(string namePart);
        Projectile GetProjectile(int id, string templateName, Position position);
    }
}
