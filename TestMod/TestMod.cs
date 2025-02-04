using BepInEx;
using HarmonyLib;
using UnityEngine;

namespace TestMod
{
    [BepInPlugin("DeathMonger.ValheimMod", "Test Mod", "1.0.0")]
    [BepInProcess("valheim.exe")]
    public class TestMod : BaseUnityPlugin
    {
        private readonly Harmony harmony = new Harmony("DeathMonger.ValheimMod");

        void Awake()
        {
            harmony.PatchAll();
        }

        [HarmonyPatch(typeof(Character), nameof(Character.Jump))]
        class Jump_Patch
        {
            static void Prefix(ref float ___m_jumpForce)
            {
                Debug.Log($"Jump force: {___m_jumpForce}");
                ___m_jumpForce = 15;
                Debug.Log($"Modified jump force: {___m_jumpForce}");
            }
        }
    }
}
