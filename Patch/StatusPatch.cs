using MegaCrit.Sts2.Core.Combat;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Commands;

using BaiMod.Function;

namespace BaiMod.Patch;




[HarmonyPatch(typeof(Hook), nameof(Hook.AfterPowerAmountChanged))]
public class Hook_AfterPowerAmountChanged_Patch
{
    [HarmonyPrefix]
    public static void PowerRecord(PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        Log.Info($"[BaiMod] PowerAmount = {power.Amount}, PowerID = {power.Id.Entry}");
        if (power == null) return;
        
        string powerId = power.Id.Entry; 
        
        
        if (powerId == "POISON_POWER" || powerId == "DEMISE_POWER" || powerId == "DOOM_POWER") 
        {
            
            //Log.Info($"[BaiMod] amount = {amount}");

            // 3. 安全獲取目標怪物實體
            Creature? target = power.Target ?? power.Owner;
            if (target == null) return;
            
            if (amount <= 0) 
            {
                // 檢查怪物血條上的毒是不是徹底歸零了
                if (power.Amount <= 0)
                {
                    // 呼叫你的重置方法，把這隻怪物的權重底池清空
                    DmgCount.ResetMonsterStatusWeight(target, powerId);
                }
                
                // 既然不是在上毒，後續的累加權重邏輯就不用走了，直接 return
                return;
            }

            // 4. 獲取施加者的玩家ID（如果有的話）
            ulong playerId = 0;
            if (applier != null)
            {
                // 如果直接是玩家
                if (applier.Player != null) 
                {
                    playerId = applier.Player.NetId;
                }
                // 或者是寵物/召喚物
                else if (applier.PetOwner != null)
                {
                    playerId = applier.PetOwner.NetId;
                }
            }


            if (playerId != 0)
            {
                DmgCount.RecordPowerApplied(target, powerId, playerId, (int)amount);
            }
        }
    }
}




[HarmonyPatch(typeof(PoisonPower), nameof(PoisonPower.AfterSideTurnStart))]
public static class PoisonTickPatch
{


    [HarmonyPrefix]

    public static void PoisonSnapShotPatch(ICombatState combatState)
    {
        if (combatState.CurrentSide == CombatSide.Enemy)
        {
            DmgCount.CapturePoisonSnapshots(combatState);
        }
    }
}

[HarmonyPatch(typeof(StranglePower), nameof(StranglePower.AfterCardPlayed))]
public static class StranglePower_AfterCardPlayed_Patch
{
    [HarmonyPrefix]
    public static void GetStrangleApplier(StranglePower __instance, CardPlay cardPlay)
    {
        // 1. 檢查這個狀態有沒有施加者（Player）
        if (__instance.Applier?.Player == null) return;

        // 2. 檢查目前打出的這張牌，它的主人是不是這個狀態的施加者
        // 如果是，代表等一下底層執行時，這個 StranglePower 就會跳傷害
        if (cardPlay.Card.Owner == __instance.Applier.Player)
        {
            // 鎖定這個施加者的 NetId
            DmgCount.CurrentStrangleApplierId = __instance.Applier.Player.NetId;
        }
    }

    [HarmonyPostfix]
    public static void ClearStrangleApplier()
    {
        // 傷害結算完了，把暫存變數清空，避免污染其他地方
        DmgCount.CurrentStrangleApplierId = null;
    }
}


[HarmonyPatch(typeof(DoomPower), nameof(DoomPower.DoomKill))]
public static class DoomPower_DoomKill_Patch
{
    [HarmonyPrefix]
    public static void DoomTrigger(IReadOnlyList<Creature> creatures)
    {
        if (creatures == null || creatures.Count == 0) return;

        Log.Info($"[BaiMod 災厄引爆] >>> 偵測到 DoomPower.DoomKill 觸發！即將引爆 {creatures.Count} 隻怪物 <<<");


        foreach (Creature creature in creatures)
        {
            if (creature == null || creature.IsPlayer) continue;

            // 1. 抓取引爆前的真實剩餘血量
            int monsterHp = creature.CurrentHp;

            
            DmgCount.DistributedDoomDamage(creature, monsterHp);
        }
    }
}

