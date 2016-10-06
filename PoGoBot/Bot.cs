using Google.Protobuf.Collections;
using log4net;
using POGOLib.Net;
using POGOLib.Net.Data;
using POGOProtos.Data;
using POGOProtos.Enums;
using POGOProtos.Inventory.Item;
using POGOProtos.Map.Pokemon;
using POGOProtos.Networking.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static Demo.Utils;

namespace Demo
{
    public enum BotState { Chilling, Pokemon, Fort };

    public class Bot
    {
        public readonly PoClient PoClient;
        public readonly BotSettings Settings;

        public BotStats Stats;
        private readonly ILog Log;
        private CancellationTokenSource _cancelTokenSource;

        //saves state (which encounters have been finished and should be ignored), this list should be purged on mapobjects update
        private List<ulong> _processedEncounters = new List<ulong>();

        //saves state (which forts have been processed and should be ignored), this list should be purged on mapobjects update
        private List<string> _processedForts = new List<string>();

        private Dictionary<ulong, string> PokemonEncounters;

        public Bot(PoClient poClient, BotSettings settings)
        {
            PoClient = poClient;
            Log = LogManager.GetLogger(settings.FriendlyName);
            Stats = new BotStats(this, Log);
            PokemonTransferFilter = BuildTransferFilter(settings.MinIvPct, settings.MinCpM, settings.MinCp);
            Settings = settings;
        }

        public Bot(PoClient poClient, BotSettings settings, Dictionary<ulong, string> pokemonEncounters)
        {
            PoClient = poClient;
            Log = LogManager.GetLogger(settings.FriendlyName);
            Stats = new BotStats(this, Log);
            PokemonEncounters = pokemonEncounters;
            PokemonTransferFilter = BuildTransferFilter(settings.MinIvPct, settings.MinCpM, settings.MinCp);
            Settings = settings;
        }

        public Bot(PoClient poClient, BotSettings settings, Func<PokemonData, bool> shouldTransfer)
        {
            PoClient = poClient;
            Log = LogManager.GetLogger(Settings.FriendlyName);
            Stats = new BotStats(this, Log);
            PokemonTransferFilter = shouldTransfer;
            Settings = settings;
        }

        public bool IsWalking { get; private set; }

        public int Level
        {
            get
            {
                return PoClient.PlayerStats.Level;
            }
        }

        public Func<PokemonData, bool> PokemonTransferFilter { get; set; }
        public List<KeyValuePair<DateTime, GpsData>> TravelLog { get; private set; }

        public Task Start()
        {
            var task = new Task(() =>
            {
                while (true)
                {
                    // do not bog down the cpu ~10 ticks per second are plenty
                    Wait(100);
                    // catch inside loop so if exception happens, bot restarts
                    try
                    {
                        switch (Settings.Strategy)
                        {
                            case BotSettings.BotStrategy.CleanUpInventoryPokemons:
                                var pokemons = PoClient.InventoryPokemons.Where(PokemonTransferFilter);
                                var nTPokemons = PoClient.InventoryPokemons.Where(p => !PokemonTransferFilter(p));
                                DoEvolveTransferInventoryPokemons(PokemonTransferFilter);
                                return;

                            case BotSettings.BotStrategy.FarmPokeBalls:
                                FarmPokeBallsTick();
                                break;

                            case BotSettings.BotStrategy.FarmWanderPokestops:
                                WalkPokeStopsAndFarm();
                                break;

                            case BotSettings.BotStrategy.FarmStandStill:
                                FarmAllStandStillTick();
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        OnCrash();
                        Log.Info($"CRASH: {Settings.FriendlyName} {ex.Message}");
                    }
                }
            });

            task.Start();
            return task;
        }

        public void WalkPokeStopsAndFarm()
        {
            _cancelTokenSource = new CancellationTokenSource();

            Task.Factory.StartNew(async () =>
            {
                while (!_cancelTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(60000);
                    if (TravelLog != null)
                    {
                        IgnoreExceptions(() =>
                        {
                            System.IO.File.WriteAllText($"{Settings.FriendlyName}.kml", BuildTravelLogKml());
                        });
                    }
                }
            });

            Task.Factory.StartNew(async () =>
            {
                while (!_cancelTokenSource.Token.IsCancellationRequested)
                {
                    await Task.Delay(30000);
                    _processedForts = new List<string>();
                    _processedEncounters = new List<ulong>();
                }
            });

            while (true)
            {
                // do not bog down the cpu ~10 ticks per second are plenty
                Wait(100);
                WalkPokeStopsAndFarmTick();
            }
        }

        private void ApplyLuckyEgg()
        {
            var isLuckyEggActive = PoClient.InventoryAppliedItems.Any(i => i.ItemType == ItemType.XpBoost);

            if (isLuckyEggActive)
                return;

            var luckyEggs = PoClient.InventoryItems.Where(i => i.ItemId == POGOProtos.Inventory.Item.ItemId.ItemLuckyEgg);

            if (luckyEggs.Count() > 0)
            {
                DoUseLuckyEgg(ItemId.ItemLuckyEgg);
            }
        }

        private Func<PokemonData, bool> BuildTransferFilter(int minIvPct = 90, double minCpM = 0.5, int? minCp = null)
        {
            return (pokemon) =>
            {
                //if (pokemon.Favorite == 1)
                //{
                //    return false;
                //}
                //if ((new PokemonId[] { PokemonId.Bulbasaur,
                //    PokemonId.Ivysaur,
                //    PokemonId.Squirtle,
                //    PokemonId.Charmander,
                //    PokemonId.Pikachu }).Contains(pokemon.PokemonId)
                //    && CalcIVPct(pokemon) >= 70)
                //{
                //    //these pokemons are really hard to find above 60% IV with good CPM
                //    return false;
                //}
                if (minCp != null && pokemon.Cp > minCp)
                {
                    return false;
                }
                if (CalcIVPct(pokemon) >= minIvPct && pokemon.CpMultiplier > minCpM)
                {
                    return false;
                }

                return true;
            };
        }

        /// <summary>
        /// Consume with this viewer http://codepen.io/pingec/full/XKqvxp/
        /// </summary>
        /// <returns></returns>
        private string BuildTravelLogKml()
        {
            var kml = @"<?xml version=""1.0"" encoding=""UTF-8""?>
<kml xmlns=""http://www.opengis.net/kml/2.2""
 xmlns:gx=""http://www.google.com/kml/ext/2.2"">
<Folder>
  <Placemark>
    <gx:Track>
	@when
	@coords
    </gx:Track>
  </Placemark>
</Folder>
</kml>
";
            var when = new StringBuilder();
            var coords = new StringBuilder();
            foreach (var pair in TravelLog)
            {
                when.AppendLine($"<when xmlns=\"http://www.opengis.net/kml/2.2\">{pair.Key.ToString("o")}</when>");
                coords.AppendLine(string.Format("<gx:coord xmlns:gx=\"http://www.google.com/kml/ext/2.2\">{0} {1} {2}</gx:coord>",
                    pair.Value.Latitude.ToString(CultureInfo.InvariantCulture),
                    pair.Value.Longitude.ToString(CultureInfo.InvariantCulture),
                    pair.Value.Altitude.ToString(CultureInfo.InvariantCulture)));
            }

            kml = kml.Replace("@when", when.ToString());
            kml = kml.Replace("@coords", coords.ToString());

            return kml;
        }

        private int CandyAvailable(PokemonId pokemon)
        {
            var familyId = PokemonFamilyId(pokemon);
            var candy = PoClient.InventorieCandies.FirstOrDefault(c => c.FamilyId == familyId);
            if (candy == null)
                return 0;

            return candy.Candy;
        }

        private int CandyToEvolve(PokemonId pokemon)
        {
            return PoClient.PokemonSettings.FirstOrDefault(s => s.PokemonId == pokemon).CandyToEvolve;
        }

        private int CandyToFullFamilyEvolve(PokemonFamilyId pokemonFamily)
        {
            return PoClient.PokemonSettings.Where(s => s.FamilyId == pokemonFamily).Select(s => s.CandyToEvolve).Sum();
        }

        private void DoCatchPokemon(ulong encounterId, string spawnPointGuid, PokemonData pokemon, Func<PokemonData, bool> shouldTransfer)
        {
            var catchPokemon = PoClient.RpcClient.CatchPokemon(encounterId, spawnPointGuid);
            Log.Info($"CATCH: {catchPokemon?.Status} Xp: {catchPokemon?.CaptureAward?.Xp}");
            Wait(2000);

            if (catchPokemon.Status == POGOProtos.Networking.Responses.CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
            {
                Stats.OnXpIncrease(catchPokemon.CaptureAward.Xp.Sum());
                Stats.PokemonsCaught++;

                // override pokemondata, we have an id now
                pokemon.Id = catchPokemon.CapturedPokemonId;
                var finalPokemon = DoEvolveOnlyWhiteList(pokemon);
                if (shouldTransfer(finalPokemon))
                {
                    DoTransferPokemon(finalPokemon.Id);
                }
            }
            else
            {
                Stats.PokemonCatchFailures++;
                if (catchPokemon.Status == POGOProtos.Networking.Responses.CatchPokemonResponse.Types.CatchStatus.CatchError)
                {
                    Log.Info($"Are we out of POKEBALLS? Changing to other type.");
                    PoClient.RpcClient.ChangePokeBallType();
                }
            }
        }

        private void DoCatchPokemon2(ulong encounterId, string spawnPointGuid, Func<PokemonData, bool> shouldTransfer)
        {
            var catchPokemon = PoClient.RpcClient.CatchPokemon(encounterId, spawnPointGuid);
            Log.Info($"CATCH: {catchPokemon?.Status} Xp: {catchPokemon?.CaptureAward?.Xp}");
            Wait(2000);

            if (catchPokemon.Status == POGOProtos.Networking.Responses.CatchPokemonResponse.Types.CatchStatus.CatchSuccess)
            {
                Stats.OnXpIncrease(catchPokemon.CaptureAward.Xp.Sum());
                Stats.PokemonsCaught++;

                var pokemon = PoClient.InventoryPokemons.Where(p => p.Id == catchPokemon.CapturedPokemonId).FirstOrDefault();

                if (pokemon != null)
                {
                    var finalPokemon = DoEvolveOnlyWhiteList(pokemon);
                    if (shouldTransfer(finalPokemon))
                    {
                        DoTransferPokemon(finalPokemon.Id);
                    }
                }
                else
                {
                }
            }
            else
            {
                Stats.PokemonCatchFailures++;
                if (catchPokemon.Status == POGOProtos.Networking.Responses.CatchPokemonResponse.Types.CatchStatus.CatchError)
                {
                    Log.Info($"Are we out of POKEBALLS? Changing to other type.");
                    PoClient.RpcClient.ChangePokeBallType();
                }
            }
        }

        private void DoDestroyNonPokeBalls(RepeatedField<ItemAward> itemsAwarded)
        {
            var responses = PoClient.RpcClient.RecycleFortAwardedItems(itemsAwarded);
            Wait(2000);
        }

        private bool DoDiskEncounter(ulong encounterId, string fortId, Func<PokemonData, bool> shouldTransfer)
        {
            if (!PoClient.PokeballsAvailable())
            {
                return false;
            }

            var diskEncounter = PoClient.RpcClient.DiskEncounter(encounterId, fortId);
            Log.Info(string.Format("DISKENCOUNTER {0} {1:N2}% {2}",
                diskEncounter.PokemonData?.PokemonId,
                diskEncounter.PokemonData != null ?
                    CalcIVPct(diskEncounter.PokemonData) : 0d,
                diskEncounter.Result
                ));
            Wait(2000);

            if (diskEncounter.Result != POGOProtos.Networking.Responses.DiskEncounterResponse.Types.Result.Success)
            {
                return false;
            }

            _processedEncounters.Add(encounterId);
            //PokemonEncounters[encounterId] = fortId;
            DoCatchPokemon(encounterId, fortId, diskEncounter.PokemonData, shouldTransfer);
            return true;
        }

        private bool DoEncounter(MapPokemon pokemon, Func<PokemonData, bool> shouldTransfer)
        {
            if (!PoClient.PokeballsAvailable())
            {
                return false;
            }

            var encounterPokemon = PoClient.RpcClient.EncounterPokemon(pokemon.EncounterId, pokemon.SpawnPointId);
            Log.Info($"ENCOUNTER {encounterPokemon.WildPokemon?.PokemonData.PokemonId}, {string.Format("{0:N2}%", CalcIVPct(encounterPokemon.WildPokemon?.PokemonData))} {encounterPokemon.Status}");

            Wait(2000);

            if (encounterPokemon.Status != EncounterResponse.Types.Status.EncounterSuccess)
            {
                return false;
            }

            _processedEncounters.Add(pokemon.EncounterId);
            //PokemonEncounters[pokemon.EncounterId] = pokemon.SpawnPointId;
            DoCatchPokemon(pokemon.EncounterId, pokemon.SpawnPointId, encounterPokemon.WildPokemon.PokemonData, shouldTransfer);

            return true;
        }

        private EvolvePokemonResponse DoEvolve(ulong pokemonId)
        {
            var evolve = PoClient.RpcClient.EvolvePokemon(pokemonId);

            if (evolve.Result == EvolvePokemonResponse.Types.Result.Success)
            {
                var xp = evolve.ExperienceAwarded;
                if (xp > 0)
                {
                    Stats.OnXpIncrease(xp);
                }
                Stats.PokemonsEvolved++;
            }
            Log.Info($"EVOLVE: {evolve.Result}");

            return evolve;
        }

        private PokemonData DoEvolveOnlyWhiteList(PokemonData pokemon, List<PokemonId> whitelist = null)
        {
            if (!Settings.EvolvePokemons)
            {
                return pokemon;
            }

            if (pokemon == null) //unexpected
                return null;

            whitelist = whitelist != null ? whitelist : Configuration.EvolveWhiteList;

            if (!whitelist.Contains(pokemon.PokemonId))
            {
                return pokemon;
            }

            var candyNeeded = CandyToEvolve(pokemon.PokemonId);
            var candyAvailable = CandyAvailable(pokemon.PokemonId);
            var candyToFullFamilyEvolve = CandyToFullFamilyEvolve(PokemonFamilyId(pokemon.PokemonId));

            // ensure we always retain enough candy for a full family evolve eg. charmeleon will be evolved only if there are at least 250 candy available
            if (candyNeeded == 0 || candyAvailable < (candyNeeded + candyToFullFamilyEvolve * 3))
            {
                return pokemon;
            }

            var evolve = DoEvolve(pokemon.Id);

            if (evolve.Result != EvolvePokemonResponse.Types.Result.Success)
            {
                return pokemon;
            }

            return evolve.EvolvedPokemonData;
        }

        private void DoEvolveTransferInventoryPokemons(Func<PokemonData, bool> shouldTransfer)
        {
            foreach (var pokemon in PoClient.InventoryPokemons)
            {
                var finalPokemon = DoEvolveOnlyWhiteList(pokemon);
                if (shouldTransfer(finalPokemon))
                {
                    DoTransferPokemon(finalPokemon.Id);
                }
            }
        }

        private void DoIncubateEgg(string incubatorId, ulong eggId)
        {
            var response = PoClient.RpcClient.UseItemEggIncubator(incubatorId, eggId);
            Log.Info($"INCUBATE EGG: {response.Result} {response.EggIncubator.PokemonId} {response.EggIncubator.ItemId}");
            Wait(2000);
        }

        private bool DoSpinPokeStop(string fortId, double fortLat, double fortLon)
        {
            var fortSearch = PoClient.RpcClient.FortSearch(fortId, fortLat, fortLon);
            Log.Info($"SPIN: {fortSearch.Result} XP: {fortSearch.ExperienceAwarded}, Items: {GetFortSearchItemsFriendlyString(fortSearch)} {fortId}");

            Wait(2000);

            var success = fortSearch.Result == FortSearchResponse.Types.Result.Success;
            if (success)
            {
                DoDestroyNonPokeBalls(fortSearch.ItemsAwarded);
            }

            if (success || fortSearch.Result == FortSearchResponse.Types.Result.InventoryFull)
            {
                Stats.OnXpIncrease(fortSearch.ExperienceAwarded);
                _processedForts.Add(fortId);
                Stats.PokeStopSpins++;
                return true;
            }
            Stats.PokeStopsSpinFailures++;
            return false;
        }

        private void DoTransferPokemon(ulong pokemonId)
        {
            var transferResponse = PoClient.RpcClient.TransferPokemon(pokemonId);
            if (transferResponse.Result == ReleasePokemonResponse.Types.Result.Success)
            {
                Stats.PokemonsTransferred++;
            }
            Log.Info($"TRANSFER: {transferResponse.Result}");
            Wait(2000);
        }

        private void DoUseLuckyEgg(ItemId itemId)
        {
            var response = PoClient.RpcClient.UseItemXpBoost(itemId);
            Log.Info($"ACTIVATE LUCKY EGG: {response.Result}");
            Wait(2000);
        }

        private void EndWalk()
        {
            IsWalking = false;
        }

        private void FarmAllStandStillTick()
        {
            var unixTime = UnixTimeUtc();
            var pokeStopsInRange = GetSortedFortsByDistance(PoClient, f =>
                f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
                && f.CooldownCompleteTimestampMs < UnixTimeUtc()
                && DistanceBetweenPlacesInMeters(PoClient.ClientData.GpsData.Longitude,
                        PoClient.ClientData.GpsData.Latitude,
                        f.Longitude,
                        f.Latitude) < PoClient.GlobalSettings.FortSettings.InteractionRangeMeters
                );

            foreach (var pokeStop in pokeStopsInRange)
            {
                // spin all pokestops
                DoSpinPokeStop(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                // catch all lure pokemons
                if (pokeStop.LureInfo != null)
                {
                    DoDiskEncounter(pokeStop.LureInfo.EncounterId, pokeStop.Id, PokemonTransferFilter);
                }
            }

            // catch all reachable map pokemons
            var pokemons = PoClient.MapObjects.MapCells.SelectMany(c => c.CatchablePokemons);
            foreach (var pokemon in pokemons)
            {
                DoEncounter(pokemon, PokemonTransferFilter);
            }
        }

        private void FarmPokeBallsTick()
        {
            var unixTime = UnixTimeUtc();
            //todo: this should be calculated on every MapObject update but cached otherwise
            var pokeStopsInRange = GetSortedFortsByDistance(PoClient, f =>
                f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
                && f.CooldownCompleteTimestampMs < UnixTimeUtc(),
                40
                );

            if (!IsWalking)
            {
                // we can afford wait calls and more checks since we are not moving
                IncubateEggs();
                ApplyLuckyEgg();

                foreach (var pokeStop in pokeStopsInRange)
                {
                    // spin all pokestops
                    DoSpinPokeStop(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                    // catch all lure pokemons
                    if (pokeStop.LureInfo != null)
                    {
                        DoDiskEncounter(pokeStop.LureInfo.EncounterId, pokeStop.Id, PokemonTransferFilter);
                    }
                }

                // we have cleared this area (disregarding any pokemons/pokestop that became active in the meantime -> todo)!
                var nearestOutOfRangePokestop = GetSortedFortsByDistance(PoClient, f =>
                    f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
                    && f.CooldownCompleteTimestampMs < UnixTimeUtc())
                    .FirstOrDefault();

                if (Settings.MaxRadiusFromStartLocationInMeters != 0)
                {
                    var distance = DistanceBetweenPlacesInMeters(
                        Settings.Longitude,
                        Settings.Latitude,
                        PoClient.ClientData.GpsData.Longitude,
                        PoClient.ClientData.GpsData.Latitude);
                    if (distance > Settings.MaxRadiusFromStartLocationInMeters)
                    {
                        Log.Info("Maximum radius reached, going back to start.");
                        StartWalk(Settings.Latitude,
                            Settings.Longitude,
                            Settings.Speed,
                            null);
                        return;
                    }
                }
                if (nearestOutOfRangePokestop != null)
                {
                    StartWalk(nearestOutOfRangePokestop.Latitude, nearestOutOfRangePokestop.Longitude, Settings.Speed, null);
                    return;
                }
                else
                {
                    //StartWalk(Settings.Latitude, Settings.Longitude, Settings.Speed, null);
                    Log.Info("Out of forts to travel to.");
                    return;
                }
            }
            else
            {
                // EXPLICIT WAITS ARE FORBIDDEN HERE!
                // MORE THAN ONE DO<SOMETHING> CALL IS FORBIDDEN HERE
                // DO<SOMETHING> SHOULD BE FOLLOWED BY A RETURN

                // we are walking, so stuff is more critical here as we might go out of range while trying to interact with it
                // we do not use foreach but instead find one thing to do, do it and then return

                // fort spinning
                var spinnablePokeStop = pokeStopsInRange
                    .Where(f => !_processedForts.Contains(f.Id))
                    .FirstOrDefault();
                if (spinnablePokeStop != null)
                {
                    var spinSuccess = DoSpinPokeStop(spinnablePokeStop.Id, spinnablePokeStop.Latitude, spinnablePokeStop.Longitude);

                    if (spinSuccess && Settings.TestingFeatures)
                    {
                        //StopWalk();
                    }

                    return;
                }
            }
        }

        private void IncubateEggs()
        {
            var emptyIncubators = PoClient.InventoryEggIncubators.Where(i => i.PokemonId == 0);
            var nonIncubatedEggs = PoClient.InventoryEggs.ToList().Where(e => e.EggIncubatorId == "").ToList();
            nonIncubatedEggs.Sort((e1, e2) => e2.EggKmWalkedTarget.CompareTo(e1.EggKmWalkedTarget));

            foreach (var incubator in emptyIncubators)
            {
                if (nonIncubatedEggs.Count < 1)
                    return;

                var egg = nonIncubatedEggs[0];
                nonIncubatedEggs.RemoveAt(0);
                DoIncubateEgg(incubator.Id, egg.Id);
            }
        }

        private bool IsPokemonWorthy(PokemonData pokemon)
        {
            return PokemonTransferFilter(pokemon);
        }

        private void OnCrash()
        {
            //Sometimes a bot crashes, here we do cleanup
            if (_cancelTokenSource != null)
                _cancelTokenSource.Cancel();
        }

        private PokemonFamilyId PokemonFamilyId(PokemonId pokemon)
        {
            return PoClient.PokemonSettings.FirstOrDefault(s => s.PokemonId == pokemon).FamilyId;
        }

        private void RecycleItem(POGOProtos.Inventory.Item.ItemId itemId, int count)
        {
            var recycleItem = PoClient.RpcClient.RecycleInventoryItem(itemId, count);
            Log.Info($"Destroyed {count} {itemId}");
            Wait(2000);
        }

        private void StartWalk(double endLat, double endLon, int speedKph, Action onArrival, int updateIntervalSeconds = 1)
        {
            //updateIntervalSeconds is how fast we update client position *client-side*

            double speedMpS = speedKph * 1000d / 3600d;

            var travelDistancePerInterval = speedMpS * updateIntervalSeconds;

            var startLat = PoClient.ClientData.GpsData.Latitude;
            var startLon = PoClient.ClientData.GpsData.Longitude;

            var walkDistance = DistanceBetweenPlacesInMeters(endLon,
                endLat,
                startLon,
                startLat);

            //https://maps.googleapis.com/maps/api/staticmap?zoom=17&size=600x300&maptype=roadmap&markers=color:green%7Clabel:G%7C-33.859048,151.213183&markers=color:red%7Clabel:C%7C-33.859608,151.212681

            var stepsRequired = (int)Math.Ceiling(walkDistance / travelDistancePerInterval);
            var stepLatDelta = (endLat - startLat) / stepsRequired;
            var stepLonDelta = (endLon - startLon) / stepsRequired;

            Log.Info($"Starting a {walkDistance}m ({(int)(walkDistance / speedMpS)}s) walk to {endLat},{endLon}");
            Log.Debug($"Update interval {updateIntervalSeconds}s, {stepsRequired} steps required.");

            StartWalkTimer(stepLatDelta, stepLonDelta, stepsRequired, updateIntervalSeconds, onArrival);
        }

        private async void StartWalkTimer(double stepLatDelta, double stepLonDelta, int stepsRequired, int updateIntervalSeconds, Action onArrival)
        {
            IsWalking = true;
            while (true)
            {
                Log.Debug($"WalkTimer tick... {stepsRequired} steps left");

                if (!IsWalking)
                {
                    Log.Info($"WalkTimer aborting walking at {stepsRequired} steps left");
                    return;
                }
                if (stepsRequired-- < 1)
                {
                    Log.Info($"WalkTimer finished walking.");
                    WriteTravelLogEntry();
                    // force location refresh so that final location is sent to the server immediately (we want fresh MapObjects)
                    PoClient.RpcClient.Heartbeat();
                    Log.Debug($"WalkTimer forcefully refreshed location/MapObjects, signaling end of walk to all parties.");
                    IsWalking = false;
                    // fire callback to notify walking is finished
                    onArrival?.Invoke();
                    return;
                }

                await Task.Delay(updateIntervalSeconds * 1000);
                var lat = PoClient.ClientData.GpsData.Latitude;
                var lon = PoClient.ClientData.GpsData.Longitude;
                PoClient.SetGpsData(lat + stepLatDelta, lon + stepLonDelta);
            }
        }

        private void StopWalk()
        {
            IsWalking = false;
        }

        private void Wait(int ms)
        {
            Log.Debug($"Waiting {ms / 1000} seconds...");
            Thread.Sleep(ms > 500 ? 500 : ms);
        }

        private void WalkPokeStopsAndFarm2()
        {
            PoClient.MapObjectUpdate += (sender, args) =>
            {
                //todo new bot implementation relying on events instead of ticks?
            };
        }

        private void WalkPokeStopsAndFarmTick()
        {
            //var snipeEncounters = PokemonEncounters.Where(e => !_processedEncounters.Contains(e.Key));
            //snipeEncounters.ToList().ForEach(e => DoCatchPokemon2(e.Key, e.Value, PokemonTransferFilter));

            var unixTime = UnixTimeUtc();
            //todo: this should be calculated on every MapObject update but cached otherwise
            var pokeStopsInRange = GetSortedFortsByDistance(PoClient, f =>
                f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
                && f.CooldownCompleteTimestampMs < UnixTimeUtc(),
                40
                //(int)PoClient.GlobalSettings.FortSettings.InteractionRangeMeters
                );

            if (!IsWalking)
            {
                // we can afford wait calls and more checks since we are not moving
                IncubateEggs();
                ApplyLuckyEgg();

                foreach (var pokeStop in pokeStopsInRange)
                {
                    // spin all pokestops
                    DoSpinPokeStop(pokeStop.Id, pokeStop.Latitude, pokeStop.Longitude);

                    // catch all lure pokemons
                    if (pokeStop.LureInfo != null)
                    {
                        DoDiskEncounter(pokeStop.LureInfo.EncounterId, pokeStop.Id, PokemonTransferFilter);
                    }
                }

                #region 500m fort lures testing

                if (Settings.TestingFeatures)
                {
                    var pokeStopsInRange500 = GetSortedFortsByDistance(PoClient, f =>
            f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
            && f.CooldownCompleteTimestampMs < UnixTimeUtc(),
            Settings.LurePokemonsRadius
            );

                    var fortsWithPokemon = pokeStopsInRange500.Where(f => f.LureInfo != null).
                        Where(f => !_processedEncounters.Contains(f.LureInfo.EncounterId));
                    var pokeStop = fortsWithPokemon.FirstOrDefault();
                    if (pokeStop != null)
                    {
                        var diskEnctrSuccess = DoDiskEncounter(pokeStop.LureInfo.EncounterId, pokeStop.Id, PokemonTransferFilter);
                        if (diskEnctrSuccess)
                        {
                            Log.Info("FINISHED TEST CATCH AT DISTANCE: " + DistanceBetweenPlacesInMeters(PoClient.ClientData.GpsData.Longitude,
                            PoClient.ClientData.GpsData.Latitude, pokeStop.Longitude, pokeStop.Latitude)
                            + pokeStop.LureInfo.EncounterId);
                        }
                        return;
                    }
                }

                #endregion 500m fort lures testing

                // catch all reachable map pokemons
                var pokemons = PoClient.MapObjects.MapCells.SelectMany(c => c.CatchablePokemons);
                foreach (var pokemon in pokemons)
                {
                    DoEncounter(pokemon, PokemonTransferFilter);
                }

                // we have cleared this area (disregarding any pokemons/pokestop that became active in the meantime -> todo)!
                var nearestOutOfRangePokestop = GetSortedFortsByDistance(PoClient, f =>
                    f.Type == POGOProtos.Map.Fort.FortType.Checkpoint
                    && f.CooldownCompleteTimestampMs < UnixTimeUtc())
                    .FirstOrDefault();

                if (Settings.MaxRadiusFromStartLocationInMeters != 0)
                {
                    var distance = DistanceBetweenPlacesInMeters(
                        Settings.Longitude,
                        Settings.Latitude,
                        PoClient.ClientData.GpsData.Longitude,
                        PoClient.ClientData.GpsData.Latitude);
                    if (distance > Settings.MaxRadiusFromStartLocationInMeters)
                    {
                        Log.Info("Maximum radius reached, going back to start.");
                        StartWalk(Settings.Latitude,
                            Settings.Longitude,
                            Settings.Speed,
                            null);
                        return;
                    }
                }
                if (nearestOutOfRangePokestop != null)
                {
                    StartWalk(nearestOutOfRangePokestop.Latitude, nearestOutOfRangePokestop.Longitude, Settings.Speed, null);
                    return;
                }
                else
                {
                    //StartWalk(Settings.Latitude, Settings.Longitude, Settings.Speed, null);
                    Log.Info("Out of forts to travel to.");
                    return;
                }
            }
            else
            {
                // EXPLICIT WAITS ARE FORBIDDEN HERE!
                // MORE THAN ONE DO<SOMETHING> CALL IS FORBIDDEN HERE
                // DO<SOMETHING> SHOULD BE FOLLOWED BY A RETURN

                // we are walking, so stuff is more critical here as we might go out of range while trying to interact with it
                // we do not use foreach but instead find one thing to do, do it and then return

                // first priority is reachable map pokemons (we speculate they could be rarer)
                var pokemons = PoClient.MapObjects.MapCells.SelectMany(c => c.CatchablePokemons)
                    .Where(p => !_processedEncounters.Contains(p.EncounterId));
                var pokemon = pokemons.FirstOrDefault();
                if (pokemon != null)
                {
                    // catch it and return
                    var enctrSuccess = DoEncounter(pokemon, PokemonTransferFilter);
                    return;
                }

                // second priority are fort pokemons
                var fortsWithPokemon = pokeStopsInRange.Where(f => f.LureInfo != null).
                    Where(f => !_processedEncounters.Contains(f.LureInfo.EncounterId));
                var pokeStop = fortsWithPokemon.FirstOrDefault();
                if (pokeStop != null)
                {
                    var diskEnctrSuccess = DoDiskEncounter(pokeStop.LureInfo.EncounterId, pokeStop.Id, PokemonTransferFilter);
                    return;
                }

                // third priority fort spinning
                var spinnablePokeStop = pokeStopsInRange
                    .Where(f => !_processedForts.Contains(f.Id))
                    .FirstOrDefault();
                if (spinnablePokeStop != null)
                {
                    var spinSuccess = DoSpinPokeStop(spinnablePokeStop.Id, spinnablePokeStop.Latitude, spinnablePokeStop.Longitude);

                    if (spinSuccess && Settings.TestingFeatures)
                    {
                        //StopWalk();
                    }

                    return;
                }
            }
        }

        private void WriteTravelLogEntry()
        {
            if (TravelLog == null)
            {
                TravelLog = new List<KeyValuePair<DateTime, GpsData>>();
            }

            TravelLog.Add(new KeyValuePair<DateTime, GpsData>(DateTime.UtcNow, new GpsData
            {
                Latitude = PoClient.ClientData.GpsData.Latitude,
                Longitude = PoClient.ClientData.GpsData.Longitude
            }));
        }
    }
}