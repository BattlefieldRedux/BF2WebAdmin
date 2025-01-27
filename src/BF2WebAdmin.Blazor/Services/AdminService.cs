﻿using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace BF2WebAdmin.Blazor.Services;

public class AdminService : IAsyncDisposable
{
    private readonly NavigationManager _navigationManager;
    private readonly HttpClient _httpClient;

    public HubConnection HubConnection { get; private set; }
    public string? SelectedServerId { get; private set; }
    public bool IsAuthenticated { get; set; }

    public AdminService(NavigationManager navigationManager, HttpClient httpClient)
    {
        _navigationManager = navigationManager;
        _httpClient = httpClient;
        HubConnection = new HubConnectionBuilder()
            .WithUrl(navigationManager.ToAbsoluteUri("/hubs/server"))
            .WithAutomaticReconnect()
            //.AddMessagePackProtocol()
            .AddJsonProtocol(options =>
            {
                options.PayloadSerializerOptions.PropertyNamingPolicy = null;
            })
            .Build();
    }

    public async Task<bool> LoginAsync(string username, string password)
    {
        var loginUrl = new Uri($"/api/login?username={Uri.EscapeDataString(username)}&password={Uri.EscapeDataString(password)}", UriKind.Relative);
        var response = await _httpClient.PostAsync(loginUrl, null);
        IsAuthenticated = response.IsSuccessStatusCode;
        return response.IsSuccessStatusCode;
    }

    public async Task ConnectAsync()
    {
        if (HubConnection.State is HubConnectionState.Connecting or HubConnectionState.Reconnecting)
            return;

        try
        {
            if (HubConnection.State == HubConnectionState.Disconnected)
                await HubConnection.StartAsync();
        }
        catch (HttpRequestException ex)
        {
            if (ex.StatusCode == HttpStatusCode.Unauthorized)
                _navigationManager.NavigateTo("/login", forceLoad: false);

            return;
        }

        IsAuthenticated = true;

        // Just used to get server data atm
        await HubConnection.SendAsync("UserConnect");
    }

    public async Task SelectServerAsync(string? serverId)
    {
        if (serverId is null)
            return;

        serverId = UrlDecodeServerId(serverId);
        SelectedServerId = serverId;
        await HubConnection.SendAsync("SelectServer", serverId);
    }

    public async Task DeselectServerAsync(string? serverId)
    {
        if (serverId is null)
            return;

        serverId = UrlDecodeServerId(serverId);
        if (SelectedServerId == serverId)
        {
            SelectedServerId = null;
        }

        await HubConnection.SendAsync("DeselectServer", serverId);
    }

    public async Task SendChatMessageAsync(string text)
    {
        await HubConnection.SendAsync("SendChatMessage", SelectedServerId, text);
    }

    public async Task SendRconCommandAsync(string text)
    {
        await HubConnection.SendAsync("SendRconCommand", SelectedServerId, text);
    }

    public async Task SendCustomCommandAsync(string text)
    {
        await HubConnection.SendAsync("SendCustomCommand", SelectedServerId, text);
    }

    public async ValueTask DisposeAsync()
    {
        await HubConnection.DisposeAsync();
    }

    public static string UrlEncodeServerId(string serverId)
    {
        return serverId.Replace(".", "-").Replace(":", "_");
    }

    public static string UrlDecodeServerId(string encodedServerId)
    {
        return encodedServerId.Replace("-", ".").Replace("_", ":");
    }
}
