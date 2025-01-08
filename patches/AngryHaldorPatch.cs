using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine;

namespace AngryHaldorPatches
{
    [HarmonyPatch(typeof(MonsterAI), nameof(MonsterAI.UpdateAI))]
    public class AllowTamedInNoMonsterArea
    {
        private static readonly MethodInfo TraderObjectCheckMethod = AccessTools.DeclaredMethod(typeof(AllowTamedInNoMonsterArea), nameof(TraderObjectCheck));
        private static readonly MethodInfo IsPointInsideAreaMethod = AccessTools.DeclaredMethod(typeof(EffectArea), nameof(EffectArea.IsPointInsideArea));

        private static EffectArea? TraderObjectCheck(EffectArea? targetEffectArea, MonsterAI monsterAI)
        {
            if (targetEffectArea == null) return targetEffectArea;

            // Check if the EffectArea is part of the Vendor_BlackForest
            if (Utils.GetPrefabName(targetEffectArea.transform.root.gameObject.name) != "Vendor_BlackForest")
            {
                return targetEffectArea;
            }

            // Allow AngryHaldor and AngryHalstein to ignore NoMonsterArea
            string prefabName = Utils.GetPrefabName(monsterAI.gameObject.name);
            return prefabName is "AngryHalstein" or "AngryHaldor" ? null : targetEffectArea;
        }

        [UsedImplicitly]
        private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
        {
            List<CodeInstruction> instrs = instructions.ToList();
            for (int i = 0; i < instrs.Count; ++i)
            {
                yield return instrs[i];

                // Look for the call to EffectArea.IsPointInsideArea and check for NoMonsters
                if (instrs[i].opcode == OpCodes.Call && instrs[i].OperandIs(IsPointInsideAreaMethod) &&
                    instrs[i - 2].opcode == OpCodes.Ldc_I4_S && instrs[i - 2].OperandIs((int)EffectArea.Type.NoMonsters))
                {
                    // Inject a call to TraderObjectCheck after the area check
                    yield return new CodeInstruction(OpCodes.Ldarg_0); // Load 'this' (MonsterAI)
                    yield return new CodeInstruction(OpCodes.Call, TraderObjectCheckMethod);
                }
            }
        }
    }
}
