using log4net;
using POGOLib.Net;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Demo.Utils;

namespace Demo
{
    public class Program
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(PoClient));

        private static Dictionary<ulong, string> PokemonEncounters = new Dictionary<ulong, string>();
        private static List<Bot> RunningBots = new List<Bot>();
        private static List<Task> RunningTasks = new List<Task>();

        public static PoClient InitClientSession(BotSettings s)
        {
            var client = new PoClient(s);
            // Load previous data.
            if (!client.LoadClientData())
            {
                // Need to set initial gps data before authenticating!
                if (!client.HasGpsData())
                {
                    client.SetGpsData(s.Latitude, s.Longitude);
                }

                if (!client.Authenticate())
                    throw new Exception("Wrong password.");

                client.SaveClientData();
            }

            var profile = client.RpcClient.GetProfile();
            var stats = client.PlayerStats;
            var xpPct = (stats.Experience - stats.PrevLevelXp) / (stats.NextLevelXp - stats.PrevLevelXp) * 100;
            Log.Info($"Got profile from server: {profile.PlayerData.Username} Lvl:{stats.Level} Xp:{xpPct} Pkmns:{client.InventoryPokemons.Count} Itms:{client.InventoryItems.Count}");
            Log.Info($"PlayerStats: {stats}");

            // Make sure to save if you want to use save / loading.
            client.SaveClientData();

            client.StartHeartbeats();

            return client;
        }

        public static void Main(string[] args)
        {
            var saveDataPath = Path.Combine(Environment.CurrentDirectory, "savedata");
            if (!Directory.Exists(saveDataPath)) Directory.CreateDirectory(saveDataPath);

            Task.Factory.StartNew(async () =>
            {
                while (true)
                {
                    await Task.Delay(60000);
                    IgnoreExceptions(() =>
                    {
                        var report = RunningBots
                        .Select(b => string.Format("{0} Lvl:{1} {2}% XpRate:{3} Gained:{4} Catch:{5}/{6} Spins:{7}/{8} Evolves:{9}/{10} Transfers:{11}/{12} Pkmns:{13} Items:{14} Balls:{15} {16}\r\n",
                            b.Settings.FriendlyName,
                            b.Level,
                            (b.PoClient.PlayerStats.Experience - b.PoClient.PlayerStats.PrevLevelXp) / (b.PoClient.PlayerStats.NextLevelXp - b.PoClient.PlayerStats.PrevLevelXp) * 100,
                            b.Stats.HourlyXpRate,
                            b.Stats.TotalXp,
                            b.Stats.PokemonsCaught,
                            b.Stats.PokemonCatchFailures,
                            b.Stats.PokeStopSpins,
                            b.Stats.PokeStopsSpinFailures,
                            b.Stats.PokemonsEvolved,
                            b.Stats.PokemonEvolveFailures,
                            b.Stats.PokemonsTransferred,
                            b.Stats.PokemonTransferFailures,
                            b.Stats.InventoryPokemonsCount,
                            b.Stats.InventoryItemsCount,
                            b.Stats.InventoryBallsCount,
                            string.Format("R:{0} S:{1} lureR:{2} evolve:{3} loc:{4},{5} test:{6} beats:{7}",
                            b.Settings.MaxRadiusFromStartLocationInMeters,
                            b.Settings.Speed,
                            b.Settings.LurePokemonsRadius,
                            b.Settings.EvolvePokemons,
                            b.Settings.Latitude,
                            b.Settings.Longitude,
                            b.Settings.TestingFeatures,
                            b.Settings.HeartBeatInterval
                            )))
                        .Aggregate((current, next) => current + next);
                        File.WriteAllText($"report.txt", report);
                    });
                }
            });

#if DEBUG
            StartBots(Configuration.TestingSettings);
#else
            StartBots(Configuration.GenSettings());
#endif

            // not expected to ever throw
            Task.WaitAll(RunningTasks.ToArray());
        }

        public static Task StartBot(PoClient client, BotSettings s)
        {
            var bot = new Bot(client, s, PokemonEncounters);
            RunningBots.Add(bot);
            return bot.Start();
        }

        public static void StartBots(IEnumerable<BotSettings> settings)
        {
            foreach (var s in settings)
            {
                Task t = Task.Run(async () =>
               {
                   PoClient client = null;
                   do
                   {
                       try
                       {
                           client = InitClientSession(s);
                       }
                       catch (Exception ex)
                       {
                       }
                   }
                   while (client == null);
                   SaveSummaryToFile(client, $"{s.FriendlyName}.txt");

                   await StartBot(client, s);
               });
                RunningTasks.Add(t);
            }
        }
    }
}