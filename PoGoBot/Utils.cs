using POGOLib.Net;
using POGOProtos.Map.Fort;
using POGOProtos.Networking.Responses;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Demo
{
    internal static class Utils
    {
        public static double CalcIVPct(POGOProtos.Data.PokemonData p)
        {
            if (p == null)
                return 0d;
            //max A/D/S is 15/15/15 which corresponds to 100% IV
            return ((p.IndividualAttack + p.IndividualDefense + p.IndividualStamina) / 45d) * 100d;
        }

        public static double DistanceBetweenPlacesInMeters(double lon1, double lat1, double lon2, double lat2)
        {
            Func<double, double> Radians = (double x) => x * Math.PI / 180;

            double R = 6376500; // m

            double sLat1 = Math.Sin(Radians(lat1));
            double sLat2 = Math.Sin(Radians(lat2));
            double cLat1 = Math.Cos(Radians(lat1));
            double cLat2 = Math.Cos(Radians(lat2));
            double cLon = Math.Cos(Radians(lon1) - Radians(lon2));

            double cosD = sLat1 * sLat2 + cLat1 * cLat2 * cLon;

            // If both points are the same, cosD can become >1 (like 1.0000000000002) due to round error
            // Acos(>1) returns NaN and corrupts results so prevent that
            cosD = cosD > 1 ? 1 : cosD;

            double d = Math.Acos(cosD);

            double dist = R * d;

            return dist;
        }

        public static string GetFortSearchItemsFriendlyString(FortSearchResponse fortSearch)
        {
            var itemDescriptions = fortSearch.ItemsAwarded.Select(i => GetItemIdFriendlyString(i.ItemId) + " x" + i.ItemCount);
            return string.Join(" ", itemDescriptions);
        }

        public static string GetItemIdFriendlyString(POGOProtos.Inventory.Item.ItemId itemId)
        {
            return Enum.GetName(typeof(POGOProtos.Inventory.Item.ItemId), itemId);
        }

        public static List<FortData> GetSortedFortsByDistance(PoClient client, Func<FortData, bool> filter, int maxDistance = -1)
        {
            var forts = client.MapObjects.MapCells.SelectMany(c => c.Forts).Where(filter);

            var lat = client.ClientData.GpsData.Latitude;
            var lon = client.ClientData.GpsData.Longitude;

            Dictionary<string, double> distances = new Dictionary<string, double>();
            foreach (var fort in forts)
            {
                distances.Add(fort.Id, DistanceBetweenPlacesInMeters(fort.Longitude, fort.Latitude, lon, lat));
            }

            var fortsByDistance = maxDistance > 0 ? forts.Where(f => distances[f.Id] < maxDistance).ToList() : forts.ToList();
            fortsByDistance.Sort((f1, f2) =>
            {
                return distances[f1.Id].CompareTo(distances[f2.Id]);
            });

            return fortsByDistance;
        }

        public static void IgnoreExceptions(Action act)
        {
            try
            {
                act.Invoke();
            }
            catch { }
        }

        public static void SaveSummaryToFile(PoClient client, string fileName, bool compact = true)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("Stats:");
            var s = client.PlayerStats;
            var xpPct = (s.Experience - s.PrevLevelXp) / (s.NextLevelXp - s.PrevLevelXp) * 100;
            sb.AppendLine($"Level:{s.Level} XP:{xpPct}% KmWalked:{s.KmWalked}");
            sb.AppendLine($"Items:({client.InventoryItems.Sum(i => i.Count)})");
            client.InventoryItems.ForEach(i =>
            {
                if (compact)
                {
                    sb.Append($"{i.ItemId} : {i.Count} ");
                }
                else
                {
                    sb.AppendLine($"{i.ItemId} : {i.Count}");
                }
            });
            sb.AppendLine($"Candies:");
            var candies = client.InventorieCandies.ToList();
            candies.Sort((pf1, pf2) => pf2.Candy.CompareTo(pf1.Candy));
            candies.ForEach(c =>
            {
                if (compact)
                {
                    sb.Append($"{c.FamilyId.ToString().Replace("Family", "")}:{c.Candy} ");
                }
                else
                {
                    sb.AppendLine($"{c.FamilyId.ToString().Replace("Family", "")}:{c.Candy}");
                }
            });
            sb.AppendLine($"Pokemons: ({client.InventoryPokemons.Count})");
            var pokemons = client.InventoryPokemons.ToList();
            pokemons.Sort((p1, p2) => CalcIVPct(p1).CompareTo(CalcIVPct(p2)));
            pokemons.ForEach(i =>
            {
                var pkIVPct = string.Format("{0:N2}", CalcIVPct(i));
                sb.AppendLine($"{i.PokemonId} CP:{i.Cp} IV:{pkIVPct}% (S:{i.IndividualStamina} A:{i.IndividualAttack} D:{i.IndividualDefense}) CPM:{i.CpMultiplier} aCPM:{i.AdditionalCpMultiplier} LVL:{i.NumUpgrades} HP:{i.StaminaMax} M1:{i.Move1} M2:{i.Move2}");
            });
            IgnoreExceptions(() => System.IO.File.WriteAllText($"{fileName}", sb.ToString()));
        }

        public static long UnixTimeUtc()
        {
            return Convert.ToInt64((DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalMilliseconds);
        }
    }
}