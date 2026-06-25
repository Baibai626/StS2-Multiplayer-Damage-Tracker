using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

//using BaiMod.Patch;
//using STS2RitsuLib.Scaffolding.Characters.Patches;

namespace BaiMod.Function;

public static class DmgCount
{
    // ========================================================
    // 1. 資料結構與全域欄位
    // ========================================================

    public class PlayerDamageData
    {
        public int CombatDamage { get; set; }
        public int TotalDamage { get; set; }
        public int CheckpointDamage { get; set; }
    }

    // 用來記錄目前「正在觸發緊勒傷害」的玩家 ID
    public static ulong? CurrentStrangleApplierId = null;

    //玩家ID -> 傷害數據
    private static Dictionary<ulong, PlayerDamageData> _PlayerDamageMap = new(); 
    
    // 結構：怪物實體 -> 狀態名稱("POISON_POWER"或"DEMISE_POWER") -> 玩家ID -> 施加總層數
    private static Dictionary<Creature, Dictionary<string, Dictionary<ulong, int>>> _monsterStatusTracker = new();
    
    // 紀錄每隻怪物這回合「還剩下幾次毒傷要跳」的快照本子
    private static Dictionary<Creature, int> _pendingPoisonHitCounts = new();


    // ========================================================
    // 2. 遊戲生命週期 Hook 呼叫點
    // ========================================================




    /// <summary>
    /// 在每次戰鬥開始前呼叫：重置本場傷害、同步快照、清空狀態帳本與毒快照
    /// </summary>
    public static void BeforeCombat()
    {
        foreach (var kv in _PlayerDamageMap)
        {
            kv.Value.TotalDamage = kv.Value.CheckpointDamage;
            kv.Value.CombatDamage = 0;
        }

        _monsterStatusTracker.Clear();
        _pendingPoisonHitCounts.Clear(); // 這裡順手幫你加上了，讓新戰鬥徹底乾淨
        Log.Info("[BaiMod] 已清空上一場戰鬥的怪物狀態貢獻帳本與毒快照。");

        
    }

    /// <summary>
    /// 戰鬥勝利時呼叫：保存當前總傷害快照
    /// </summary>
    public static void AfterCombatVictory()
    {
        foreach (var kv in _PlayerDamageMap)
        {
            kv.Value.CheckpointDamage = kv.Value.TotalDamage;
            Log.Info($"[BaiMod] Player {kv.Key} 本場造成傷害 = {kv.Value.CombatDamage}");
        }
    }

    /// <summary>
    /// 刪除存檔時呼叫：將所有玩家的所有傷害數據徹底重置為 0
    /// </summary>
    public static void ResetDamage()
    {
        foreach (var kv in _PlayerDamageMap)
        {
            Log.Info($"[BaiMod] Player {kv.Key} 本局總傷害 = {kv.Value.TotalDamage}");
        }

        foreach (var kv in _PlayerDamageMap)
        {
            kv.Value.TotalDamage = 0;
            kv.Value.CombatDamage = 0;
            kv.Value.CheckpointDamage = 0;
        }
    }


    // ========================================================
    // 3. 傷害核心計算與分流
    // ========================================================

    /// <summary>
    /// 傷害統計進入點（Debug 用）：觸發核心計算並印出當前傷害日誌
    /// </summary>
    public static void DamageTracker(ICombatState combatState, Creature? dealer, DamageResult results, ValueProp props, Creature target, CardModel? cardSource)
    {
        DamageCalculate(combatState, dealer, results, props, target, cardSource);
        ModUI.UpdateDisplay();

        foreach (var kv in _PlayerDamageMap)
        {
            Log.Info($"[BaiMod] Player {kv.Key} 本場造成傷害 = {kv.Value.CombatDamage}");
            Log.Info($"[BaiMod] Player {kv.Key} 總傷害 = {kv.Value.TotalDamage}");
        }
    }

    /// <summary>
    /// 核心分流邏輯：判斷是直傷還是回合結束的狀態結算傷害
    /// </summary>
    public static void DamageCalculate(ICombatState combatState, Creature? dealer, DamageResult results, ValueProp props, Creature target, CardModel? cardSource)
    {
        if (target.IsPlayer) return; // 目前只統計對怪物的傷害，對玩家的傷害不計入內
        if (dealer != null && dealer.IsEnemy) return; // 如果造成傷害的是敵人，直接不統計

        int _totalDamage = results.TotalDamage;
        if (_totalDamage <= 0) return;

        // 狀況一：dealer 不為 null 且不是敵人（處理Direct Damage）
        if (dealer != null && dealer.IsEnemy == false)
        {
            Log.Info($"[BaiMod] 直傷來源 dealer= {dealer}");
            var player = ResolveOwner(dealer);
            if (player == null) return;
            
            ulong playerId = player.NetId;
            var PData = GetOrCreatePlayerData(playerId);
            PData.CombatDamage += _totalDamage;
            PData.TotalDamage += _totalDamage;
            return;
        }

        // 狀況二：dealer 為 null 且目前是敵人的回合（處理Poison_Power, Demise_Power）
        if (dealer == null && combatState.CurrentSide == CombatSide.Enemy)
        {
            // 篩選出狀態型傷害特徵（無視格擋、無視威力加成、無卡牌來源）
            if (cardSource == null && props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered))
            {
                // 【時序判定】使用快照扣除制
                if (_pendingPoisonHitCounts.TryGetValue(target, out int remainingTicks) && remainingTicks > 0)
                {
                    // 還有毒的預期次數，判定為毒
                    _pendingPoisonHitCounts[target] = remainingTicks - 1;
                    Log.Info($"[BaiMod 分流] 依快照剩餘次數（剩餘 {remainingTicks} 次）判定為【毒傷害】");
                    DistributeStatusDamage(target, "POISON_POWER", _totalDamage);
                }
                else
                {
                    // 次數扣光了或無紀錄，後面排隊進來的一律是消亡
                    Log.Info($"[BaiMod 分流] 毒次數已扣光或無紀錄，判定為【消亡傷害】");
                    DistributeStatusDamage(target, "DEMISE_POWER", _totalDamage);
                }
                return;
            }
        }

        // 狀況三：dealer 為 null 且目前是玩家的回合（處理Strangle_Power）
        if (dealer == null && combatState.CurrentSide == CombatSide.Player && CurrentStrangleApplierId.HasValue)
        {
            // 嚴謹過濾：無卡牌來源，且具不吃格擋與加成的狀態傷害特徵
            if (cardSource == null && props.HasFlag(ValueProp.Unblockable) && props.HasFlag(ValueProp.Unpowered))
            {
                ulong applierId = CurrentStrangleApplierId.Value;
                Log.Info($"[BaiMod 分流] 偵測到緊勒傷害，成功追溯施加者玩家 ID: {applierId}");

                var PData = GetOrCreatePlayerData(applierId);
                PData.CombatDamage += _totalDamage;
                PData.TotalDamage += _totalDamage;
                return; 
            }
        }
                
        //fliter後無法辨別的傷害來源, 用於確認是否有應該計算卻還未注意的傷害
        Log.Info($"[BaiMod]Warning: Can't Defect Damage Source! ");
        Log.Info($"[BaiMod]未知傷害數據 dealer={dealer}, target={target}, damage={results.TotalDamage}, props={props}, cardSource={cardSource}");
    }

    /// <summary>
    /// 敵方回合開始時呼叫：建立全場活著怪物的「預期毒發傷害次數」快照（含加速劑計算）
    /// </summary>
    public static void CapturePoisonSnapshots(ICombatState combatState)
    {
        _pendingPoisonHitCounts.Clear();

        foreach (var enemy in combatState.Enemies.Where(e => e.IsAlive))
        {
            var poisonPower = enemy.GetPower<PoisonPower>();
            if (poisonPower != null && poisonPower.Amount > 0)
            {
                // 累加場上所有活著的玩家身上的「加速劑（Accelerant）」層數總和
                int accelerantCount = combatState.GetOpponentsOf(enemy)
                    .Where(c => c.IsAlive)
                    .Sum(c => c.GetPowerAmount<AccelerantPower>());

                // 實際會跳毒的次數 = 基礎 1 次 + 加速劑層數，且不能超過毒素本身層數
                int totalPoisonTicks = Math.Min((int)poisonPower.Amount, 1 + accelerantCount);
                
                _pendingPoisonHitCounts[enemy] = totalPoisonTicks;
                Log.Info($"[BaiMod 快照] 建立 {enemy.Name} 毒快照：本回合預期跳毒 {totalPoisonTicks} 次。");
            }
        }
    }


    // ========================================================
    // 4. 狀態帳本記帳與比例分配（工具型私有方法）
    // ========================================================

    /// <summary>
    /// 當玩家施加狀態時呼叫：累加該玩家對該怪物的狀態貢獻層數
    /// </summary>
    public static void RecordPowerApplied(Creature target, string powerId, ulong playerId, int amount)
    {
        if (!_monsterStatusTracker.ContainsKey(target))
        {
            _monsterStatusTracker[target] = new Dictionary<string, Dictionary<ulong, int>>(StringComparer.Ordinal);
        }

        if (!_monsterStatusTracker[target].ContainsKey(powerId))
        {
            _monsterStatusTracker[target][powerId] = new Dictionary<ulong, int>();
        }

        if (!_monsterStatusTracker[target][powerId].ContainsKey(playerId))
        {
            _monsterStatusTracker[target][powerId][playerId] = 0;
        }

        _monsterStatusTracker[target][powerId][playerId] += amount;
        Log.Info($"[BaiMod 記帳] 玩家 {playerId} 對 {target.Name} 施加了 {amount} 層 {powerId}。該玩家目前累計貢獻: {_monsterStatusTracker[target][powerId][playerId]} 層");
    }

    /// <summary>
    /// 當怪物身上的狀態歸零時呼叫：清空該狀態的玩家貢獻權重
    /// </summary>
    public static void ResetMonsterStatusWeight(Creature target, string powerId)
    {
        if (_monsterStatusTracker.ContainsKey(target) && _monsterStatusTracker[target].ContainsKey(powerId))
        {
            _monsterStatusTracker[target][powerId].Clear();
            Log.Info($"[BaiMod 帳本] 怪物 {target.Name} 的 {powerId} 已歸零，成功重置所有玩家的權重。");
        }
    }

    /// <summary>
    /// 根據狀態帳本中的施加比例，動態拆分狀態傷害並累加給對應玩家（處理四捨五入尾差）
    /// </summary>
    private static void DistributeStatusDamage(Creature target, string powerId, int totalDamage)
    {
        if (!_monsterStatusTracker.ContainsKey(target) || !_monsterStatusTracker[target].ContainsKey(powerId))
        {
            Log.Warn($"[BaiMod] 怪物 {target.Name} 受到 {powerId} 傷害 但帳本中沒有玩家的施加記錄！");
            return;
        }

        var playerStatusContributions = _monsterStatusTracker[target][powerId];
        
        int totalContributedStacks = 0;
        foreach (var kv in playerStatusContributions)
        {
            totalContributedStacks += kv.Value;
        }

        if (totalContributedStacks <= 0)
        {
            Log.Warn($"[BaiMod] 帳本中 {powerId} 總貢獻層數為 0  無法計算權重。");
            return;
        }

        int distributedSum = 0;
        List<(ulong PlayerId, int AllocatedDamage)> allocations = new();

        foreach (var kv in playerStatusContributions)
        {
            ulong playerId = kv.Key;
            int myStacks = kv.Value;

            int allocated = (int)Math.Round((double)myStacks / totalContributedStacks * totalDamage);
            allocations.Add((playerId, allocated));
            distributedSum += allocated;
        }

        int residual = totalDamage - distributedSum;
        if (residual > 0 && allocations.Count > 0)
        {
            var first = allocations[0];
            allocations[0] = (first.PlayerId, first.AllocatedDamage + residual);
        }
        else if (residual < 0 && allocations.Count > 0)
        {
            for (int i = 0; i < allocations.Count; i++)
            {
                var current = allocations[i];
                if (current.AllocatedDamage + residual >= 0)
                {
                    allocations[i] = (current.PlayerId, current.AllocatedDamage + residual);
                    residual = 0;
                    break;
                }
                else
                {
                    residual = current.AllocatedDamage + residual;
                    allocations[i] = (current.PlayerId, 0);
                }
            }
        }

        foreach (var alloc in allocations)
        {
            if (alloc.AllocatedDamage <= 0) continue;
            
            var PData = GetOrCreatePlayerData(alloc.PlayerId);
            PData.CombatDamage += alloc.AllocatedDamage;
            PData.TotalDamage += alloc.AllocatedDamage;
            
            Log.Info($"[BaiMod 傷害分配] 玩家 {alloc.PlayerId} 獲得 {powerId} 拆分傷害: {alloc.AllocatedDamage} 點。");
        }

        
        ModUI.UpdateDisplay();
    }

    public static void DistributedDoomDamage(Creature target, int currentHp)
    {
        if (target == null || currentHp <= 0) return;
        string powerId = "DOOM_POWER";

        // 1. 檢查帳本裡有沒有這隻怪物的災厄施加紀錄
        if (_monsterStatusTracker.ContainsKey(target) && 
            _monsterStatusTracker[target].ContainsKey(powerId) && 
            _monsterStatusTracker[target][powerId].Count > 0)
        {
            var playerDoomContributions = _monsterStatusTracker[target][powerId];

            int totalWeight = 0;
            foreach (var kv in playerDoomContributions)
            {
                totalWeight += kv.Value;
            }

            if (totalWeight > 0)
            {
                Log.Info($"[BaiMod 災厄清算] 開始分配 {target.Name} 的災厄傷害（血量: {currentHp}）");

                foreach (var kv in playerDoomContributions)
                {
                    ulong pId = kv.Key;
                    int weight = kv.Value;

                    // 計算分配傷害
                    int finalDmg = (int)Math.Round((double)currentHp * weight / totalWeight);

                    if (finalDmg > 0)
                    {
                        Log.Info($"[BaiMod 災厄清算] 玩家 {pId} 分配到 {finalDmg} 點傷害。");
                        var PData = GetOrCreatePlayerData(pId);
                        PData.CombatDamage += finalDmg;
                        PData.TotalDamage += finalDmg;
                    }
                }

                // 2. 清除該怪紀錄
                ModUI.UpdateDisplay();

                playerDoomContributions.Clear();
                return;
            }
        }    
    }

    /// <summary>
    /// 獲取或建立玩家傷害資料的輔助方法
    /// </summary>
    private static PlayerDamageData GetOrCreatePlayerData(ulong playerId)
    {
        if (!_PlayerDamageMap.TryGetValue(playerId, out var playerData))
        {
            playerData = new PlayerDamageData();
            _PlayerDamageMap[playerId] = playerData;
        }
        return playerData;
    }

    /// <summary>
    /// 根據傷害來源解析出玩家實體的輔助方法
    /// </summary>
    private static Player? ResolveOwner(Creature dealer)
    {
        if (dealer.Player != null) return dealer.Player;

        if (dealer.PetOwner != null)
        {
            Log.Info($"[BaiMod] dealer {dealer} is a pet, owner={dealer.PetOwner}");
            return dealer.PetOwner;
        }
        return null;
    }

    /// <summary>
    /// 提供給 UI 讀取當前所有玩家傷害數據的公開接口
    /// </summary>
    public static Dictionary<ulong, PlayerDamageData> GetAllPlayerData()
    {
        return _PlayerDamageMap ?? new Dictionary<ulong, PlayerDamageData>();
    }
}