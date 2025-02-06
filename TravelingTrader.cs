using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TravelingHaldor
{
    /// <summary>
    /// Created a custom Trader to contain the changes required
    /// This way, any patches can directly check if the game object has our custom component instead of checking against the prefab name
    /// We can now hold all references of the instantiated traveling traders within this scope
    /// So whenever they instantiate (awake), they are automatically added to the list
    /// Whenever they are destroyed, they are automatically removed from the list
    /// </summary>
    public class TravelingTrader : Trader
    {
        public static GameObject travelingHaldorPrefab = null!;
        public static readonly List<TravelingTrader> m_instances = new();
        public static readonly EffectList m_spawnEffects = new();
        public static double m_lastEventSpawn = 0.0;

        public ZNetView m_nview = null!;
        public AnimalAI m_animalAI = null!;
        public bool m_startDespawn;
        public float m_despawnTimer;
        public void Awake()
        {
            m_nview = GetComponent<ZNetView>();
            m_animalAI = GetComponent<AnimalAI>();
            m_instances.Add(this);
            m_randomGreets = TravelingHaldor.customGreetings.Value.Split(';').ToList();
            m_randomGoodbye = TravelingHaldor.customGoodbyes.Value.Split(';').ToList();
            m_randomStartTrade = TravelingHaldor.customTradeDialogues.Value.Split(';').ToList();
            m_randomTalk = TravelingHaldor.customTradeDialogues.Value.Split(';').ToList();

            if (m_randomGreets.Count <= 0) m_randomGreets.Add("Hello!");
            if (m_randomGoodbye.Count <= 0) m_randomGoodbye.Add("Goodbye!");
            if (m_randomStartTrade.Count <= 0) m_randomStartTrade.Add("Let's trade!");
            if (m_randomBuy.Count <= 0) m_randomBuy.Add("Good choice!");
            if (m_randomSell.Count <= 0) m_randomSell.Add("Deal!");
            if (m_randomTalk.Count <= 0) m_randomTalk.Add("Stupid Lox! Whoa!!");

            m_dialogHeight = TravelingHaldor.dialogueHeight.Value;
            // Invoke the method delayed spawn in (eventDuration.Value)
            if (!m_nview.IsOwner()) return;
            Invoke(nameof(DelayedDespawn), TravelingHaldor.eventDuration.Value);
        }

        public void OnDestroy()
        {
            m_instances.Remove(this);
        }

        public void DelayedDespawn()
        {
            // When set to true, the UpdateAI kicks in, and starts the process for the character to move away and despawn
            m_startDespawn = true;
        }

        public void Patch_Update()
        {
            if (Player.GetClosestPlayer(transform.position, Mathf.Max(m_byeRange + 3f, m_standRange)) is { } closestPlayer)
            {
                float distance = Vector3.Distance(closestPlayer.transform.position, transform.position);
                m_lookAt.SetLoockAtTarget(closestPlayer.GetHeadPoint());

                // Since the character is the Lox in this case,
                // Our available triggers are:
                // stagger, consume, attack_bite, attack_stomp

                if (!m_didGreet && distance < m_greetRange)
                {
                    m_didGreet = true;
                    Say(m_randomGreets, "");
                    m_randomGreetFX.Create(transform.position, Quaternion.identity);
                }

                // Changed this because it was wrong, the reverse if statement had to be used,
                // Ended up trying to greet, then immediately try to say goodbye
                if (!m_didGreet || m_didGoodbye || distance <= m_byeRange) return;
                m_didGoodbye = true;
                Say(m_randomGoodbye, "");
                m_randomGreetFX.Create(transform.position, Quaternion.identity);
            }
            else
            {
                m_lookAt.ResetTarget();
            }

            // We return false after this always, since the default update runs extra lines to make Haldor stand up
        }

        public bool Patch_UpdateAI(float dt)
        {
            if (!m_startDespawn) return true; // return true, to use default UpdateAI

            // Since this is a patch, we need to re-write the BaseAI.UpdateAI method
            if (!m_animalAI.m_nview.IsValid()) return false;
            if (!m_animalAI.m_nview.IsOwner())
            {
                m_animalAI.m_alerted = m_animalAI.m_nview.GetZDO().GetBool(ZDOVars.s_alert);
                return false;
            }
            m_animalAI.UpdateTakeoffLanding(dt);
            if (m_animalAI.m_jumpInterval > 0.0) m_animalAI.m_jumpTimer += dt;
            if (m_animalAI.m_randomMoveUpdateTimer > 0.0) m_animalAI.m_randomMoveUpdateTimer -= dt;
            m_animalAI.UpdateRegeneration(dt);
            m_animalAI.m_timeSinceHurt += dt;
            // Then we re-write the MoveAwayAndDespawn() method, as we want to include the (m_spawnEffects)
            // If you decide to remove the (m_spawnEffects) then, you can simply use: 
            // m_animalAI.MoveAwayAndDespawn(dt, true);
            var prefabTransform = m_animalAI.transform;
            var position = prefabTransform.position;
            if (Player.GetClosestPlayer(m_animalAI.transform.position, 40f) is { } closestPlayer)
            {
                Vector3 normalized = (closestPlayer.transform.position - position).normalized;
                m_animalAI.MoveTo(dt, position - normalized * 5f, 0.0f, true);
            }
            else
            {
                m_spawnEffects.Create(position, prefabTransform.rotation, prefabTransform);
                m_animalAI.m_nview.Destroy();
            }

            // Force despawn if haldor is trapped
            if (TravelingHaldor.forceDespawn.Value)
            {
                m_despawnTimer += dt;
                if (m_despawnTimer > TravelingHaldor.eventDuration.Value)
                {
                    m_spawnEffects.Create(position, prefabTransform.rotation, prefabTransform);
                    m_animalAI.m_nview.Destroy();
                }
            }

            return false;
        }

        public List<TradeItem> GetConfigTradeItems()
        {
            List<TradeItem> availableItems = new();
            foreach (var itemConfig in TravelingHaldor.traderItemConfig.Value.Split(';'))
            {
                if (ValidateTradeItem(itemConfig) is not { } item) continue;
                if (!item.m_requiredGlobalKey.IsNullOrWhiteSpace() && !ZoneSystem.instance.GetGlobalKey(item.m_requiredGlobalKey)) continue;
                availableItems.Add(item);
            }

            return availableItems;
        }

        public static void UpdateSpawn()
        {
            if (!Player.m_localPlayer || !ZNet.m_instance || !ZoneSystem.m_instance) return;
            // Check if game has required global key
            if (!TravelingHaldor.globalKeyRequirement.Value.IsNullOrWhiteSpace() && !ZoneSystem.m_instance.GetGlobalKey(TravelingHaldor.globalKeyRequirement.Value)) return;

            // Check if max active haldors is reached
            if (m_instances.Count >= TravelingHaldor.maxHaldorCount.Value) return;

            // Check if time for to spawn a new traveling trader
            // If user just loaded game, the last event spawn == 0, so it will always try to spawn,
            // Might want to change this so the first spawn isn't as soon as user logs on.
            // Change m_lastEventSpawn to 1000.0, so the first spawn is always 1000 seconds after user logs on for example
            if (m_lastEventSpawn != 0.0 && ZNet.m_instance.GetTimeSeconds() - m_lastEventSpawn < TravelingHaldor.eventInterval.Value * 30 * 60) return;

            // Check if current biome is allowed
            if (TravelingHaldor.enableLocationVariability.Value)
            {
                var currentBiome = Player.m_localPlayer.GetCurrentBiome();
                if (!TravelingHaldor.allowedBiomes.Value.HasFlag(currentBiome)) return;
            }


            // Check time of day
            switch (TravelingHaldor.specificSpawnTime.Value)
            {
                case TravelingHaldor.SpawnTime.Day:
                    if (EnvMan.IsNight()) return;
                    break;
                case TravelingHaldor.SpawnTime.Night:
                    if (EnvMan.IsDay()) return;
                    break;
            }

            // Randomize chance to spawn
            if (Random.value > TravelingHaldor.eventChance.Value) return;

            m_lastEventSpawn = ZNet.m_instance.GetTimeSeconds();

            float randomDistance = Random.Range(15f, 30f);

            // I see you have a config for this, but do not use it.
            // randomDistance = TravelingHaldor.spawnDistance.Value;
            var pos = Player.m_localPlayer.transform.position + Random.insideUnitSphere * randomDistance;
            pos.y = ZoneSystem.m_instance.GetGroundHeight(pos);

            GameObject prefab = Instantiate(travelingHaldorPrefab, pos, Quaternion.identity);
            m_spawnEffects.Create(prefab.transform.position, prefab.transform.rotation, prefab.transform, 1f, 0);
            ZLog.Log("Spawned TravelingHaldor x1");
        }

        public TradeItem? ValidateTradeItem(string config)
        {
            var parts = config.Split(',');
            var prefabName = parts[0];
            var stack = int.TryParse(parts[1], out int s) ? s : 1;
            var price = int.TryParse(parts[2], out int p) ? p : 1;
            var key = parts.Length > 3 ? parts[3] : "";
            if (ObjectDB.m_instance.GetItemPrefab(prefabName) is not { } prefab || !prefab.TryGetComponent(out ItemDrop component))
            {
                return null;
            }

            return new TradeItem()
            {
                m_prefab = component,
                m_stack = stack,
                m_price = price,
                m_requiredGlobalKey = key
            };
        }

        [HarmonyPatch(typeof(Trader), nameof(Update))]
        private static class Trader_Update_Patch
        {
            private static bool Prefix(Trader __instance)
            {
                if (__instance is not TravelingTrader component) return true;
                component.Patch_Update();
                return false;
            }
        }

        [HarmonyPatch(typeof(AnimalAI), nameof(AnimalAI.UpdateAI))]
        private static class AnimalAI_UpdateAI_Patch
        {
            private static bool Prefix(AnimalAI __instance, float dt)
            {
                if (!__instance.TryGetComponent(out TravelingTrader component)) return true;
                return component.Patch_UpdateAI(dt);
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(GetAvailableItems))]
        private static class Trader_GetAvailableItems_Patch
        {
            private static void Postfix(Trader __instance, ref List<TradeItem> __result)
            {
                if (__instance is not TravelingTrader component) return;
                __result = component.GetConfigTradeItems();
            }
        }

        [HarmonyPatch(typeof(Character), nameof(Character.GetHoverText))]
        private static class Character_GetHoverText_Patch
        {
            private static void Postfix(Character __instance, ref string __result)
            {
                if (!__instance.TryGetComponent(out TravelingTrader component)) return;
                __result = component.GetHoverText();
            }
        }

        [HarmonyPatch(typeof(Trader), nameof(RandomTalk))]
        private static class Trader_RandomTalk_Patch
        {
            private static void Postfix(Trader __instance)
            {
                // Had to patch this because this method checks if the animator has a trigger key: "Stand"
                if (__instance is not TravelingTrader component) return;
                if (StoreGui.IsVisible() || !Player.IsPlayerInRange(__instance.transform.position, __instance.m_greetRange)) return;
                component.Say(component.m_randomTalk, "");
                component.m_randomTalkFX.Create(__instance.transform.position, Quaternion.identity);
            }
        }
    }
}