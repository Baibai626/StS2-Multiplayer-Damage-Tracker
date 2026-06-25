using MegaCrit.Sts2.Core.Combat;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes;

using BaiMod.Function;

namespace BaiMod.Patch;


[HarmonyPatch(typeof(Hook), nameof(Hook.AfterDamageGiven))]

public static class DamagePatch
{
    [HarmonyPostfix]

    public static void DamageCountPatch(ICombatState combatState, Creature dealer, Creature target, DamageResult results, ValueProp props, CardModel? cardSource)
    {
        DmgCount.DamageTracker(combatState, dealer, results, props, target, cardSource);

        ModUI.UpdateDisplay();
    }

}

[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeCombatStart))]
public static class CombatStartPatch
{
    [HarmonyPrefix]
    public static void OnCombatStart()
    {
        DmgCount.BeforeCombat();

        ModUI.CreateLayout();
    }
}


[HarmonyPatch(typeof(Hook), nameof(Hook.AfterCombatVictory))]
public static class CombatVictoryPatch
{
    [HarmonyPostfix]
    public static void OnCombatVictory()
    {
        DmgCount.AfterCombatVictory();
    }
}


[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentRun))]
public static class DeleteCurrentRunPatch
{
    [HarmonyPrefix]
    public static void OnDeleteCurrentRun()
    {
        DmgCount.ResetDamage();
        Log.Info($"[BaiMod] 刪除存檔: damage reset");

        ModUI.DestroyLayout();
    }

}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentMultiplayerRun))]
public static class DeleteCurrentMultiplayerRunPatch
{
    [HarmonyPrefix]
    public static void OnDeleteCurrentMultiplayerRun()
    {
        DmgCount.ResetDamage();
        Log.Info($"[BaiMod] 刪除存檔: damage reset");

        ModUI.DestroyLayout();
    }

}




[HarmonyPatch(typeof(NGame), nameof(NGame.ReturnToMainMenu))]
public static class NGameReturnToMainMenuPatch
{
    // 使用 Prefix，在準備退出的第一時間就把 UI 銷毀
    [HarmonyPrefix]
    public static void Prefix()
    {
        try
        {
            ModUI.DestroyLayout();
            MegaCrit.Sts2.Core.Logging.Log.Info("[BaiMod Patch] 偵測到返回主選單，已成功銷毀 UI 看板。");
        }
        catch (System.Exception ex)
        {
            MegaCrit.Sts2.Core.Logging.Log.Error($"[BaiMod Patch] 銷毀 UI 時發生錯誤: {ex.Message}");
        }
    }
}

/*
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
public static class SaveRunPatch
{
    [HarmonyPrefix]
    public static void BeforeSaveRun()
    {
        Log.Info("[BaiMod] SaveRun called");
    }
}
*/
