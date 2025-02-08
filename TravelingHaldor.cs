using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using CreatureManager;
using HarmonyLib;
using ItemManager;
using PieceManager;
using ServerSync;
using UnityEngine;

namespace TravelingHaldor
{
    [BepInPlugin(ModGUID, ModName, ModVersion)]
    public class TravelingHaldor : BaseUnityPlugin
    {
        private const string ModName = "TravelingHaldor";
        private const string ModVersion = "1.0.5";
        private const string ModGUID = "org.bepinex.plugins.travelinghaldor";
        private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };
        public enum Toggle { On = 1, Off = 0, }
        public enum SpawnTime { Always, Day, Night }

        public static ConfigEntry<float> eventInterval = null!;
        public static ConfigEntry<float> eventChance = null!;
        public static ConfigEntry<float> eventDuration = null!;
        public static ConfigEntry<string> globalKeyRequirement = null!;
        public static ConfigEntry<SpawnTime> specificSpawnTime = null!;
        public static ConfigEntry<string> traderItemConfig = null!;
        public static ConfigEntry<string> customGreetings = null!;
        public static ConfigEntry<string> customGoodbyes = null!;
        public static ConfigEntry<string> customTradeDialogues = null!;
        public static ConfigEntry<bool> enableLocationVariability = null!;
        public static ConfigEntry<int> maxHaldorCount = null!;
        public static ConfigEntry<float> dialogueHeight = null!;
        public static ConfigEntry<Heightmap.Biome> allowedBiomes = null!;
        public static ConfigEntry<bool> forceDespawn = null!;
        public static ConfigEntry<bool> enableCustomPins = null;

        void InitConfig()
        {
            eventInterval = Config.Bind("Event Settings", "Event Interval (Days)", 3f,
                "How often (in in-game days) Traveling Haldor appears.");
            eventChance = Config.Bind("Event Settings", "Event Chance", 0.5f,
                "Chance (0-1) of event occurring when interval is reached.");
            eventDuration = Config.Bind("Event Settings", "Event Duration", 250f,
                "How long (seconds) the trader stays.");
            globalKeyRequirement = Config.Bind("Event Settings", "Global Key Requirement", "defeated_gdking",
                "Global key required to trigger the event. Acceptable values: defeated_bonemass, defeated_gdking, defeated_goblinking, defeated_dragon, defeated_eikthyr, defeated_queen, defeated_fader, defeated_serpent, KilledTroll, killed_surtling, KilledBat.");
            specificSpawnTime = Config.Bind("Event Settings", "Specific Spawn Time", SpawnTime.Always,
                "Specify when Haldor can spawn. Acceptable values: Always, Day, Night.");
            maxHaldorCount = Config.Bind("Event Settings", "Max Haldor Count", 1,
                "Maximum number of Traveling Haldors allowed in the world. Set to 1 to prevent duplicates.");
            traderItemConfig = Config.Bind("Trader Settings", "TradeItems",
                "HelmetYule,1,100;HelmetDverger,1,650;BeltStrength,1,950;YmirRemains,1,120,defeated_gdking;FishingRod,1,350;FishingBait,20,10;Thunderstone,1,50,defeated_gdking;ChickenEgg,1,1500,defeated_goblinking;BarrelRings,3,100",
                "List of items for Traveling Haldor to sell. Format: PrefabName,Amount,Cost,RequiredGlobalKey. Separate multiple items with ';'.");

            customGreetings = Config.Bind("Custom Dialogues", "Greetings", "Greetings, traveler!;Hello!;Welcome!",
                "Custom greetings for the trader. Separate multiple greetings with ';'.");
            customGoodbyes = Config.Bind("Custom Dialogues", "Goodbyes", "Safe travels!;Goodbye!;Until next time!",
                "Custom goodbyes for the trader. Separate multiple goodbyes with ';'.");
            customTradeDialogues = Config.Bind("Custom Dialogues", "TradeDialogues",
                "Have a look!;Welcome to my shop!;What can I get you?",
                "Custom trade dialogues for the trader. Separate multiple dialogues with ';'.");

            enableLocationVariability = Config.Bind("Location Settings", "Check Biome", true,
                "Enable or disable trader location variability.");
            allowedBiomes = Config.Bind("Location Settings", "Allowed Biomes",
                Heightmap.Biome.Meadows | Heightmap.Biome.BlackForest, "Acceptable biomes");

            forceDespawn = Config.Bind("Event Settings", "Force Despawn", true,
                "If true, even if haldor is trapped, he will despawn");

            dialogueHeight = Config.Bind("Dialogue", "Height", 5f, "Set dialogue height");
            dialogueHeight.SettingChanged += (_, _) =>
            {
                foreach (var instance in TravelingTrader.m_instances)
                {
                    instance.m_dialogHeight = dialogueHeight.Value;
                }
            };

            enableCustomPins = Config.Bind("Event Settings", "Enable Custom Pins", false, "Enable or disable custom map pins.");

            // Register the clear command
            var command = new Terminal.ConsoleCommand("clear_traveling_haldors",
                "Removes all active Traveling Haldors from the world.", (args) => { ClearAllHaldors(); });
        }

        void Awake()
        {
            InitConfig();

            GameObject vfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "vfx_spawn_travelinghaldor");
            GameObject sfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_spawn_travelinghaldor");
            GameObject sfx_travelinghaldor_laugh = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_travelinghaldor_laugh");

            TravelingTrader.m_spawnEffects.m_effectPrefabs = new List<EffectList.EffectData>()
            {
                new(){m_prefab = vfx_spawn_travelinghaldor},
                new(){m_prefab = sfx_spawn_travelinghaldor}
            }.ToArray();

            Creature travelingHaldor = new("hangryaldor", "TravelingHaldor")
            {
                Biome = Heightmap.Biome.None,
                CreatureFaction = Character.Faction.Players,
                CanHaveStars = false,
                Maximum = 1
            };

            travelingHaldor.Localize().English("Haldor");

            MaterialReplacer.RegisterGameObjectForShaderSwap(travelingHaldor.Prefab, MaterialReplacer.ShaderType.UseUnityShader);

            var component = travelingHaldor.Prefab.AddComponent<TravelingTrader>();
            component.m_name = "$npc_haldor";
            var animator = travelingHaldor.Prefab.GetComponentInChildren<Animator>();
            animator.gameObject.AddComponent<LookAt>();
            TravelingTrader.travelingHaldorPrefab = travelingHaldor.Prefab;
            TravelingTrader.m_lastEventSpawn = eventInterval.Value;

            Item Traveling_Haldor_Token = new("hangryaldor", "Traveling_Haldor_Token");
            Traveling_Haldor_Token.Name.English("Traveling Haldor Token"); // You can use this to fix the display name in code
            Traveling_Haldor_Token.Description.English("A throwable token to spawn Traveling Haldor.");
            Traveling_Haldor_Token.Trade.Price = 100; // You can set a price for the item
            Traveling_Haldor_Token.Trade.Stack = 1; // And how many you can buy at once
            Traveling_Haldor_Token.Trade.RequiredGlobalKey = "defeated_bonemass"; // You can set a global key that is required to buy this item
            Traveling_Haldor_Token.Trade.Trader = ItemManager.Trader.Haldor;
            GameObject Traveling_Haldor_Token_Pro = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "Traveling_Haldor_Token_Pro");

            // Register custom pin only if the config entry is enabled
            if (enableCustomPins.Value)
            {
                CustomMapPins_Trader.RegisterCustomPin(travelingHaldor.Prefab, "Traveling Haldor", Traveling_Haldor_Token.Prefab.GetComponent<ItemDrop>().m_itemData.m_shared.m_icons[0]);
            }

            Assembly assembly = Assembly.GetExecutingAssembly();
            Harmony harmony = new(ModGUID);
            harmony.PatchAll(assembly);
        }
        public static class CustomMapPins_Trader
        {
            public class CustomPinhandler_Generic_Trader : MonoBehaviour
            {
                public Sprite icon;
                public string pinName;
                private Minimap.PinData pin;

                private void Awake()
                {
                    pin = new Minimap.PinData();
                    pin.m_type = Minimap.PinType.Icon0;
                    pin.m_name = Localization.instance.Localize(pinName);
                    pin.m_pos = transform.position;
                    pin.m_icon = icon;
                    pin.m_save = false;
                    pin.m_checked = false;
                    pin.m_ownerID = 0;
                    RectTransform root = (Minimap.instance.m_mode == Minimap.MapMode.Large) ? Minimap.instance.m_pinNameRootLarge : Minimap.instance.m_pinNameRootSmall;
                    pin.m_NamePinData = new Minimap.PinNameData(pin);
                    Minimap.instance.CreateMapNamePin(pin, root);
                    pin.m_NamePinData.PinNameText.richText = true;
                    pin.m_NamePinData.PinNameText.overrideColorTags = false;
                    Minimap.instance?.m_pins?.Add(pin);
                }

                private void LateUpdate()
                {
                    pin.m_checked = false;
                    pin.m_pos = transform.position;
                }

                private void OnDestroy()
                {
                    if (pin == null) return;

                    // Ensure pin UI elements are safely destroyed
                    if (pin.m_uiElement != null)
                        Minimap.instance.DestroyPinMarker(pin);
                    Minimap.instance?.m_pins?.Remove(pin);
                }
            }

            public static void RegisterCustomPin(GameObject go, string name, Sprite icon)
            {
                var comp = go.AddComponent<CustomPinhandler_Generic_Trader>();
                comp.pinName = name;
                comp.icon = icon;
            }
        }
        private void ClearAllHaldors()
        {
            int removedCount = 0;

            foreach (var instance in TravelingTrader.m_instances)
            {
                instance.m_nview.ClaimOwnership();
                instance.m_nview.Destroy();
                ++removedCount;
            }

            Debug.Log($"[TravelingHaldor] Removed {removedCount} Traveling Haldor(s).");
            if (Player.m_localPlayer) Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, $"Removed {removedCount} Traveling Haldor(s)!");
        }

        private void Update()
        {
            TravelingTrader.UpdateSpawn();
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
        {
            ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

            SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
            syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

            return configEntry;
        }

        private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);
    }
}

