using HarmonyLib;
using UnityEngine;

namespace TravelingHaldorMod
{
    [HarmonyPatch(typeof(Character), "Awake")]
    public static class CharacterAwakePatch
    {
        static void Postfix(Character __instance)
        {
            // Check if this is the specific character you want to modify
            if (__instance.name == "TravelingHaldor")
            {
                // Add HoverText component if it doesn't already exist
                var hoverText = __instance.GetComponent<HoverText>();
                if (hoverText == null)
                {
                    hoverText = __instance.gameObject.AddComponent<HoverText>();
                    hoverText.m_text = "Open Trade";
                }

                // Add any other necessary components or initialization here
                var trader = __instance.GetComponent<Trader>();
                if (trader == null)
                {
                    trader = __instance.gameObject.AddComponent<Trader>();
                }
            }
        }
    }
}
