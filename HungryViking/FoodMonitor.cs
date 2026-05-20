using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace HungryViking
{
    // Polls Player.m_foods each tick and fires events when food approaches or hits expiry.
    // OnWarning fires once per slot per expiry cycle when it crosses below HungerThreshold.
    // OnStarving fires when a slot disappears (expired or missing on load).
    public class FoodMonitor
    {
        private readonly HungryVikingMod _mod;

        public event Action OnWarning;   // any slot crossed below HungerThreshold
        public event Action OnStarving;  // a slot fully expired

        private class SlotState
        {
            public bool WarningFired;
        }

        public static readonly AccessTools.FieldRef<Player, List<Player.Food>> FoodsRef =
            AccessTools.FieldRefAccess<Player, List<Player.Food>>("m_foods");

        // Number of active food slots this tick (0–3).
        public int CurrentFoodCount { get; private set; }

        // 0 = all food above HungerThreshold.
        // Ramps 0→1 as the hungriest active slot ticks from HungerThreshold down to 0s.
        // Does not account for empty slots — the mod adds that separately.
        public float WorstSlotUrgency { get; private set; }

        private readonly Dictionary<string, SlotState> _states   = new Dictionary<string, SlotState>();
        private readonly HashSet<string>               _prevNames = new HashSet<string>();
        private int _prevFoodCount = -1;

        public FoodMonitor(HungryVikingMod mod) => _mod = mod;

        // --- Static helpers for console commands ---
        // Player.Food references live here so command lambdas never touch the type directly,
        // avoiding a JIT TypeLoadException when Terminal compiles the lambda bodies.

        public static void ClearFoods(Player player)
        {
            FoodsRef(player).Clear();
        }

        public static string DrainFood(Player player, int zeroBasedIdx, float seconds)
        {
            var foods = FoodsRef(player);
            if (zeroBasedIdx >= foods.Count) return $"Slot {zeroBasedIdx + 1} is empty.";
            var food = foods[zeroBasedIdx];
            food.m_time = Mathf.Max(0f, food.m_time - seconds);
            return $"Slot {zeroBasedIdx + 1} ({food.m_item?.m_shared?.m_name ?? "?"}): {food.m_time:F0}s remaining.";
        }

        public static string DrainAllFoods(Player player, float seconds)
        {
            var foods = FoodsRef(player);
            foreach (var food in foods)
                food.m_time = Mathf.Max(0f, food.m_time - seconds);
            return $"Drained {seconds}s from {foods.Count} slot(s).";
        }

        public static string SetFoodTime(Player player, int zeroBasedIdx, float seconds)
        {
            var foods = FoodsRef(player);
            if (zeroBasedIdx >= foods.Count) return $"Slot {zeroBasedIdx + 1} is empty.";
            var food = foods[zeroBasedIdx];
            food.m_time = Mathf.Max(0f, seconds);
            return $"Slot {zeroBasedIdx + 1} ({food.m_item?.m_shared?.m_name ?? "?"}): set to {food.m_time:F0}s.";
        }

        public static string GetFoodStatus(Player player)
        {
            var foods = FoodsRef(player);
            if (foods.Count == 0) return "No active food buffs.";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < foods.Count; i++)
            {
                var food = foods[i];
                sb.AppendLine($"  Slot {i + 1}: {food.m_item?.m_shared?.m_name ?? "unknown"}  {food.m_time:F0}s remaining");
            }
            return sb.ToString().TrimEnd();
        }

        public void Tick(float dt)
        {
            var foods = FoodsRef(Player.m_localPlayer);

            if (CurrentFoodCount == 0 && foods.Count > 0)
                HungryVikingMod.Log.LogInfo($"HungryViking: tracking {foods.Count} food slot(s)");

            CurrentFoodCount = foods.Count;

            var currentNames = new HashSet<string>();
            foreach (var food in foods)
            {
                if (food?.m_item?.m_shared == null) continue;
                currentNames.Add(food.m_item.m_shared.m_name);
            }

            // Newly eaten food: reset per-slot warning so it can fire again later.
            foreach (var name in currentNames)
            {
                if (!_prevNames.Contains(name))
                    _states.Remove(name);
            }

            // Starving: first tick with missing slots, or a slot disappears mid-session.
            bool firstTick   = _prevFoodCount == -1;
            bool slotExpired = CurrentFoodCount < _prevFoodCount;
            if ((firstTick || slotExpired) && CurrentFoodCount < 3)
                OnStarving?.Invoke();

            // Per-slot urgency + one-shot warning event.
            float worstUrgency = 0f;
            float threshold    = _mod.HungerThreshold.Value;
            foreach (var food in foods)
            {
                if (food?.m_item?.m_shared == null) continue;
                var name = food.m_item.m_shared.m_name;

                if (!_states.TryGetValue(name, out var state))
                {
                    state = new SlotState();
                    _states[name] = state;
                }

                var t = food.m_time;

                if (t < threshold)
                {
                    worstUrgency = Mathf.Max(worstUrgency, 1f - (t / threshold));

                    if (!state.WarningFired)
                    {
                        state.WarningFired = true;
                        OnWarning?.Invoke();
                    }
                }
                else
                {
                    state.WarningFired = false;
                }
            }
            WorstSlotUrgency = worstUrgency;

            _prevFoodCount = CurrentFoodCount;
            _prevNames.Clear();
            foreach (var n in currentNames)
                _prevNames.Add(n);
        }
    }
}
