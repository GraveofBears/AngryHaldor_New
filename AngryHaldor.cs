/**
 * AngryHaldor Mod
 * Makes Haldor turn into AngryHaldor when hit or if the player lingers too long without buying anything.
 * Resets AngryHaldor back to Haldor after 300 seconds, upon player death, or when leaving the area.
 */

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

[BepInPlugin(ModGUID, ModName, ModVersion)]
public class AngryHaldor : BaseUnityPlugin
{
    private const string ModName = "AngryHaldor";
    private const string ModVersion = "1.4.0";
    private const string ModGUID = "org.bepinex.plugins.angryhaldor";

    private static readonly ConfigSync configSync = new(ModName) { DisplayName = ModName, CurrentVersion = ModVersion, MinimumRequiredVersion = ModVersion };

    public static AngryHaldor Instance { get; private set; }

    private const string AngryHaldorPrefabName = "AngryHaldor";
    private const string AngryHalsteinPrefabName = "AngryHalstein";
    private const string HaldorObjectName = "Haldor";
    private const float TimeoutBeforeAnger = 30f; // Seconds before Haldor gets angry
    public GameObject travelingHaldorPrefab;
    private static GameObject haldorCache;
    private static GameObject halsteinCache;
    private static GameObject forceFieldCache;
    private static GameObject spawnedLox;
    private static bool isAngryHaldorSpawned = false;

    private const float TravelingTraderSpawnIntervalDays = 10f; // Spawn every 10 in-game days
    private const float TravelingTraderWanderDurationSeconds = 600f; // Despawn after 600 seconds
    private static float nextTravelingTraderSpawnDay = 0;

    // Configuration variables for TravelingHaldor items
    private ConfigEntry<string> configTraderItems;
    // Config format: ItemName,Stack,Price:ItemName,Stack,Price
    private const string DefaultTraderItems = "BarrelRings,1,100:FishingBait,20,350";
    private ConfigEntry<float> configSpawnIntervalDays;
    private GameObject travelingHaldorInstance;

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
        //Register attack
        ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "HaldorAttack");
        ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "HalsteinBite");
        ItemManager.PrefabManager.RegisterPrefab("hangryaldor", "HalsteinStomp");



        Creature AngryHaldor = new("hangryaldor", AngryHaldorPrefabName)
        {
            Biome = Heightmap.Biome.None,
            CreatureFaction = Character.Faction.ForestMonsters,
            AttackImmediately = true,
            Maximum = 1
        };
        AngryHaldor.Localize()
            .English("Angry Haldor");
        AngryHaldor.Drops["Coins"].Amount = new Range(1, 2);
        AngryHaldor.Drops["Coins"].DropChance = 100f;

        Creature AngryHalstein = new("hangryaldor", AngryHalsteinPrefabName)
        {
            Biome = Heightmap.Biome.None,
            CreatureFaction = Character.Faction.ForestMonsters,
            AttackImmediately = true,
            Maximum = 1
        };
        AngryHalstein.Localize()
            .English("Angry Halstein");
        AngryHalstein.Drops["LoxMeat"].Amount = new Range(1, 2);
        AngryHalstein.Drops["LoxMeat"].DropChance = 100f;

        Creature travelingHaldor = new("hangryaldor", "TravelingHaldor")
        {
            Biome = Heightmap.Biome.None,
            CreatureFaction = Character.Faction.Dverger,
            Maximum = 1
        };
        travelingHaldor.Localize().English("Traveling Haldor");
        // Add configuration for trader items
        configTraderItems = config(
            "Trader",
            "Items",
            "BarrelRings,1,100:FishingBait,20,350",
            "List of items sold by the trader in the format: ItemName,Stack,Price:ItemName,Stack,Price"
        );

        configSpawnIntervalDays = config(
            "Trader",
            "SpawnIntervalDays",
            10f,
            "Interval (in in-game days) between TravelingHaldor spawns"
        );


        // Assign the singleton instance
        Instance = this;

        // Load Harmony patches
        Assembly assembly = Assembly.GetExecutingAssembly();
        Harmony harmony = new(ModGUID);
        harmony.PatchAll(assembly);
    }

    void Update()
    {
        if (travelingHaldorInstance != null)
        {
            UpdateTraderBehavior(travelingHaldorInstance);
        }
    }


    private bool didGreet = false;

    private void UpdateTraderBehavior(GameObject travelingHaldor)
    {
        // Find the closest player within interaction range
        Player closestPlayer = Player.GetClosestPlayer(travelingHaldor.transform.position, 15f);
        if (closestPlayer == null)
        {
            didGreet = false; // Reset greeting when no players are nearby
            return;
        }

        float distance = Vector3.Distance(closestPlayer.transform.position, travelingHaldor.transform.position);

        if (distance < 5f && !didGreet)
        {
            didGreet = true;
            GreetPlayer("Hello, traveler! Care to browse my wares?");
        }
    }

    private void GreetPlayer(string message)
    {
        // Display the greeting in the chat
        Chat.instance.SetNpcText(null, Vector3.up * 1.5f, 20f, 5f, "", message, false);
        Logger.LogInfo($"Greeted the player with message: {message}");
    }

    private bool envManInitialized = false;

    private IEnumerator WaitForEnvMan()
    {
        if (EnvMan.instance == null)
        {
            Logger.LogInfo("Waiting for EnvMan instance...");
        }

        while (EnvMan.instance == null)
        {
            yield return new WaitForSeconds(0.5f); // Check every 0.5 seconds
        }

        envManInitialized = true;
        Logger.LogInfo("EnvMan instance is ready. Starting logic.");
        InitializeTravelingHaldor();
    }
    private void InitializeTravelingHaldor()
    {
        Logger.LogInfo("TravelingHaldor initialization completed.");
        // Add any initialization logic for TravelingHaldor here.
    }


    private void AddAndConfigureTrader(GameObject travelingHaldor)
    {
        if (!travelingHaldor.TryGetComponent(out ZNetView zNetView))
        {
            zNetView = travelingHaldor.AddComponent<ZNetView>();
            Logger.LogInfo("Added ZNetView to TravelingHaldor.");
        }

        if (!travelingHaldor.TryGetComponent(out Trader trader))
        {
            trader = travelingHaldor.AddComponent<Trader>();
            Logger.LogInfo("Added Trader component to TravelingHaldor.");
        }

        trader.m_name = "Traveling Trader";
        trader.m_items = new List<Trader.TradeItem>();

        // Populate random talk lists with at least one entry to prevent index errors
        trader.m_randomTalk = new List<string> { "Welcome, traveler!", "Care to browse my wares?" };
        trader.m_randomGreets = new List<string> { "Hello there!", "Greetings!" };
        trader.m_randomGoodbye = new List<string> { "Safe travels!", "Come back soon!" };

        // Nullify the animator to disable animations
        trader.m_animator = null; // Prevent animations
        Logger.LogInfo("Disabled Trader animations by nullifying Animator.");

        AddTraderItems(trader); // Populate the trader items dynamically.

        if (!travelingHaldor.TryGetComponent(out BoxCollider collider))
        {
            collider = travelingHaldor.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            collider.size = new Vector3(2f, 2f, 2f); // Adjust collider size as needed
            Logger.LogInfo("Added BoxCollider to TravelingHaldor.");
        }
    }


    private void SuppressUnnecessaryUpdates(Trader trader)
    {
        var animator = trader.GetComponentInChildren<Animator>();
        if (animator == null)
        {
            animator = trader.gameObject.AddComponent<Animator>(); // Add a placeholder Animator to avoid errors
            Logger.LogWarning("Added a placeholder Animator to TravelingHaldor.");
        }

        trader.m_animator = animator;
    }
    private void UpdateTraderInteraction(GameObject travelingHaldor)
    {
        Player closestPlayer = Player.GetClosestPlayer(travelingHaldor.transform.position, 15f);
        if (closestPlayer == null) return;

        float distance = Vector3.Distance(closestPlayer.transform.position, travelingHaldor.transform.position);
        if (distance < 5f)
        {
            Logger.LogInfo("Player is close enough to interact with TravelingHaldor.");
        }
    }


    private void AddTraderItems(Trader trader)
    {
        trader.m_items.Clear();

        string itemsConfig = configTraderItems.Value;
        if (string.IsNullOrWhiteSpace(itemsConfig))
        {
            Logger.LogWarning("Trader items configuration is empty. No items will be added.");
            return;
        }

        string[] items = itemsConfig.Split(':');
        foreach (string itemConfig in items)
        {
            string[] parts = itemConfig.Split(',');
            if (parts.Length != 3)
            {
                Logger.LogWarning($"Invalid trader item entry: {itemConfig}. Skipping.");
                continue;
            }

            string itemName = parts[0].Trim();
            if (!int.TryParse(parts[1].Trim(), out int stack) || stack <= 0)
            {
                Logger.LogWarning($"Invalid stack size for item: {itemName}. Skipping.");
                continue;
            }

            if (!int.TryParse(parts[2].Trim(), out int price) || price < 0)
            {
                Logger.LogWarning($"Invalid price for item: {itemName}. Skipping.");
                continue;
            }

            ItemDrop itemPrefab = ZNetScene.instance.GetPrefab(itemName)?.GetComponent<ItemDrop>();
            if (itemPrefab == null)
            {
                Logger.LogWarning($"Item prefab not found: {itemName}. Skipping.");
                continue;
            }

            trader.m_items.Add(new Trader.TradeItem
            {
                m_prefab = itemPrefab,
                m_stack = stack,
                m_price = price
            });

            Logger.LogInfo($"Added trader item: {itemName}, Stack: {stack}, Price: {price}");
        }

        Logger.LogInfo($"Trader has {trader.m_items.Count} items configured.");
    }


    private void SpawnTravelingHaldor()
    {
        var prefab = ZNetScene.instance.GetPrefab("TravelingHaldor");
        if (prefab == null)
        {
            Logger.LogError("TravelingHaldor prefab not found in ZNetScene!");
            return;
        }

        Vector3 spawnPosition = Player.m_localPlayer.transform.position + Vector3.forward * 10f; // Spawn 10 units in front of the player
        travelingHaldorInstance = Instantiate(prefab, spawnPosition, Quaternion.identity);
        travelingHaldorInstance.name = "TravelingHaldor";

        AddAndConfigureTrader(travelingHaldorInstance);
        Logger.LogInfo("Spawned TravelingHaldor.");
    }

    private void ConfigureInteraction(GameObject travelingHaldor)
    {
        if (!travelingHaldor.TryGetComponent(out Interactable interactable))
        {
            travelingHaldor.AddComponent<CustomTraderInteraction>(); // Use a custom interaction handler if needed.
        }
    }


    private static GameObject FindHaldorInstance()
    {
        if (haldorCache != null && halsteinCache != null) return haldorCache;

        AngryHaldor.Instance.Logger.LogInfo("Searching for Haldor, Halstein, and ForceField in the main scene...");

        foreach (var obj in GameObject.FindObjectsOfType<GameObject>())
        {
            if (obj.name == HaldorObjectName || obj.name == $"{HaldorObjectName}(Clone)")
            {
                AngryHaldor.Instance.Logger.LogInfo($"Found Haldor GameObject at {obj.transform.position}");
                haldorCache = obj;
            }

            if (obj.name == "ForceField")
            {
                AngryHaldor.Instance.Logger.LogInfo($"Found ForceField GameObject at {obj.transform.position}");
                forceFieldCache = obj;
            }

            if (obj.name == "Halstein")
            {
                AngryHaldor.Instance.Logger.LogInfo($"Found Halstein GameObject at {obj.transform.position}");
                halsteinCache = obj;
            }
        }

        if (haldorCache == null) AngryHaldor.Instance.Logger.LogError("Haldor GameObject not found in the main scene!");
        if (forceFieldCache == null) AngryHaldor.Instance.Logger.LogError("ForceField GameObject not found in the main scene!");
        if (halsteinCache == null) AngryHaldor.Instance.Logger.LogError("Halstein GameObject not found in the main scene!");

        return haldorCache;
    }

    private void DespawnTravelingHaldor()
    {
        if (travelingHaldorInstance != null)
        {
            Destroy(travelingHaldorInstance);
            travelingHaldorInstance = null;
            Logger.LogInfo("Despawned TravelingHaldor.");
        }
    }



    private static void MakeCreaturesIgnoreEffectArea(GameObject creature)
    {
        var effectAreaComponents = creature.GetComponentsInChildren<EffectArea>();
        foreach (var effectArea in effectAreaComponents)
        {
            GameObject.Destroy(effectArea);
        }

        AngryHaldor.Instance.Logger.LogInfo($"Removed EffectArea components from {creature.name}");
    }

    private static void ForceTargetPlayer(GameObject creature)
    {
        var monsterAI = creature.GetComponent<MonsterAI>();
        if (monsterAI != null)
        {
            var player = Player.m_localPlayer;
            if (player != null && monsterAI.m_nview.IsOwner())
            {
                AngryHaldor.Instance.Logger.LogInfo($"{creature.name} is now targeting and attacking player {player.GetPlayerName()}...");
                monsterAI.SetTarget(player.GetComponent<Character>()); // Set the player as the attack target
                monsterAI.ResetPatrolPoint(); // Clear patrol behavior
                monsterAI.m_alerted = true; // Mark as alerted
            }
        }
        else
        {
            AngryHaldor.Instance.Logger.LogWarning($"{creature.name} does not have a MonsterAI component.");
        }
    }
    private static void DebugSceneHierarchy()
    {
        AngryHaldor.Instance.Logger.LogInfo("Scene Hierarchy Debug:");
        foreach (var obj in GameObject.FindObjectsOfType<GameObject>())
        {
            AngryHaldor.Instance.Logger.LogInfo($"Object: {obj.name}, Path: {GetFullPath(obj.transform)}");
        }
    }

    private static string GetFullPath(Transform transform)
    {
        return transform.parent == null ? transform.name : $"{GetFullPath(transform.parent)}/{transform.name}";
    }

    private static void DisableForceFieldAndNoMonsterArea()
    {
        // Locate Vendor_BlackForest(Clone) in the scene
        var vendorLocation = GameObject.FindObjectsOfType<GameObject>()
            .FirstOrDefault(obj => obj.name == "Vendor_BlackForest(Clone)");
        if (vendorLocation == null)
        {
            AngryHaldor.Instance.Logger.LogWarning("Vendor_BlackForest(Clone) not found in the scene.");
            return;
        }

        // Search for ForceField under Vendor_BlackForest(Clone)
        var forceField = vendorLocation.GetComponentsInChildren<Transform>()
            .FirstOrDefault(child => child.name == "ForceField");
        if (forceField == null)
        {
            AngryHaldor.Instance.Logger.LogWarning("ForceField not found under Vendor_BlackForest(Clone).");
            return;
        }

        AngryHaldor.Instance.Logger.LogInfo("Disabling ForceField...");
        forceField.gameObject.SetActive(false);

        // Search for NoMonsterArea under ForceField
        var noMonsterArea = forceField.GetComponentsInChildren<Transform>()
            .FirstOrDefault(child => child.name == "NoMonsterArea");
        if (noMonsterArea == null)
        {
            AngryHaldor.Instance.Logger.LogWarning("NoMonsterArea not found under ForceField.");
            return;
        }

        AngryHaldor.Instance.Logger.LogInfo("Disabling NoMonsterArea...");
        noMonsterArea.gameObject.SetActive(false);
    }

    private static void TransformHaldorToAngry(GameObject haldor)
    {
        if (isAngryHaldorSpawned)
        {
            AngryHaldor.Instance.Logger.LogInfo("AngryHaldor is already spawned. Skipping transformation.");
            return;
        }

        isAngryHaldorSpawned = true;

        AngryHaldor.Instance.Logger.LogInfo("Disabling ForceField and NoMonsterArea before spawning AngryHaldor and AngryHalstein...");
        DisableForceFieldAndNoMonsterArea(); // Ensure this is invoked

        AngryHaldor.Instance.Logger.LogInfo("Transforming Haldor to AngryHaldor...");
        var haldorObject = FindHaldorInstance();
        if (haldorObject == null)
        {
            AngryHaldor.Instance.Logger.LogError("Failed to transform Haldor: Haldor instance not found.");
            return;
        }

        DisableHaldor(haldorObject);

        // Spawn AngryHalstein
        if (halsteinCache != null)
        {
            AngryHaldor.Instance.Logger.LogInfo("Disabling Halstein...");
            halsteinCache.SetActive(false);

            var angryHalsteinPrefab = ZNetScene.instance.GetPrefab(AngryHalsteinPrefabName);
            if (angryHalsteinPrefab == null)
            {
                AngryHaldor.Instance.Logger.LogError("AngryHalstein prefab not found in ZNetScene!");
            }
            else
            {
                AngryHaldor.Instance.Logger.LogInfo("Spawning AngryHalstein...");
                spawnedLox = Instantiate(angryHalsteinPrefab, halsteinCache.transform.position, halsteinCache.transform.rotation);
                spawnedLox.name = "AngryHalstein";
                MakeCreaturesIgnoreEffectArea(spawnedLox);
                ForceTargetPlayer(spawnedLox);
            }
        }

        // Spawn AngryHaldor
        Vector3 originalPosition = haldorObject.transform.position;
        Quaternion originalRotation = haldorObject.transform.rotation;

        var angryHaldorPrefab = ZNetScene.instance.GetPrefab(AngryHaldorPrefabName);
        if (angryHaldorPrefab == null)
        {
            AngryHaldor.Instance.Logger.LogError($"Prefab '{AngryHaldorPrefabName}' not found in ZNetScene!");
            return;
        }

        AngryHaldor.Instance.Logger.LogInfo("Spawning AngryHaldor...");
        var angryHaldor = Instantiate(angryHaldorPrefab, originalPosition, originalRotation);
        angryHaldor.name = AngryHaldorPrefabName;
        MakeCreaturesIgnoreEffectArea(angryHaldor);
        ForceTargetPlayer(angryHaldor);

        AngryHaldor.Instance.Logger.LogInfo("AngryHaldor successfully spawned!");
    }

    public static IEnumerator ResetAll()
    {
        isAngryHaldorSpawned = false;

        if (spawnedLox != null)
        {
            AngryHaldor.Instance.Logger.LogInfo("Despawning the spawned AngryHalstein...");
            Destroy(spawnedLox);
            spawnedLox = null;
        }

        if (haldorCache != null)
        {
            AngryHaldor.Instance.Logger.LogInfo("Re-enabling Haldor...");
            haldorCache.SetActive(true);

            var traderComponent = haldorCache.GetComponent<Trader>();
            if (traderComponent != null) traderComponent.enabled = true;

            var colliders = haldorCache.GetComponentsInChildren<Collider>();
            foreach (var collider in colliders)
            {
                collider.enabled = true;
            }
        }

        if (forceFieldCache != null)
        {
            AngryHaldor.Instance.Logger.LogInfo("Re-enabling ForceField...");
            forceFieldCache.SetActive(true);
        }

        if (halsteinCache != null)
        {
            AngryHaldor.Instance.Logger.LogInfo("Re-enabling Halstein...");
            halsteinCache.SetActive(true);
        }

        yield break;
    }

    private static void DisableHaldor(GameObject haldorObject)
    {
        AngryHaldor.Instance.Logger.LogInfo("Disabling Haldor and all child objects...");

        foreach (Transform child in haldorObject.transform)
        {
            child.gameObject.SetActive(false);
        }

        haldorObject.SetActive(false);

        var traderComponent = haldorObject.GetComponent<Trader>();
        if (traderComponent != null)
        {
            traderComponent.enabled = false;
        }

        var colliders = haldorObject.GetComponentsInChildren<Collider>();
        foreach (var collider in colliders)
        {
            collider.enabled = false;
        }
    }
    [HarmonyPatch(typeof(Trader), nameof(Trader.Interact))]
    public static class TraderInteractPatch
    {
        static bool Prefix(Trader __instance, ref bool __result, Humanoid character, bool hold, bool alt)
        {
            if (hold) return true;

            Player player = character as Player;
            if (player == null) return true;

            AngryHaldor.Instance.Logger.LogInfo($"Player {player.GetPlayerName()} interacted with {__instance.name}.");


            // Allow interaction to proceed
            bool canInteract = __instance.GetComponent<ZNetView>()?.IsValid() == true;
            if (!canInteract)
            {
                __result = false; // Prevent interaction
                return false;
            }

            // Start the anger timer if not already running
            if (!AngryHaldor.isAngryHaldorSpawned)
            {
                AngryHaldor.Instance.Logger.LogInfo("Starting anger timeout...");
                AngryHaldor.Instance.StartCoroutine(AngryHaldor.StartAngerTimeout(__instance.gameObject));
            }

            return true; // Allow original interaction logic to proceed
        }
    }
    public static IEnumerator StartAngerTimeout(GameObject haldor)
    {
        AngryHaldor.Instance.Logger.LogInfo("Starting anger timeout timer...");
        yield return new WaitForSeconds(TimeoutBeforeAnger);

        AngryHaldor.Instance.Logger.LogInfo("Anger timeout expired. Transforming Haldor to AngryHaldor...");
        if (haldor != null) TransformHaldorToAngry(haldor);
    }
}
