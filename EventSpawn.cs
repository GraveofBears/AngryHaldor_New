using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using HarmonyLib;
using ServerSync;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TravelingHaldor
{
    public static class EventSpawn
    {
        private const float maxRadius = 9500f;
        private const float range = 3000f;

        private static readonly CustomSyncedValue<string> ServerLocationData = new(TravelingHaldor.configSync, "TravelingHaldorLocation", "");

        private static Vector3 m_spawnPosition = Vector3.zero;
        private static float m_updateTimer;
        private const float m_spawnRange = 50f;
        private static Minimap.PinData? m_pin;
        private static long m_lastPositionTime;

        public static void UpdateSpawn()
        {
            if (m_spawnPosition == Vector3.zero || !Player.m_localPlayer) return;
            m_updateTimer += Time.fixedDeltaTime;
            if (m_updateTimer < 1f) return;
            m_updateTimer = 0.0f;

            var playerPos = Player.m_localPlayer.transform.position;
            var distance = Utils.DistanceXZ(m_spawnPosition, playerPos);
            if (distance > m_spawnRange) return;

            var haldor = UnityEngine.Object.Instantiate(TravelingTrader.travelingHaldorPrefab, m_spawnPosition, Quaternion.identity);
            ZLog.Log("[TravelingHaldor]: Spawned TravelingHaldor x1");
            
            if (ZNet.m_instance.IsServer())
            {
                m_spawnPosition = Vector3.zero;
                ServerLocationData.Value = "";
                m_lastPositionTime = DateTime.Now.Ticks;
            }
            else
            {
                var server = ZNet.m_instance.GetServerPeer();
                server.m_rpc.Invoke(nameof(RPC_MessageServer), "Spawned Haldor!");
            }
        }

        [HarmonyPatch(typeof(ZNet), nameof(ZNet.OnNewConnection))]
        private static class ZNet_OnNewConnection_Patch
        {
            private static void Postfix(ZNetPeer peer)
            {
                peer.m_rpc.Register(nameof(RPC_MessageServer), new Action<ZRpc,string>(RPC_MessageServer));
            }
        }

        private static void RPC_MessageServer(ZRpc rpc, string text)
        {
            Debug.LogWarning(text);
            m_spawnPosition = Vector3.zero;
            ServerLocationData.Value = "";
            m_lastPositionTime = DateTime.Now.Ticks;
            UpdatePin();
        }

        public static void UpdateLocation()
        {
            if (!ZNet.m_instance || !ZNet.m_instance.IsServer()) return;
            if (m_lastPositionTime != 0L && DateTime.Now.Ticks < m_lastPositionTime + TimeSpan.FromMinutes(TravelingHaldor.eventInterval.Value * 30).Ticks) return;
            if (!FindSpawnLocation(out Vector3 pos)) return;

            ServerLocationData.Value = new SerializedVector(pos).ToString();
            m_spawnPosition = pos;
            m_lastPositionTime = DateTime.Now.Ticks;
            UpdatePin();
        }

        public static void SetupServerSync()
        {
            ServerLocationData.ValueChanged += () =>
            {
                if (!ZNet.m_instance || ZNet.m_instance.IsServer()) return;
                m_spawnPosition = ServerLocationData.Value.IsNullOrWhiteSpace() ? Vector3.zero : new SerializedVector(ServerLocationData.Value).position;
                UpdatePin();
            };
        }

        private static void UpdatePin()
        {
            if (m_spawnPosition == Vector3.zero)
            {
                if (m_pin != null) Minimap.m_instance.RemovePin(m_pin);
            }
            else
            {
                if (m_pin != null) Minimap.m_instance.RemovePin(m_pin);
                m_pin = Minimap.m_instance.AddPin(m_spawnPosition, Minimap.PinType.Hildir1, "Traveling Haldor", false, false);
            }
        }

        private class SerializedVector
        {
            public readonly Vector3 position;

            public SerializedVector(Vector3 pos) => position = pos;

            public SerializedVector(string pos)
            {
                var parts = pos.Split('@');
                if (parts.Length < 3)
                {
                    position = Vector3.zero;
                    return;
                }
                position = new Vector3(
                    float.TryParse(parts[0], out float x) ? x : 0f,
                    float.TryParse(parts[0], out float y) ? y : 0f, 
                    float.TryParse(parts[0], out float z) ? z : 0f);
            }

            public override string ToString() => $"{position.x}@{position.y}@{position.z}";
        }

        private static Vector3 GetRandomPlayerPosition()
        {
            if (!ZNet.m_instance) return Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero;
            List<ZNetPeer> validPeers = ZNet.m_instance.GetPeers().Where(peer => peer.m_refPos != Vector3.zero).ToList();
            if (validPeers.Count <= 0) return Player.m_localPlayer ? Player.m_localPlayer.transform.position : Vector3.zero;
            ZNetPeer randomPeer = validPeers[Random.Range(0, validPeers.Count)];
            return randomPeer.m_refPos;
        }

        private static bool FindSpawnLocation(out Vector3 pos)
        {
            pos = Vector3.zero;

            var randomPos = GetRandomPlayerPosition();
            if (randomPos != Vector3.zero)
            {
                // Try get location within margin
                for (int index = 0; index < 1000; ++index)
                {
                    Vector3 vector3 = GetRandomVectorWithin(randomPos, range);
                    if (!TravelingHaldor.allowedBiomes.Value.HasFlag(WorldGenerator.instance.GetBiome(vector3))) continue;
                    if (WorldGenerator.instance.GetBiomeArea(vector3) is not Heightmap.BiomeArea.Median) continue;
                    pos = vector3;
                    return true;
                }
            }
            // Else try get location entire world
            for (int index = 0; index < 1000; ++index)
            {
                Vector3 vector3 = GetRandomVector();

                if (!TravelingHaldor.allowedBiomes.Value.HasFlag(WorldGenerator.instance.GetBiome(vector3))) continue;
                if (WorldGenerator.instance.GetBiomeArea(vector3) is not Heightmap.BiomeArea.Median) continue;
                pos = vector3;
                return true;
            }
            return false;
        }
        
        private static Vector3 GetRandomVectorWithin(Vector3 point, float margin)
        {
            Vector2 vector2 = Random.insideUnitCircle * margin;
            return point + new Vector3(vector2.x, 0.0f, vector2.y);
        }
        
        private static Vector3 GetRandomVector()
        {
            float x = Random.Range(-maxRadius, maxRadius);
            float y = Random.Range(0f, 5000f);
            float z = Random.Range(-maxRadius, maxRadius);
            return new Vector3(x, y, z);
        }
    }
}