using HarmonyLib;
using System.Linq;
using UnityEngine;

namespace TravelingHaldorMod
{
    [HarmonyPatch(typeof(Character), "Awake")]
    public static class CharacterAwakePatch
    {
        static void Postfix(Character __instance)
        {
            if (__instance.name == "TravelingHaldor")
            {
                // Ensure HoverText component is attached
                var hoverText = __instance.GetComponent<HoverText>();
                if (hoverText == null)
                {
                    hoverText = __instance.gameObject.AddComponent<HoverText>();
                    hoverText.m_text = "Open Trade";
                }

                // Adjust HoverText height
                hoverText.transform.position += new Vector3(0, 7.0f, 0); // Adjust Y value as needed

                // Ensure Trader component is attached and configured
                var trader = __instance.GetComponent<Trader>();
                if (trader == null)
                {
                    trader = __instance.gameObject.AddComponent<Trader>();
                }

                ConfigureTrader(trader);
            }
        }

        private static void ConfigureTrader(Trader trader)
        {
            // Add animator if missing
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

            // Add LookAt component if missing
            if (trader.m_lookAt == null)
            {
                trader.m_lookAt = trader.GetComponentInChildren<LookAt>() ?? trader.gameObject.AddComponent<LookAt>();
            }

            // Additional configuration code from ConfigureTrader method in TravelingHaldor class...
        }
    }
}
