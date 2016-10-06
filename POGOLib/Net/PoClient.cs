using DankMemes.GPSOAuthSharp;
using log4net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using POGOLib.Net.Data;
using POGOLib.Net.Data.Login;
using POGOLib.Pokemon;
using POGOLib.Util;
using POGOProtos.Data;
using POGOProtos.Inventory;
using POGOProtos.Inventory.Item;
using POGOProtos.Networking.Responses;
using POGOProtos.Settings;
using POGOProtos.Settings.Master;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static POGOProtos.Networking.Responses.DownloadItemTemplatesResponse.Types;

namespace POGOLib.Net
{
    public class PoClient
    {
        private readonly ILog Log;

        private List<ItemTemplate> _itemTemplates;

        private GetMapObjectsResponse _mapObjects;

        private int? _heartBeatingInterval = null;

        public PoClient(BotSettings s)
        {
            Log = LogManager.GetLogger(s.FriendlyName);
            //Settings = s;
            Uid = HashUtil.HashMD5(s.Username + s.LoginProvider).ToLower();
            _heartBeatingInterval = s.HeartBeatInterval;
            ClientData = new ClientData
            {
                Username = s.Username,
                Password = s.Password,
                LoginProvider = s.LoginProvider
            };

            Authenticated += OnAuthenticated;
        }

        public event EventHandler MapObjectUpdate;

        private event EventHandler Authenticated;

        public ClientData ClientData { get; private set; }

        /// <summary>
        /// <see cref="GlobalSettings"/> is automatically updated and only accessible after authenticating.
        /// </summary>
        public GlobalSettings GlobalSettings { get; internal set; }

        public bool HeartBeating { get; private set; }

        /// <summary>
        /// <see cref="Inventory"/> is automatically updated and only accessible after authenticating.
        /// </summary>
        public InventoryDelta InitialInventory { get; internal set; }

        public List<PokemonFamily> InventorieCandies
        {
            get
            {
                var candies = Inventory.InventoryItems
                        .Where(i => i.InventoryItemData?.PokemonFamily != null)
                        .Select(i => i.InventoryItemData.PokemonFamily)
                        .ToList();

                return candies;
            }
        }

        /// <summary>
        /// <see cref="Inventory"/> is automatically updated and only accessible after authenticating.
        /// </summary>
        public Inventory Inventory { get; internal set; } = new Inventory();

        public List<AppliedItem> InventoryAppliedItems
        {
            get
            {
                var items = Inventory.InventoryItems
                    .Where(i => i?.InventoryItemData?.AppliedItems != null)
                    .SelectMany(i => i.InventoryItemData.AppliedItems.Item)
                    .ToList();

                return items;
            }
        }

        public List<EggIncubator> InventoryEggIncubators
        {
            get
            {
                var incubators = Inventory.InventoryItems
                    .Where(i => i.InventoryItemData?.EggIncubators != null)
                    .SelectMany(i => i.InventoryItemData.EggIncubators.EggIncubator)
                    .ToList();

                return incubators;
            }
        }

        public bool PokeballsAvailable()
        {
            return PokeballsCount > 0;
        }

        public int PokeballsCount
        {
            get
            {
                return InventoryItems.Where(i => i.ItemId.ToString().ToLowerInvariant().Contains("ball")).Select(i => i.Count).Sum();
            }
        }

        public List<PokemonData> InventoryEggs
        {
            get
            {
                var pokemons = Inventory.InventoryItems
                    .Where(i => i.InventoryItemData?.PokemonData != null
                        && i.InventoryItemData.PokemonData.IsEgg == true)
                    .Select(i => i.InventoryItemData.PokemonData)
                    .ToList();

                return pokemons;
            }
        }

        public List<ItemData> InventoryItems
        {
            get
            {
                var items = Inventory.InventoryItems
                    .Where(i => i?.InventoryItemData?.Item != null)
                    .Select(i => i.InventoryItemData.Item)
                    .ToList();

                return items;
            }
        }

        public List<PokemonData> InventoryPokemons
        {
            get
            {
                var pokemons = Inventory.InventoryItems
                    .Where(i => i.InventoryItemData?.PokemonData != null
                        && i.InventoryItemData.PokemonData.IsEgg == false)
                    .Select(i => i.InventoryItemData.PokemonData)
                    .ToList();

                return pokemons;
            }
        }

        public List<ItemTemplate> ItemTemplates
        {
            get
            {
                return _itemTemplates ?? (_itemTemplates = RpcClient.DownloadItemTemplates().ItemTemplates.ToList());
            }
        }

        /// <summary>
        /// <see cref="MapObjects"/> is automatically updated and only accessible after authenticating.
        /// </summary>
        public GetMapObjectsResponse MapObjects
        {
            get
            {
                return _mapObjects;
            }
            internal set
            {
                _mapObjects = value;
                OnMapObjectUpdate();
            }
        }

        public POGOProtos.Data.Player.PlayerStats PlayerStats
        {
            get
            {
                var playerStarts = Inventory.InventoryItems
                    .Where(i => i?.InventoryItemData?.PlayerStats != null)
                    .Single()
                    .InventoryItemData.PlayerStats;

                return playerStarts;
            }
        }

        public List<PokemonSettings> PokemonSettings
        {
            get
            {
                return ItemTemplates.Where(t => t.PokemonSettings != null).Select(t => t.PokemonSettings).ToList();
            }
        }

        /// <summary>
        /// <see cref="RpcClient"/> is used to manually send rpc requests to pokemon, use with caution.
        /// </summary>
        public RpcClient RpcClient { get; private set; }

        public string Uid { get; }

        public bool Authenticate()
        {
            if (ClientData.LoginProvider == LoginProvider.PokemonTrainerClub)
            {
                using (var httpClientHandler = new HttpClientHandler())
                {
                    httpClientHandler.AllowAutoRedirect = false;

                    using (var httpClient = new HttpClient(httpClientHandler))
                    {
                        httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(Configuration.LoginUserAgent);

                        var loginData = GetLoginDataAsync(httpClient).Result;
                        var ticket = PostLoginAsync(httpClient, loginData, ClientData.Password).Result;

                        if (ticket == null)
                            return false;

                        ClientData.AuthData = PostLoginOauthAsync(httpClient, ticket).Result;
                        OnAuthenticated(EventArgs.Empty);

                        return true;
                    }
                }
            }

            if (ClientData.LoginProvider == LoginProvider.GoogleAuth)
            {
                var googleClient = new GPSOAuthClient(ClientData.Username, ClientData.Password);
                var masterLoginResponse = googleClient.PerformMasterLogin();

                if (masterLoginResponse.ContainsKey("Error") && masterLoginResponse["Error"] == "BadAuthentication")
                    return false;

                if (!masterLoginResponse.ContainsKey("Token"))
                    throw new Exception("Token was missing from master login response.");

                var oauthResponse = googleClient.PerformOAuth(masterLoginResponse["Token"], Configuration.GoogleAuthService, Configuration.GoogleAuthApp, Configuration.GoogleAuthClientSig);

                if (!oauthResponse.ContainsKey("Auth"))
                    throw new Exception("Auth token was missing from oauth login response.");

                ClientData.AuthData = new AuthData
                {
                    AccessToken = oauthResponse["Auth"],
                    ExpireDateTime = TimeUtil.GetDateTimeFromS(int.Parse(oauthResponse["Expiry"]))
                };
                OnAuthenticated(EventArgs.Empty);

                return true;
            }

            throw new Exception("Unknown login provider.");
        }

        public GpsData GetGpsData()
        {
            return ClientData.GpsData;
        }

        public bool HasGpsData()
        {
            return ClientData.GpsData != null;
        }

        public bool LoadClientData()
        {
            var saveDataPath = Path.Combine(Environment.CurrentDirectory, "savedata", $"{Uid}.json");

            if (!File.Exists(saveDataPath))
                return false;

            ClientData = JsonConvert.DeserializeObject<ClientData>(File.ReadAllText(saveDataPath));

            if (!(ClientData.AuthData.ExpireDateTime > DateTime.UtcNow))
                return false;

            OnAuthenticated(EventArgs.Empty);

            return true;
        }

        public void SaveClientData()
        {
            File.WriteAllText(Path.Combine(Environment.CurrentDirectory, "savedata", $"{Uid}.json"), JsonConvert.SerializeObject(ClientData, Formatting.Indented));
        }

        public void SetGpsData(GpsData gpsData)
        {
            ClientData.GpsData = gpsData;
        }

        public void SetGpsData(double latitude, double longitude, double altitude = 50.0)
        {
            if (!HasGpsData())
            {
                ClientData.GpsData = new GpsData
                {
                    Latitude = latitude,
                    Longitude = longitude,
                    Altitude = altitude
                };
            }
            else
            {
                ClientData.GpsData.Latitude = latitude;
                ClientData.GpsData.Longitude = longitude;
                ClientData.GpsData.Altitude = altitude;
            }
        }

        public void StartHeartbeats()
        {
            if (HeartBeating)
            {
                Log.Debug("Heartbeating has already been started.");
                return;
            }

            if (GlobalSettings == null)
            {
                RpcClient.Heartbeat(); // Forcekick

                if (GlobalSettings == null)
                    throw new Exception("Couldn't fetch settings.");
            }

            HeartBeating = true;

            new Thread(() =>
            {
                var sleepTime = _heartBeatingInterval != null ? (int)_heartBeatingInterval * 1000 : Convert.ToInt32(GlobalSettings.MapSettings.GetMapObjectsMinRefreshSeconds) * 1000;

                while (HeartBeating)
                {
                    RpcClient.Heartbeat();

                    Thread.Sleep(sleepTime);
                }
            })
            { IsBackground = true }.Start();
        }

        public void StopHeartbeats()
        {
            if (!HeartBeating)
            {
                Log.Debug("Heartbeating has already been stopped.");
                return;
            }

            HeartBeating = false;
        }

        private async Task<LoginData> GetLoginDataAsync(HttpClient httpClient)
        {
            var loginDataResponse = await httpClient.GetAsync(Configuration.LoginUrl);
            var loginData = JsonConvert.DeserializeObject<LoginData>(await loginDataResponse.Content.ReadAsStringAsync());

            return loginData;
        }

        private void OnAuthenticated(object sender, EventArgs eventArgs)
        {
            RpcClient = new RpcClient(this, Log.Logger.Name);
            StartHeartbeats();
        }

        private void OnAuthenticated(EventArgs e)
        {
            Authenticated?.Invoke(this, e);
        }

        private void OnMapObjectUpdate()
        {
            MapObjectUpdate?.Invoke(this, EventArgs.Empty);
        }

        private async Task<string> PostLoginAsync(HttpClient httpClient, LoginData loginData, string password)
        {
            var loginResponse = await httpClient.PostAsync(Configuration.LoginUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"lt", loginData.Lt},
                {"execution", loginData.Execution},
                {"_eventId", "submit"},
                {"username", ClientData.Username},
                {"password", password}
            }));

            var loginResponseDataRaw = await loginResponse.Content.ReadAsStringAsync();
            if (!loginResponseDataRaw.Contains("{"))
            {
                var locationQuery = loginResponse.Headers.Location.Query;
                var ticketStartPosition = locationQuery.IndexOf("=", StringComparison.Ordinal) + 1;
                return locationQuery.Substring(ticketStartPosition, locationQuery.Length - ticketStartPosition);
            }

            var loginResponseData = JObject.Parse(loginResponseDataRaw);
            var loginResponseErrors = (JArray)loginResponseData["errors"];

            foreach (var loginResponseError in loginResponseErrors)
            {
                Log.Debug($"Login error: '{loginResponseError}'");
            }

            return null;
        }

        private async Task<AuthData> PostLoginOauthAsync(HttpClient httpClient, string ticket)
        {
            var loginResponse = await httpClient.PostAsync(Configuration.LoginOauthUrl, new FormUrlEncodedContent(new Dictionary<string, string>
            {
                {"client_id", "mobile-app_pokemon-go"},
                {"redirect_uri", "https://www.nianticlabs.com/pokemongo/error"},
                {"client_secret", "w8ScCUXJQc6kXKw8FiOhd8Fixzht18Dq3PEVkUCP5ZPxtgyWsbTvWHFLm2wNY0JR"},
                {"grant_type", "refresh_token"},
                {"code", ticket}
            }));

            var loginResponseDataRaw = await loginResponse.Content.ReadAsStringAsync();

            var oAuthData = Regex.Match(loginResponseDataRaw, "access_token=(?<accessToken>.*?)&expires=(?<expires>\\d+)");
            if (!oAuthData.Success)
                throw new Exception("Couldn't verify the OAuth login response data.");

            return new AuthData
            {
                AccessToken = oAuthData.Groups["accessToken"].Value,
                ExpireDateTime = DateTime.UtcNow.AddSeconds(int.Parse(oAuthData.Groups["expires"].Value))
            };
        }
    }
}