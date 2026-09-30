using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace InventoryKamera
{
    public class GOOD
    {
        [JsonProperty("format")]
        public string Format { get; private set; }

        [JsonProperty("version")]
        public int Version { get; private set; }

        [JsonProperty("kamera_version")]
        public string AppVersion { get; private set; }

        [JsonProperty("source")]
        public string Source { get; private set; }

        [JsonProperty("weapons", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Weapon> Weapons { get; private set; }

        [JsonProperty("artifacts", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Artifact> Artifacts { get; private set; }

        [JsonProperty("characters", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public List<Character> Characters { get; private set; }

        [JsonProperty("materials", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, int> Materials { get; private set; }

        [JsonProperty("kamera_artifact_icons", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, Dictionary<string, string>> KameraArtifactIcons { get; private set; }

        [JsonProperty("kamera_material_icons", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public Dictionary<string, string> KameraMaterialIcons { get; private set; }

        public GOOD()
        {
            Format = "EMPTY";
            Version = 0;
            Source = "NOT FILLED";
        }

        public GOOD(InventoryKamera genshinData) : this()
        {
            // Get rid of VS warning since we are converting this class to JSON
            Format = "GOOD";
            Version = 2;
            AppVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString(3);
            Source = "Inventory_Kamera";

            // Assign Characters
            if (genshinData.Characters.Count > 0)
            {
                Characters = new List<Character>(genshinData.Characters);

                if (!Properties.Settings.Default.EquipWeapons)
                {
                    foreach (Character character in Characters) character.Weapon = null;
                }
            }



            // Assign Weapons
            if (genshinData.Inventory.Weapons.Count > 0)
            {
                Weapons = new List<Weapon>(genshinData.Inventory.Weapons);

                if (!Properties.Settings.Default.EquipWeapons)
                {
                    foreach (Weapon weapon in Weapons) weapon.EquippedCharacter = "";
                }
            }

            // Assign Artifacts
            if (genshinData.Inventory.Artifacts.Count > 0)
            {
                Artifacts = new List<Artifact>(genshinData.Inventory.Artifacts);

                if (!Properties.Settings.Default.EquipArtifacts)
                {
                    foreach (Artifact artifact in Artifacts) artifact.EquippedCharacter = "";
                }
            }

            // Assign materials
            if (genshinData.Inventory.AllMaterials.Count > 0)
            {
                Materials = new Dictionary<string, int>();
                foreach (var material in genshinData.Inventory.AllMaterials)
                {
                    if (!string.IsNullOrEmpty(material.name) && !Materials.ContainsKey(material.name))
                        Materials[material.name] = material.count;
                }
            }

            // Embed artifact icon data from local database for use by external tools
            try
            {
                var db = new DatabaseManager();
                var artifactDb = db.LoadArtifacts();
                if (artifactDb != null && artifactDb.Count > 0)
                {
                    var iconMap = new Dictionary<string, Dictionary<string, string>>();
                    foreach (var kvp in artifactDb)
                    {
                        if (kvp.Value.TryGetValue("GOOD", out var goodKey) &&
                            kvp.Value.TryGetValue("artifacts", out var piecesToken) &&
                            piecesToken is Newtonsoft.Json.Linq.JObject pieces)
                        {
                            var slotIcons = new Dictionary<string, string>();
                            foreach (var piece in pieces.Properties())
                            {
                                var iconVal = piece.Value["icon"]?.ToString();
                                if (!string.IsNullOrEmpty(iconVal))
                                    slotIcons[piece.Name] = iconVal;
                            }
                            if (slotIcons.Count > 0)
                                iconMap[goodKey.ToString()] = slotIcons;
                        }
                    }
                    if (iconMap.Count > 0) KameraArtifactIcons = iconMap;
                }

                // Embed material icon data from local database
                var materialIcons = db.LoadMaterialIcons();
                if (materialIcons != null && materialIcons.Count > 0)
                    KameraMaterialIcons = materialIcons;
            }
            catch { /* Non-critical, skip if database unavailable */ }
        }

        internal void WriteToJSON(string outputDirectory)
        {
            // Creates directory if doesn't exist
            Directory.CreateDirectory(outputDirectory);

            // Create file with timestamp in name
            string fileName = $"genshinData_GOOD_{DateTime.Now.ToString("yyyy_MM_dd_HH_mm")}.json";
            string outputFile = Path.Combine(outputDirectory, fileName);

            // Write file
            WriteToJson(outputFile);


            if (!File.Exists(outputFile)) // did not make file
            {
                UserInterface.AddError($"Failed to output at : {outputDirectory}");
            }
        }

        private void WriteToJson(string outputFile)
        {
            using (var streamWriter = new StreamWriter(outputFile))
            {
                streamWriter.WriteLine(ToString());
            }
        }

        public override string ToString()
        {
            return JsonConvert.SerializeObject(this).ToString();
        }
    }
}