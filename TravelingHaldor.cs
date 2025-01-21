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
using UnityEngine.UI; // Add this namespace

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class TravelingHaldor : BaseUnityPlugin
{
    private const string ModName = "TravelingHaldor";
    private const string ModVersion = "1.0.0";
    private const string ModGUID = "org.bepinex.plugins.travelinghaldor";

    private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    private ConfigEntry<float> eventInterval;
    private ConfigEntry<float> eventChance;
    private ConfigEntry<float> eventDuration;
    private GameObject travelingHaldorPrefab;
    private Trader travelingHaldorTrader;
    private ConfigEntry<string> traderItemConfig;

    private float m_byeRange = 5f;
    private float m_standRange = 15f;
    private float m_greetRange = 5f;
    private Animator m_animator;
    private LookAt m_lookAt;
    private bool m_didGreet;
    private bool m_didGoodbye;
    private List<string> m_randomGreets = new List<string> { "Greetings, traveler!", "Come, see my wares!" };
    private List<string> m_randomGoodbye = new List<string> { "Safe travels!", "Until we meet again!" };
    private EffectList m_randomGreetFX = new EffectList();
    private EffectList m_randomGoodbyeFX = new EffectList();
    private EffectList m_greetSFX = new EffectList();
    private EffectList m_yeaSFX = new EffectList();


    private Text interactText; // Add this field
    private bool isPlayerNearHaldor = false; // Add this field

    void InitConfig()
    {
        eventInterval = config("Event Settings", "Event Interval (Days)", 3f, "How often (in in-game days) Traveling Haldor appears.");
        eventChance = config("Event Settings", "Event Chance", 0.5f, "Chance (0-1) of event occurring when interval is reached.");
        eventDuration = config("Event Settings", "Event Duration", 250f, "How long (seconds) the trader stays.");
        traderItemConfig = config("Trader Settings", "TradeItems",
            "HelmetYule,1,100;HelmetDverger,1,650;BeltStrength,1,950;YmirRemains,1,120,defeated_gdking;FishingRod,1,350;FishingBait,20,10;Thunderstone,1,50,defeated_gdking;ChickenEgg,1,1500,defeated_goblinking;BarrelRings,3,100",
            "List of items for Traveling Haldor to sell. Format: PrefabName,Amount,Cost,RequiredGlobalKey. Separate multiple items with ';'.");
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
        GameObject vfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "vfx_spawn_travelinghaldor");
        GameObject sfx_spawn_travelinghaldor = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_spawn_travelinghaldor");
        GameObject sfxTravelHaldorGreet = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_travelhaldor_greet");
        GameObject sfxTravelingHaldorYea = ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "sfx_travelinghaldor_yea");
        m_greetSFX.m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("sfx_travelhaldor_greet") } };
        m_yeaSFX.m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("sfx_travelinghaldor_yea") } };


        Creature travelingHaldor = new("hangryaldor", "TravelingHaldor")
        {
            Biome = Heightmap.Biome.None,
            CreatureFaction = Character.Faction.Players,
            Maximum = 1
        };

        travelingHaldor.Localize().English("Traveling Haldor");

        travelingHaldorPrefab = travelingHaldor.Prefab;

        // Add Trader component, but configure later
        travelingHaldorTrader = travelingHaldorPrefab.AddComponent<Trader>();

        // Ensure the prefab has an Animator component
        m_animator = travelingHaldorPrefab.GetComponentInChildren<Animator>();
        if (m_animator == null)
        {
            m_animator = travelingHaldorPrefab.AddComponent<Animator>();
            m_animator.runtimeAnimatorController = Resources.FindObjectsOfTypeAll<RuntimeAnimatorController>()
                .FirstOrDefault(a => a.name.Contains("HumanoidMonster")); // Example controller
        }

        // Ensure the prefab has a LookAt component
        m_lookAt = travelingHaldorPrefab.GetComponentInChildren<LookAt>();
        if (m_lookAt == null)
        {
            m_lookAt = travelingHaldorPrefab.AddComponent<LookAt>();
        }

        // Load Harmony patches
        Assembly assembly = Assembly.GetExecutingAssembly();
        Harmony harmony = new(ModGUID);
        harmony.PatchAll(assembly);

        // Find and cache the InteractText UI element
        interactText = GameObject.Find("InteractText")?.GetComponent<Text>();

        // Delay configuring trader items
        StartCoroutine(DelayedTraderSetup());
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

        trader.m_name = "Traveling Haldor";  // Set custom name

        // Ensure lists are not empty
        if (trader.m_randomGreets == null || trader.m_randomGreets.Count == 0)
        {
            trader.m_randomGreets = new List<string> { "Hello!" };
        }

        if (trader.m_randomGoodbye == null || trader.m_randomGoodbye.Count == 0)
        {
            trader.m_randomGoodbye = new List<string> { "Goodbye!" };
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

    private void Start()
    {
        InitConfig();
        StartCoroutine(SpawnEventLoop());
    }

    private IEnumerator SpawnEventLoop()
    {
        yield return new WaitUntil(() => Player.m_localPlayer != null);

        while (true)
        {
            yield return new WaitForSeconds(ConvertDaysToSeconds(eventInterval.Value));

            if (UnityEngine.Random.value < eventChance.Value)
            {
                StartCoroutine(SpawnTravelingHaldor());
            }
        }
    }

    private GameObject activeTraderInstance;

    private IEnumerator SpawnTravelingHaldor()
    {
        if (activeTraderInstance != null)
        {
            ZNetScene.instance.Destroy(activeTraderInstance);
            yield return new WaitForSeconds(1f);
        }

        float randomDistance = UnityEngine.Random.Range(15f, 30f); // Randomize spawn distance
        Vector3 spawnPosition = Player.m_localPlayer.transform.position + UnityEngine.Random.insideUnitSphere * randomDistance;
        spawnPosition.y = ZoneSystem.instance.GetGroundHeight(spawnPosition);

        activeTraderInstance = Instantiate(travelingHaldorPrefab, spawnPosition, Quaternion.identity);

        // Play spawn VFX
        EffectList vfxSpawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("vfx_spawn_travelinghaldor") } } };
        vfxSpawnEffect.Create(spawnPosition, Quaternion.identity, null, 1f, 0);

        // Play spawn SFX
        EffectList sfxSpawnEffect = new EffectList { m_effectPrefabs = new[] { new EffectList.EffectData { m_prefab = ZNetScene.instance.GetPrefab("sfx_spawn_travelinghaldor") } } };
        sfxSpawnEffect.Create(spawnPosition, Quaternion.identity, null, 1f, 0);

        // Start despawn timer
        StartCoroutine(DespawnTravelingHaldor(eventDuration.Value));
    }

    private IEnumerator DespawnTravelingHaldor(float duration)
    {
        yield return new WaitForSeconds(duration);
        if (activeTraderInstance != null)
        {
            ZNetScene.instance.Destroy(activeTraderInstance);
            activeTraderInstance = null;
            Debug.Log("Traveling Haldor despawned successfully."); // Debug message
        }
    }


    private float ConvertDaysToSeconds(float days)
    {
        return days * 24 * 60 * 60;
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

    private void PlayGreetEffects()
    {
        if (m_randomGreets.Count > 0)
        {
            string greetMessage = m_randomGreets[UnityEngine.Random.Range(0, m_randomGreets.Count)];
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, greetMessage);
        }

        if (m_randomGreetFX.m_effectPrefabs != null && m_randomGreetFX.m_effectPrefabs.Length > 0)
        {
            m_randomGreetFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }

        if (m_greetSFX.m_effectPrefabs != null && m_greetSFX.m_effectPrefabs.Length > 0)
        {
            m_greetSFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }
    }

    private void PlayGoodbyeEffects()
    {
        if (m_randomGoodbye.Count > 0)
        {
            string goodbyeMessage = m_randomGoodbye[UnityEngine.Random.Range(0, m_randomGoodbye.Count)];
            Player.m_localPlayer.Message(MessageHud.MessageType.TopLeft, goodbyeMessage);
        }

        if (m_randomGoodbyeFX.m_effectPrefabs != null && m_randomGoodbyeFX.m_effectPrefabs.Length > 0)
        {
            m_randomGoodbyeFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }

        // Play "yea" sound effect
        if (m_yeaSFX.m_effectPrefabs != null && m_yeaSFX.m_effectPrefabs.Length > 0)
        {
            m_yeaSFX.Create(activeTraderInstance.transform.position, Quaternion.identity, null, 1f, 0);
        }
    }
}
