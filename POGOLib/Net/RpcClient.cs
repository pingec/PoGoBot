using Google.Protobuf;
using Google.Protobuf.Collections;
using log4net;
using POGOLib.Pokemon;
using POGOLib.Util;
using POGOProtos.Inventory.Item;
using POGOProtos.Networking.Envelopes;
using POGOProtos.Networking.Requests;
using POGOProtos.Networking.Requests.Messages;
using POGOProtos.Networking.Responses;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using static POGOProtos.Networking.Envelopes.RequestEnvelope.Types.AuthInfo.Types;

namespace POGOLib.Net
{
    public class RpcClient
    {
        private readonly string _apiUrl;
        private readonly HttpClient _httpClient;
        private readonly PoClient _poClient;
        private readonly ILog Log;
        private AuthTicket _authTicket;
        private long _lastInventoryTimestamp;
        private ulong _requestId;
        private string _settingsHash;

        public RpcClient(PoClient poClient, string loggerName)
        {
            Log = LogManager.GetLogger(loggerName);
            _poClient = poClient;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.TryParseAdd(Configuration.ApiUserAgent);
            _httpClient.DefaultRequestHeaders.ExpectContinue = false;
            _requestId = (ulong)new Random().Next(100000000, 999999999);
            _apiUrl = $"https://{GetApiEndpoint()}/rpc";
        }

        //todo: move to POClient.cs
        private ItemId ActivePokeBall { get; set; } = (ItemId)1;

        private ulong RequestId
        {
            get
            {
                _requestId = _requestId + 1;
                return _requestId;
            }
        }

        public LevelUpRewardsResponse AcceptLevelUpRewards(int level)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.LevelUpRewards,
                RequestMessage = new LevelUpRewardsMessage
                {
                    Level = level
                }.ToByteString()
            });

            return LevelUpRewardsResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public CatchPokemonResponse CatchPokemon(ulong encounterId, string spawnPointGuid)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.CatchPokemon,
                RequestMessage = new CatchPokemonMessage
                {
                    EncounterId = encounterId,
                    //Pokeball = (int)POGOProtos.Inventory.ItemId.ItemPokeBall,
                    Pokeball = ActivePokeBall,
                    SpawnPointId = spawnPointGuid,
                    HitPokemon = true,
                    NormalizedReticleSize = 1.999364d,
                    SpinModifier = .946474d,
                    NormalizedHitPosition = 1d
                }.ToByteString()
            });

            return CatchPokemonResponse.Parser.ParseFrom(response.Returns[0]);
        }

        //todo: move to POClient.cs
        public void ChangePokeBallType()
        {
            // possible values are 1 2 3
            ActivePokeBall = (ItemId)(((int)ActivePokeBall) % 3 + 1);
        }

        public DiskEncounterResponse DiskEncounter(ulong encounterId, string fortId)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.DiskEncounter,
                RequestMessage = new DiskEncounterMessage
                {
                    EncounterId = encounterId,
                    FortId = fortId,
                    PlayerLatitude = _poClient.ClientData.GpsData.Latitude,
                    PlayerLongitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return DiskEncounterResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public DiskEncounterResponse DiskEncounter2(string fortId)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.DiskEncounter,
                RequestMessage = new DiskEncounterMessage
                {
                    FortId = fortId,
                    PlayerLatitude = _poClient.ClientData.GpsData.Latitude,
                    PlayerLongitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return DiskEncounterResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public DownloadItemTemplatesResponse DownloadItemTemplates()
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.DownloadItemTemplates,
            });

            return DownloadItemTemplatesResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public EncounterResponse EncounterPokemon(ulong encounterId, string spawnPointGuid)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.Encounter,
                RequestMessage = new EncounterMessage
                {
                    EncounterId = encounterId,
                    SpawnPointId = spawnPointGuid,
                    PlayerLatitude = _poClient.ClientData.GpsData.Latitude,
                    PlayerLongitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return EncounterResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public EvolvePokemonResponse EvolvePokemon(ulong pokemonId)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.EvolvePokemon,
                RequestMessage = new EvolvePokemonMessage
                {
                    PokemonId = pokemonId
                }.ToByteString()
            });

            return EvolvePokemonResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public FortDetailsResponse FortDetails(string fortId)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.FortDetails,
                RequestMessage = new FortDetailsMessage
                {
                    FortId = fortId,
                    Latitude = _poClient.ClientData.GpsData.Latitude,
                    Longitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return FortDetailsResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public FortSearchResponse FortSearch(string fortId, double fortLatitude, double fortLongitude)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.FortSearch,
                RequestMessage = new FortSearchMessage
                {
                    FortId = fortId,
                    FortLatitude = fortLatitude,
                    FortLongitude = fortLongitude,
                    PlayerLatitude = _poClient.ClientData.GpsData.Latitude,
                    PlayerLongitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return FortSearchResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public GetPlayerResponse GetProfile()
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.GetPlayer
            });
            return GetPlayerResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public void Heartbeat()
        {
            try
            {
                _poClient.MapObjects = GetMapObjects();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Invalid URI"))
                {
                    //server problems, cannot recover from this
                }
            }
        }

        public List<RecycleInventoryItemResponse> RecycleFortAwardedItems(RepeatedField<ItemAward> awardedItems)
        {
            var requests = new List<Request>();
            foreach (var item in awardedItems)
            {
                var itemName = Enum.GetName(typeof(POGOProtos.Inventory.Item.ItemId), item.ItemId);
                if (itemName.Contains("Ball") || itemName.Contains("Egg"))
                    continue;

                var request = new Request
                {
                    RequestType = RequestType.RecycleInventoryItem,
                    RequestMessage = new RecycleInventoryItemMessage
                    {
                        ItemId = item.ItemId,
                        Count = item.ItemCount
                    }.ToByteString()
                };
                requests.Add(request);
            }

            var response = SendRemoteProtocolCall(_apiUrl, requests.ToArray());

            var responseList = new List<RecycleInventoryItemResponse>();
            for (int i = 0; i < requests.Count; i++)
            {
                responseList.Add(RecycleInventoryItemResponse.Parser.ParseFrom(response.Returns[i]));
            }

            return responseList;
        }

        public RecycleInventoryItemResponse RecycleInventoryItem(POGOProtos.Inventory.Item.ItemId itemId, int count)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.RecycleInventoryItem,
                RequestMessage = new RecycleInventoryItemMessage
                {
                    ItemId = itemId,
                    Count = count
                }.ToByteString()
            });

            return RecycleInventoryItemResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public ReleasePokemonResponse TransferPokemon(ulong pokemonId)
        {
            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.ReleasePokemon,
                RequestMessage = new ReleasePokemonMessage
                {
                    PokemonId = pokemonId
                }.ToByteString()
            });

            return ReleasePokemonResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public UseItemEggIncubatorResponse UseItemEggIncubator(string incubatorId, ulong eggId)
        {
            var request = new Request
            {
                RequestType = RequestType.UseItemEggIncubator,
                RequestMessage = new UseItemEggIncubatorMessage
                {
                    ItemId = incubatorId,
                    PokemonId = eggId
                }.ToByteString()
            };

            var response = SendRemoteProtocolCall(_apiUrl, request);

            return UseItemEggIncubatorResponse.Parser.ParseFrom(response.Returns[0]);
        }

        public UseItemXpBoostResponse UseItemXpBoost(ItemId itemId)
        {
            var request = new Request
            {
                RequestType = RequestType.UseItemXpBoost,
                RequestMessage = new UseItemXpBoostMessage
                {
                    ItemId = itemId
                }.ToByteString()
            };

            var response = SendRemoteProtocolCall(_apiUrl, request);

            return UseItemXpBoostResponse.Parser.ParseFrom(response.Returns[0]);
        }

        private string GetApiEndpoint()
        {
            ResponseEnvelope response;

            //It is critical that this response is valid, so in case it is not, try to recover
            var maxAttempts = 5;
            var failCount = 0;
            do
            {
                response = SendRemoteProtocolCall(Configuration.ApiUrl, new Request
                {
                    RequestType = RequestType.GetPlayer
                });
            }
            while (!IsResponseApiUrlValid(response) && failCount++ < maxAttempts);

            if (!IsResponseApiUrlValid(response))
            {
                throw new POGOLibException("Could not retrieve valid ApiUrl from POGO server response, this is a critical failure.");
            }

            return response.ApiUrl;
        }

        private IEnumerable<Request> GetDefaultRequests()
        {
            return new[]
            {
                new Request
                {
                    RequestType = RequestType.GetHatchedEggs
                },
                new Request
                {
                    RequestType = RequestType.GetInventory,
                    RequestMessage = new GetInventoryMessage
                    {
                       //LastTimestampMs = _lastInventoryTimestamp
                       LastTimestampMs = _poClient.Inventory.LastInventoryTimestampMs
                    }.ToByteString()
                },
                new Request
                {
                    RequestType = RequestType.CheckAwardedBadges
                },
                new Request
                {
                    RequestType = RequestType.DownloadSettings,
                    RequestMessage = new DownloadSettingsMessage
                    {
                        Hash = "4a2e9bc330dae60e7b74fc85b98868ab4700802e"
                    }.ToByteString()
                }
            };
        }

        private GetMapObjectsResponse GetMapObjects()
        {
            var cellIds = MapUtil.GetCellIdsForLatLong(_poClient.ClientData.GpsData.Latitude, _poClient.ClientData.GpsData.Longitude);
            var sinceTimeMs = new List<long>(cellIds.Length);

            for (var i = 0; i < cellIds.Length; i++)
                sinceTimeMs.Add(0);

            var response = SendRemoteProtocolCall(_apiUrl, new Request
            {
                RequestType = RequestType.GetMapObjects,
                RequestMessage = new GetMapObjectsMessage
                {
                    CellId = { cellIds },
                    SinceTimestampMs = { sinceTimeMs.ToArray() },
                    Latitude = _poClient.ClientData.GpsData.Latitude,
                    Longitude = _poClient.ClientData.GpsData.Longitude
                }.ToByteString()
            });

            return GetMapObjectsResponse.Parser.ParseFrom(response.Returns[0]);
        }

        private bool IsResponseApiUrlValid(ResponseEnvelope response)
        {
            return !string.IsNullOrEmpty(response.ApiUrl);
        }

        //public async Task<FortDetailResponse> GetFort(string fortId, double fortLat, double fortLng)
        //{
        //    var customRequest = new Request.Types.FortDetailsRequest()
        //    {
        //        Id = ByteString.CopyFromUtf8(fortId),
        //        Latitude = Utils.FloatAsUlong(fortLat),
        //        Longitude = Utils.FloatAsUlong(fortLng),
        //    };

        //    var fortDetailRequest = RequestBuilder.GetRequest(_unknownAuth, _currentLat, _currentLng, 10,
        //        new Request.Types.Requests()
        //        {
        //            Type = (int)RequestType.FORT_DETAILS,
        //            Message = customRequest.ToByteString()
        //        });
        //    return await _httpClient.PostProto<Request, FortDetailResponse>($"https://{_apiUrl}/rpc", fortDetailRequest);
        //}

        ///*num Holoholo.Rpc.Types.FortSearchOutProto.Result {
        // NO_RESULT_SET = 0;
        // SUCCESS = 1;
        // OUT_OF_RANGE = 2;
        // IN_COOLDOWN_PERIOD = 3;
        // INVENTORY_FULL = 4;
        //}*/
        private ResponseEnvelope SendRemoteProtocolCall(string apiUrl, Request request)
        {
            return SendRemoteProtocolCall(apiUrl, new Request[] { request });
        }

        private ResponseEnvelope SendRemoteProtocolCall(string apiUrl, params Request[] requests)
        {
            if (!_poClient.HasGpsData())
                throw new Exception("No gps data has been set, can't send a rpc call.");

            //var requestEnvelope = new RequestEnvelope
            //{
            //    StatusCode = 2,
            //    RequestId = RequestId,
            //    Latitude = _poClient.ClientData.GpsData.Latitude,
            //    Longitude = _poClient.ClientData.GpsData.Longitude,
            //    Altitude = _poClient.ClientData.GpsData.Altitude,
            //    Unknown12 = 123, // TODO: Figure this out.
            //    Requests = { GetDefaultRequests() }
            //};

            var requestEnvelope = new RequestEnvelope
            {
                StatusCode = 2,
                RequestId = RequestId,
                Latitude = _poClient.ClientData.GpsData.Latitude,
                Longitude = _poClient.ClientData.GpsData.Longitude,
                Altitude = _poClient.ClientData.GpsData.Altitude,
                Unknown12 = 989, // TODO: Figure this out.
                Requests = { GetDefaultRequests() }
            };

            if (_authTicket == null)
            {
                requestEnvelope.AuthInfo = new RequestEnvelope.Types.AuthInfo
                {
                    Provider = _poClient.ClientData.LoginProvider == LoginProvider.PokemonTrainerClub ? "ptc" : "google",
                    Token = new JWT
                    {
                        Contents = _poClient.ClientData.AuthData.AccessToken,
                        Unknown2 = 59
                    }
                };
            }
            else
            {
                requestEnvelope.AuthTicket = _authTicket;
            }

            //foreach (var request in requests)
            //{
            //    requestEnvelope.Requests.Insert(0, request);
            //}
            for (int i = requests.Length - 1; i >= 0; i--)
            {
                requestEnvelope.Requests.Insert(0, requests[i]);
            }

            using (var memoryStream = new MemoryStream())
            {
                requestEnvelope.WriteTo(memoryStream);

                using (var response = _httpClient.PostAsync(apiUrl, new ByteArrayContent(memoryStream.ToArray())).Result)
                {
                    if (response.StatusCode == System.Net.HttpStatusCode.BadGateway)
                    {
                        //todo: implement retry
                        var a = "";
                    }

                    var responseBytes = response.Content.ReadAsByteArrayAsync().Result;
                    var responseEnvelope = ResponseEnvelope.Parser.ParseFrom(responseBytes);

                    if (responseEnvelope.StatusCode == 102)
                    {
                        _poClient.Authenticate();
                        var a = "";
                    }

                    if (_authTicket == null && responseEnvelope.AuthTicket != null)
                        _authTicket = responseEnvelope.AuthTicket;

                    Log.Debug($"Received {responseBytes.Length} bytes.");

                    // Problems:
                    // 5 Payloads are received but only the first one (request) is made available.
                    // Fix the other 4.
                    // Also assign to property with a private set and public get.

                    // 0 = request
                    // 1 = GetHatchedEggs
                    // 2 = GetInventory
                    // 3 = CheckAwardedBadges
                    // 4 = DownloadSettings

                    if (responseEnvelope.Returns.Count == 5)
                    {
                        var hatchedEggs = GetHatchedEggsResponse.Parser.ParseFrom(responseEnvelope.Returns[1]);
                        var getInventory = GetInventoryResponse.Parser.ParseFrom(responseEnvelope.Returns[2]);
                        var checkAwardedBadges = CheckAwardedBadgesResponse.Parser.ParseFrom(responseEnvelope.Returns[3]);
                        var downloadSettingsResponse = DownloadSettingsResponse.Parser.ParseFrom(responseEnvelope.Returns[4]);

                        // Used to verify that data is being received correctly.
                        Log.Debug($"\tGetHatchedEggs Size: {responseEnvelope.Returns[1].Length}");
                        Log.Debug($"\tGetInventory Size: {responseEnvelope.Returns[2].Length}");
                        Log.Debug($"\tCheckAwardedBadges Size: {responseEnvelope.Returns[3].Length}");
                        Log.Debug($"\tDownloadSettings Size: {responseEnvelope.Returns[4].Length}");

                        if (downloadSettingsResponse.Settings != null)
                        {
                            if (_poClient.GlobalSettings == null || _settingsHash != downloadSettingsResponse.Hash)
                            {
                                _settingsHash = downloadSettingsResponse.Hash;
                                _poClient.GlobalSettings = downloadSettingsResponse.Settings;
                            }
                            else
                            {
                                _poClient.GlobalSettings = downloadSettingsResponse.Settings;
                            }
                        }

                        if (getInventory.Success)
                        {
                            if (getInventory.InventoryDelta.NewTimestampMs > _lastInventoryTimestamp)
                            {
                                // Inventory has been updated, fire an event or whatever.

                                if (_poClient.InitialInventory == null)
                                {
                                    //todo this has to go
                                    _poClient.InitialInventory = getInventory.InventoryDelta; //todo, implement merging of deltas
                                }

                                _poClient.Inventory.UpdateInventoryItems(getInventory.InventoryDelta);
                                _poClient.Inventory.LastInventoryTimestampMs = getInventory.InventoryDelta.NewTimestampMs;
                                _lastInventoryTimestamp = getInventory.InventoryDelta.NewTimestampMs; //todo: this will go
                            }
                        }
                    }

                    return responseEnvelope;
                }
            }
        }
    }
}