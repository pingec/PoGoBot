using POGOLib.Pokemon;

namespace POGOLib.Net
{
    public class BotSettings
    {
        public bool EvolvePokemons = true;
        public string FriendlyName;
        public bool IncubateEggs = true;
        public double Latitude;
        public LoginProvider LoginProvider;
        public double Longitude;
        public int MaxRadiusFromStartLocationInMeters;
        public int? MinCp;
        public double MinCpM = 0.1d;
        public int MinIvPct = 90;
        public string Password;
        public int Speed = 10;
        public BotStrategy Strategy;
        public bool TestingFeatures = false;
        public int LurePokemonsRadius = 40;
        public bool UseLuckyEggs = false;
        public string Username;
        public int? HeartBeatInterval = null;

        public enum BotStrategy
        {
            FarmStandStill,
            CleanUpInventoryPokemons,
            FarmWanderPokestops,
            FarmPokeBalls
        };
    }
}