using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Trader), nameof(Trader.Update))]
public class TraderUpdatePatch
{
    static bool Prefix(Trader __instance)
    {
        // Suppress all animation logic in Update
        Player closestPlayer = Player.GetClosestPlayer(__instance.transform.position, Mathf.Max(__instance.m_byeRange + 3f, __instance.m_standRange));
        if (closestPlayer == null) return false;

        float distance = Vector3.Distance(closestPlayer.transform.position, __instance.transform.position);
        if (distance < __instance.m_greetRange && !__instance.m_didGreet)
        {
            __instance.m_didGreet = true;
            Debug.Log($"Greets count: {__instance.m_randomGreets.Count}");
            if (__instance.m_randomGreets.Count > 0)
            {
                __instance.Say(__instance.CheckConditionals(__instance.m_randomGreets, true), "");  // Skip animation trigger
                __instance.m_randomGreetFX.Create(__instance.transform.position, Quaternion.identity);
            }
        }

        if (__instance.m_didGreet && distance > __instance.m_byeRange && !__instance.m_didGoodbye)
        {
            __instance.m_didGoodbye = true;
            Debug.Log($"Goodbye count: {__instance.m_randomGoodbye.Count}");
            if (__instance.m_randomGoodbye.Count > 0)
            {
                __instance.Say(__instance.m_randomGoodbye, "");  // Skip animation trigger
                __instance.m_randomGoodbyeFX.Create(__instance.transform.position, Quaternion.identity);
            }
        }

        return false;  // Skip original Update method
    }
}
