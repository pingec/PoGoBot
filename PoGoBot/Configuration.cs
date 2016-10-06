using POGOLib.Net;
using POGOLib.Net.Data;
using POGOLib.Pokemon;
using POGOProtos.Enums;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Demo
{
    public class Configuration
    {
        public static List<PokemonId> EvolveWhiteList = new List<PokemonId> {
                // junk
                PokemonId.Zubat,
                PokemonId.Caterpie,
                PokemonId.Weedle,
                PokemonId.Pidgey,
                PokemonId.Spearow,
                PokemonId.Rattata,
                PokemonId.Voltorb,
                PokemonId.Magnemite,

                // these are location-specific
                PokemonId.Krabby,
                PokemonId.Psyduck,
                PokemonId.Slowpoke,
                PokemonId.Shellder,
                PokemonId.Horsea,
                PokemonId.Goldeen,
                PokemonId.Staryu,
                PokemonId.Drowzee,
                PokemonId.Ekans,
                PokemonId.Exeggcute,
                PokemonId.Gastly,
                PokemonId.Geodude,
                PokemonId.Bellsprout,
                PokemonId.Cubone,
                PokemonId.Mankey,
                PokemonId.NidoranFemale,
                PokemonId.NidoranMale,
                PokemonId.Oddish,
                PokemonId.Poliwag,
                PokemonId.Rhydon,
                PokemonId.Slowpoke,
                PokemonId.Venonat,

                //Not sure about these
                PokemonId.Bulbasaur
            };

        public static List<BotSettings> TestingSettings = new List<BotSettings>
            {
                new BotSettings {
                    FriendlyName = "TestingUser",
                    Username = "testingUser@gmail.com",
                    Password = "testingtesting",
                    LoginProvider = LoginProvider.GoogleAuth,
                    Latitude = 37.808673,
                    Longitude = -122.409950,
                    Strategy = BotSettings.BotStrategy.FarmWanderPokestops,
                    MinIvPct = 95,
                    MinCp = 1500,
                    MaxRadiusFromStartLocationInMeters = 2000,
                    Speed = 30,
                    TestingFeatures = true
                },
            new BotSettings {
                    FriendlyName = "PTCUser",
                    Username = "PTCUser",
                    Password = "password1",
                    LoginProvider = LoginProvider.PokemonTrainerClub,
                    Latitude = 37.808673,
                    Longitude = -122.409950,
                    Strategy = BotSettings.BotStrategy.FarmWanderPokestops,
                    MinIvPct = 95,
                    MinCp = 1500,
                    EvolvePokemons = true,
                    Speed = 30,
                    MaxRadiusFromStartLocationInMeters = 4000
                }
        };

        public static List<BotSettings> GenSettings()
        {
            var coords = new GpsData[] { new GpsData { Latitude = 37.808673, Longitude = -122.409950 },
                new GpsData { Latitude = 37.80198, Longitude = -122.412672 }
            }.ToList();
            var speeds = new int[] { 30, 30, 30, 30 }.ToList();
            var radius = new int[] { 2000, 3000, 4000 }.ToList();
            var lureRadius = new int[] { 40, 200 }.ToList();
            var heartBeats = new int[] { 5, 2 }.ToList();

            string text = File.ReadAllText("pogo_accounts_activation.txt");
            var lines = text.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            var settings = new List<BotSettings>();
            var i = 0;
            coords.ForEach(coord => speeds.ForEach(speed => radius.ForEach(rad => lureRadius.ForEach(lr => heartBeats.ForEach(hb =>
            {
                var split = lines[i++].Split(',');
                var user = split[0];
                var pass = split[1];
                var botSettings = new BotSettings
                {
                    FriendlyName = user,
                    Username = user,
                    Password = pass,
                    LoginProvider = LoginProvider.PokemonTrainerClub,
                    Latitude = coord.Latitude,
                    Longitude = coord.Longitude,
                    Speed = speed,
                    MaxRadiusFromStartLocationInMeters = rad,
                    LurePokemonsRadius = lr,
                    Strategy = BotSettings.BotStrategy.FarmWanderPokestops,
                    MinIvPct = 90,
                    MinCp = 1500,
                    EvolvePokemons = true,
                    HeartBeatInterval = hb,
                    TestingFeatures = true
                };

                settings.Add(botSettings);
            })))));

            return settings;
        }
    }
}