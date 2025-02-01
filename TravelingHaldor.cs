using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using CreatureManager;
using ServerSync;
using ItemManager;
using BepInEx.Configuration;
using System.Reflection;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class TravelingHaldor : BaseUnityPlugin
{
    private const string ModName = "TravelingHaldor";
    private const string ModVersion = "1.0.3"; 
    private const string ModGUID = "org.bepinex.plugins.travelinghaldor";

    private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    private ConfigEntry<float> eventInterval;
    private ConfigEntry<float> eventChance;
    private ConfigEntry<float> eventDuration;
    private ConfigEntry<float> spawnDistance; 
    private ConfigEntry<string> globalKeyRequirement; 
    private ConfigEntry<SpawnTime> specificSpawnTime; 
    private GameObject travelingHaldorPrefab;
    private Trader travelingHaldorTrader;
    private ConfigEntry<string> traderItemConfig;
    private ConfigEntry<string> customGreetings;
    private ConfigEntry<string> customGoodbyes;
    private ConfigEntry<string> customTradeDialogues;
    private ConfigEntry<bool> enableLocationVariability;
    private ConfigEntry<string> spawnRegions;
    private ConfigEntry<int> maxHaldorCount;
    private float m_byeRange = 5f;
    private float m_standRange = 15f;
    private float m_greetRange = 5f;
    private float m_dialogHeight = 5f;
    private Animator m_animator;
    private LookAt m_lookAt;
    private bool m_didGreet;
    private bool m_didGoodbye;
    private List<string> m_randomGreets = new List<string> { "Greetings, traveler!", "Come, see my wares!" };
    private List<string> m_randomGoodbye = new List<string> { "Safe travels!", "Until we meet again!" };
    private List<string> m_randomStartTrade = new List<string> { "Welcome to my shop!", "Let's do business!" };
    private List<string> m_randomTalk = new List<string> { "Whoa there Halstein!" };
    private EffectList m_randomGreetFX = new EffectList();
    private EffectList m_randomGoodbyeFX = new EffectList();
    private List<GameObject> activeHaldors = new List<GameObject>();


    private enum SpawnTime
    {
        Always,
        Day,
        Night
    }


    void InitConfig()
    {
        eventInterval = Config.Bind("Event Settings", "Event Interval (Days)", 3f, "How often (in in-game days) Traveling Haldor appears.");
        eventChance = Config.Bind("Event Settings", "Event Chance", 0.5f, "Chance (0-1) of event occurring when interval is reached.");
        eventDuration = Config.Bind("Event Settings", "Event Duration", 250f, "How long (seconds) the trader stays.");
        spawnDistance = Config.Bind("Event Settings", "Spawn Distance (Meters)", 50f, "Distance from the player at which the trader spawns.");
        globalKeyRequirement = Config.Bind("Event Settings", "Global Key Requirement", "defeated_gdking", "Global key required to trigger the event. Acceptable values: defeated_bonemass, defeated_gdking, defeated_goblinking, defeated_dragon, defeated_eikthyr, defeated_queen, defeated_fader, defeated_serpent, KilledTroll, killed_surtling, KilledBat.");
        specificSpawnTime = Config.Bind("Event Settings", "Specific Spawn Time", SpawnTime.Always, "Specify when Haldor can spawn. Acceptable values: Always, Day, Night.");
        maxHaldorCount = Config.Bind("Event Settings", "Max Haldor Count", 1,
            "Maximum number of Traveling Haldors allowed in the world. Set to 1 to prevent duplicates.");
        traderItemConfig = Config.Bind("Trader Settings", "TradeItems",
            "HelmetYule,1,100;HelmetDverger,1,650;BeltStrength,1,950;YmirRemains,1,120,defeated_gdking;FishingRod,1,350;FishingBait,20,10;Thunderstone,1,50,defeated_gdking;ChickenEgg,1,1500,defeated_goblinking;BarrelRings,3,100",
            "List of items for Traveling Haldor to sell. Format: PrefabName,Amount,Cost,RequiredGlobalKey. Separate multiple items with ';'.");

        customGreetings = Config.Bind("Custom Dialogues", "Greetings", "Greetings, traveler!;Hello!;Welcome!", "Custom greetings for the trader. Separate multiple greetings with ';'.");
        customGoodbyes = Config.Bind("Custom Dialogues", "Goodbyes", "Safe travels!;Goodbye!;Until next time!", "Custom goodbyes for the trader. Separate multiple goodbyes with ';'.");
        customTradeDialogues = Config.Bind("Custom Dialogues", "TradeDialogues", "Have a look!;Welcome to my shop!;What can I get you?", "Custom trade dialogues for the trader. Separate multiple dialogues with ';'.");

        enableLocationVariability = Config.Bind("Location Settings", "Enable Location Variability", true, "Enable or disable trader location variability.");
        spawnRegions = Config.Bind("Location Settings", "Spawn Regions", "Meadows;BlackForest", "Possible spawn regions and biomes for the trader. Separate multiple regions/biomes with ';'. Acceptable values: Meadows, BlackForest, Swamp, Mountain, Plains, AshLands, DeepNorth, Ocean, Mistlands, All, None.");

        // Register the clear command
        new Terminal.ConsoleCommand("clear_traveling_haldors", "Removes all active Traveling Haldors from the world.", (args) =>
        {
            ClearAllHaldors();
        });

    }

    private ConfigEntry<T> config<T>(string group, string name, T value, ConfigDescription description, bool synchronizedSetting = true)
    {
        ConfigEntry<T> configEntry = Config.Bind(group, name, value, description);

        SyncedConfigEntry<T> syncedConfigEntry = configSync.AddConfigEntry(configEntry);
        syncedConfigEntry.SynchronizedConfig = synchronizedSetting;

        return configEntry;
    }

    private ConfigEntry<T> config<T>(string group, string name, T value, string description, bool synchronizedSetting = true) => config(group, name, value, new ConfigDescription(description), synchronizedSetting);

    public enum Toggle
    {
        On = 1,
        Off = 0,
    }


    void Awake()
    {
        InitConfig();

        GameObject vfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "vfx_spawn_travelinghaldor");
        GameObject sfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_spawn_travelinghaldor");
        GameObject sfx_travelinghaldor_laugh = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_travelinghaldor_laugh");

        Creature travelingHaldor = new("hangryaldor", "TravelingHaldor")
        {
            Biome = Heightmap.Biome.None,
            CreatureFaction = Character.Faction.Players,
            CanHaveStars = false,
            Maximum = 1
        };

        travelingHaldor.Localize().English("Haldor");

        travelingHaldorPrefab = travelingHaldor.Prefab;

        // Add HoverText component
        var hoverText = travelingHaldorPrefab.AddComponent<HoverText>();
        hoverText.m_text = "Open Trade";

        travelingHaldorTrader = travelingHaldorPrefab.AddComponent<Trader>();

        m_animator = travelingHaldorPrefab.GetComponentInChildren<Animator>();
        if (m_animator == null)
        {
            m_animator = travelingHaldorPrefab.AddComponent<Animator>();
            m_animator.runtimeAnimatorController = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()
                .FirstOrDefault(a => a.name.Contains("HumanoidMonster"));
        }

        m_lookAt = travelingHaldorPrefab.GetComponentInChildren<LookAt>();
        if (m_lookAt == null)
        {
            m_lookAt = travelingHaldorPrefab.AddComponent<LookAt>();
        }

        Assembly assembly = Assembly.GetExecutingAssembly();
        Harmony harmony = new(ModGUID);
        harmony.PatchAll(assembly);

        m_randomGreets = customGreetings.Value.Split(';').ToList();
        m_randomGoodbye = customGoodbyes.Value.Split(';').ToList();
        m_randomStartTrade = customTradeDialogues.Value.Split(';').ToList();

        StartCoroutine(DelayedTraderSetup());
    }


    private void Start()
    {
        InitConfig();
        StartCoroutine(SpawnEventLoop());
    }

    private IEnumerator DelayedTraderSetup()
    {
        yield return new WaitUntil(() => ObjectDB.instance != null); // Wait for ObjectDB to load

        ConfigureTrader(travelingHaldorTrader);
    }
    void ConfigureTrader(Trader trader)
    {
        if (trader == null) return; // Prevent null errors

        if (trader.m_animator == null)
        {
            trader.m_animator = trader.GetComponentInChildren<Animator>();
            if (trader.m_animator == null)
            {
                trader.m_animator = trader.gameObject.AddComponent<Animator>();
                trader.m_animator.runtimeAnimatorController = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()
                    .FirstOrDefault(a => a.name.Contains("HumanoidMonster")); // Example controller
            }
        }

        if (trader.m_lookAt == null)
        {
            trader.m_lookAt = trader.GetComponentInChildren<LookAt>() ?? trader.gameObject.AddComponent<LookAt>();
        }

        trader.m_items = new List<Trader.TradeItem>();
        string[] items = traderItemConfig.Value.Split(';');

        foreach (string itemData in items)
        {
            string[] parts = itemData.Split(',');
            if (parts.Length < 3) continue;

            string itemName = parts[0].Trim();
            if (!int.TryParse(parts[1], out int amount) || !int.TryParse(parts[2], out int price))
                continue;

            string requiredGlobalKey = parts.Length > 3 ? parts[3].Trim() : null;

            ItemDrop itemDrop = GetItemDrop(itemName);
            if (itemDrop != null)
            {
                trader.m_items.Add(new Trader.TradeItem
                {
                    m_prefab = itemDrop,
                    m_stack = amount,
                    m_price = price,
                    m_requiredGlobalKey = requiredGlobalKey // Add this line
                });
            }
        }

        trader.m_name = "$npc_haldor";  // Set custom name

        // Use custom dialogues from config
        trader.m_randomGreets = customGreetings.Value.Split(';').ToList();
        trader.m_randomGoodbye = customGoodbyes.Value.Split(';').ToList(); // Correct field name
        trader.m_randomStartTrade = customTradeDialogues.Value.Split(';').ToList();

        // Ensure lists are not empty
        if (trader.m_randomGreets == null || trader.m_randomGreets.Count == 0)
        {
            trader.m_randomGreets = new List<string> { "Hello!" };
        }

        if (trader.m_randomGoodbye == null || trader.m_randomGoodbye.Count == 0) // Correct field name
        {
            trader.m_randomGoodbye = new List<string> { "Goodbye!" }; // Correct field name
        }

        if (trader.m_randomStartTrade == null || trader.m_randomStartTrade.Count == 0)
        {
            trader.m_randomStartTrade = new List<string> { "Let's trade!" };
        }

        if (trader.m_randomTalk == null || trader.m_randomTalk.Count == 0)
        {
            trader.m_randomTalk = new List<string> { "Stupid Lox! Whoa!!" };
        }
    }

    private ItemDrop GetItemDrop(string itemName)
    {
        GameObject itemPrefab = ObjectDB.instance.GetItemPrefab(itemName);
        return itemPrefab ? itemPrefab.GetComponent<ItemDrop>() : null;
    }

    private IEnumerator SpawnEventLoop()
    {
        yield return new WaitUntil(() => Player.m_localPlayer != null);

        while (true)
        {
            yield return new WaitForSeconds(ConvertDaysToSeconds(eventInterval.Value));

            // Check for global key requirement
            if (!string.IsNullOrEmpty(globalKeyRequirement.Value) && !ZoneSystem.instance.GetGlobalKey(globalKeyRequirement.Value))
            {
                continue; // Skip the event if the global key requirement is not met
            }

            // Check for specific spawn time
            if (specificSpawnTime.Value != SpawnTime.Always)
            {
                bool isDaytime = EnvMan.IsDay();
                if ((specificSpawnTime.Value == SpawnTime.Day && !isDaytime) ||
                    (specificSpawnTime.Value == SpawnTime.Night && isDaytime))
                {
                    continue; // Skip the event if the specific spawn time does not match
                }
            }

            if (UnityEngine.Random.value < eventChance.Value)
            {
                StartCoroutine(SpawnTravelingHaldor());
            }
        }
    }



    private GameObject activeTraderInstance;
    private IEnumerator SpawnTravelingHaldor()
    {
        // Check if the max Haldor limit is reached
        if (activeHaldors.Count >= maxHaldorCount.Value)
        {
            Debug.Log($"[TravelingHaldor] Max Haldor count ({maxHaldorCount.Value}) reached. No new Haldor will spawn.");
            yield break; // Exit the coroutine
        }

        Vector3 spawnPosition;

        if (enableLocationVariability.Value)
        {
            string[] regions = spawnRegions.Value.Split(';');
            string randomRegion = regions[UnityEngine.Random.Range(0, regions.Length)];
            spawnPosition = GetRandomSpawnPosition(randomRegion);
        }
        else
        {
            float randomDistance = UnityEngine.Random.Range(15f, spawnDistance.Value);
            spawnPosition = Player.m_localPlayer.transform.position + UnityEngine.Random.insideUnitSphere * randomDistance;
        }

        spawnPosition.y = ZoneSystem.instance.GetGroundHeight(spawnPosition);

        GameObject newHaldor = Instantiate(travelingHaldorPrefab, spawnPosition, Quaternion.identity);
        activeHaldors.Add(newHaldor); // Track the new instance

        EffectList vfxSpawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("vfx_spawn_travelinghaldor") } } };
        vfxSpawnEffect.Create(spawnPosition, Quaternion.identity, null, 1f, 0);

        EffectList sfxSpawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("sfx_spawn_travelinghaldor") } } };
        sfxSpawnEffect.Create(spawnPosition, Quaternion.identity, null, 1f, 0);

        StartCoroutine(DespawnTravelingHaldor(newHaldor, eventDuration.Value));
    }


    private Vector3 GetRandomSpawnPosition(string region)
    {
        // Replace this with your logic to get a random position within the specified region
        // For simplicity, we'll just return a random position near the player
        float randomDistance = UnityEngine.Random.Range(15f, 30f);
        return Player.m_localPlayer.transform.position + UnityEngine.Random.insideUnitSphere * randomDistance;
    }

    private IEnumerator DespawnTravelingHaldor(GameObject haldor, float duration)
    {
        yield return new WaitForSeconds(duration);

        if (haldor != null)
        {
            Vector3 despawnPosition = haldor.transform.position;

            // Play despawn VFX
            EffectList vfxDespawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("vfx_spawn_travelinghaldor") } } };
            vfxDespawnEffect.Create(despawnPosition, Quaternion.identity, null, 1f, 0);

            // Play despawn SFX
            EffectList sfxDespawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("sfx_spawn_travelinghaldor") } } };
            sfxDespawnEffect.Create(despawnPosition, Quaternion.identity, null, 1f, 0);

            // Remove the Haldor from the list
            activeHaldors.Remove(haldor);

            // Destroy the instance
            ZNetScene.instance.Destroy(haldor);
        }
    }

    private void ClearAllHaldors()
    {
        int removedCount = 0;

        foreach (Character character in GameObject.FindObjectsOfType<Character>())
        {
            if (character != null && character.name.Contains("TravelingHaldor"))
            {
                ZNetView znv = character.GetComponent<ZNetView>();
                if (znv != null)
                {
                    znv.ClaimOwnership(); // Ensure we can delete networked objects
                    znv.Destroy();
                }
                else
                {
                    ZNetScene.instance.Destroy(character.gameObject);
                }
                removedCount++;
            }
        }

        Debug.Log($"[TravelingHaldor] Removed {removedCount} Traveling Haldor(s).");
        Player.m_localPlayer?.Message(MessageHud.MessageType.TopLeft, $"Removed {removedCount} Traveling Haldor(s)!");
    }


    private float ConvertDaysToSeconds(float days)
    {
        return days * 30 * 60; // Convert days to seconds, where 1 day = 1800 seconds
    }

    private void Update()
    {
        if (activeTraderInstance != null)
        {
            float distanceToPlayer = Vector3.Distance(activeTraderInstance.transform.position, Player.m_localPlayer.transform.position);

            if (distanceToPlayer <= m_greetRange && !m_didGreet)
            {
                m_didGreet = true;
                PlayGreetEffects();
            }

            if (distanceToPlayer <= m_byeRange && !m_didGoodbye)
            {
                m_didGoodbye = true;
                PlayGoodbyeEffects();
            }

            if (distanceToPlayer > m_standRange)
            {
                m_didGreet = false;
                m_didGoodbye = false;
            }
        }
    }

    private void AdjustDialogueBoxPosition()
    {
        GameObject dialogueBox = GameObject.FindObjectOfType<MessageHud>()?.gameObject; // Identify the correct GameObject
        if (dialogueBox != null)
        {
            RectTransform rectTransform = dialogueBox.GetComponent<RectTransform>();
            if (rectTransform != null)
            {
                rectTransform.anchoredPosition = new Vector2(rectTransform.anchoredPosition.x, m_dialogHeight); // Adjust height
            }
        }
    }

    private void PlayGreetEffects()
    {
        if (m_randomGreets.Count > 0)
        {
            string greetMessage = m_randomGreets[UnityEngine.Random.Range(0, m_randomGreets.Count)];
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, greetMessage);

            // Adjust dialogue box position
            AdjustDialogueBoxPosition();
        }

        if (m_randomGreetFX.m_effectPrefabs != null && m_randomGreetFX.m_effectPrefabs.Length > 0)
        {
            m_randomGreetFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }
    }



    private void PlayGoodbyeEffects()
    {
        if (m_randomGoodbye.Count > 0)
        {
            string goodbyeMessage = m_randomGoodbye[UnityEngine.Random.Range(0, m_randomGoodbye.Count)];
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, goodbyeMessage);

            AdjustDialogueBoxPosition();
        }

        if (m_randomGoodbyeFX.m_effectPrefabs != null && m_randomGoodbyeFX.m_effectPrefabs.Length > 0)
        {
            m_randomGoodbyeFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }
    }
}

