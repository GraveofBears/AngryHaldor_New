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

        // Define AngryHaldor using CreatureManager
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
            DefaultTraderItems,
            "List of items sold by the trader in the format: ItemName,Stack,Price:ItemName,Stack,Price"
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
        // Check in-game day for TravelingHaldor spawn
        if (EnvMan.instance.GetDayFraction() < 0.01 && EnvMan.instance.GetCurrentDay() >= nextTravelingTraderSpawnDay)
        {
            nextTravelingTraderSpawnDay = EnvMan.instance.GetCurrentDay() + TravelingTraderSpawnIntervalDays;
            SpawnTravelingHaldor();
        }
    }
    private void AddAndConfigureTrader(GameObject travelingHaldor)
    {
        // Dynamically add the Trader component
        var trader = travelingHaldor.AddComponent<Trader>();

        // Configure trader items dynamically
        AddTraderItems(trader);
    }

    private void SpawnTravelingHaldor()
    {
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            Logger.LogWarning("Player not found. Cannot spawn TravelingHaldor.");
            return;
        }

        Vector3 spawnPosition = player.transform.position + Random.insideUnitSphere * 20f; // Spawn within 20m radius
        spawnPosition.y = ZoneSystem.instance.GetGroundHeight(spawnPosition);

        GameObject prefab = ZNetScene.instance.GetPrefab("TravelingHaldor");
        if (prefab == null)
        {
            Logger.LogError("TravelingHaldor prefab not found in ZNetScene!");
            return;
        }

        GameObject travelingHaldor = Instantiate(prefab, spawnPosition, Quaternion.identity);
        if (travelingHaldor == null)
        {
            Logger.LogError("Failed to instantiate TravelingHaldor.");
            return;
        }

        travelingHaldor.name = "TravelingHaldor";

        // Add and configure the Trader component
        AddAndConfigureTrader(travelingHaldor);

        // Start despawn timer
        StartCoroutine(DespawnTravelingHaldor(travelingHaldor));
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
    }


    private IEnumerator DespawnTravelingHaldor(GameObject travelingHaldor)
    {
        yield return new WaitForSeconds(TravelingTraderWanderDurationSeconds);

        if (travelingHaldor != null)
        {
            Destroy(travelingHaldor);
            Logger.LogInfo("Despawned Traveling Haldor.");
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
            if (hold) return true; // Allow holding to proceed as normal

            Player player = character as Player;
            if (player == null) return true; // Not a player

            AngryHaldor.Instance.Logger.LogInfo("Player interacted with Haldor.");

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
