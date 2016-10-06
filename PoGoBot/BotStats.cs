using log4net;
using System;
using System.Linq;

namespace Demo
{
    public class BotStats
    {
        public readonly Bot Bot;
        public readonly DateTime StartTime = DateTime.UtcNow;
        public int PokemonCatchFailures;
        public int PokemonEcounters;
        public int PokemonEvolveFailures;
        public int PokemonsCaught;
        public int PokemonsEvolved;
        public int PokemonsTransferred;
        public int PokemonTransferFailures;
        public int PokeStopSpins;
        public int PokeStopsSpinFailures;
        private readonly ILog Log;

        public BotStats(Bot bot, ILog log)
        {
            Bot = bot;
            Log = log;
        }

        public int HourlyXpRate
        {
            get
            {
                return (int)(TotalXp / (DateTime.UtcNow - StartTime).TotalHours);
            }
        }

        public int InventoryBallsCount
        {
            get
            {
                return Bot.PoClient.PokeballsCount;
            }
        }

        public int InventoryItemsCount
        {
            get
            {
                return Bot.PoClient.InventoryItems.Select(i => i.Count).Sum();
            }
        }

        public int InventoryPokemonsCount
        {
            get
            {
                return Bot.PoClient.InventoryPokemons.Count();
            }
        }

        public long TotalXp { get; private set; } = 0;

        public void OnXpIncrease(int xp)
        {
            TotalXp += xp;
            Log.Info($"-------=Xp/h:{HourlyXpRate}= <--------");
        }
    }
}