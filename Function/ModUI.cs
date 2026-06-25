using Godot;
using MegaCrit.Sts2.Core.Combat;
using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Platform;
using MegaCrit.Sts2.Core.Runs;


namespace BaiMod.Function;

public static class ModUI
{
    private static CanvasLayer? _canvasLayer;
    private static VBoxContainer? _vboxContainer;
    private static Label? _titleLabel;
    private static Dictionary<ulong, Label> _playerLabels = new();

    /// <summary>
    /// 初始化並建立戰鬥中即時看板
    /// </summary>
    public static void CreateLayout()
    {
        DestroyLayout();

        // 1. 建立最頂層畫布
        _canvasLayer = new CanvasLayer();
        _canvasLayer.Layer = 100; 

        // 2. 建立邊界容器
        var marginContainer = new MarginContainer();
        marginContainer.SetAnchorsPreset(Control.LayoutPreset.TopRight); 
        marginContainer.GrowHorizontal = Control.GrowDirection.Both;
        marginContainer.GrowVertical = Control.GrowDirection.Both;
        
        marginContainer.AddThemeConstantOverride("margin_top", 400);   
        marginContainer.AddThemeConstantOverride("margin_right", 250);  

        // 關鍵設定：讓外層容器不阻擋滑鼠
        marginContainer.MouseFilter = Control.MouseFilterEnum.Ignore;

        // 3. 建立垂直排列容器
        _vboxContainer = new VBoxContainer();
        _vboxContainer.AddThemeConstantOverride("separation", 5); 

        // 關鍵設定：讓內層容器不阻擋滑鼠
        _vboxContainer.MouseFilter = Control.MouseFilterEnum.Ignore;

        marginContainer.AddChild(_vboxContainer);
        _canvasLayer.AddChild(marginContainer);

        // 4. 建立標題
        _titleLabel = new Label();
        _titleLabel.Text = "=== 戰鬥傷害統計 ===";
        _titleLabel.AddThemeColorOverride("font_color", new Color(1, 0.84f, 0)); 
        _vboxContainer.AddChild(_titleLabel);

        // 5. 掛載到當前場景
        var root = (SceneTree)Engine.GetMainLoop();
        root.CurrentScene.AddChild(_canvasLayer);

        

        _playerLabels.Clear();
        UpdateDisplay();
    }

    /// <summary>
    /// 刷新傷害文字面版
    /// </summary>
    public static void UpdateDisplay()
    {
        if (_vboxContainer == null) return;

        // 從你的 DmgCount 獲取當前所有玩家的數據
        // 假設 DmgCount 有暴露一個可以拿到所有玩家資料的方法或 Dictionary
        var allPlayers = DmgCount.GetAllPlayerData(); 
        if (allPlayers == null || allPlayers.Count == 0) return;

        // 計算全場總傷害，用來算百分比
        long totalCombatDmg = allPlayers.Values.Sum(p => p.CombatDamage);
        if (totalCombatDmg <= 0) totalCombatDmg = 1; // 防止除以 0

        foreach (var kvp in allPlayers)
        {
            ulong pId = kvp.Key;
            var pData = kvp.Value;
            double pct = ((double)pData.CombatDamage / totalCombatDmg) * 100;

            // 嘗試使用 PlatformUtil 撈取玩家真正的名字
            string playerName = $"玩家 {pId}"; // 預設留底，以防撈失敗
            try
            {
                // 確保 RunManager 和 NetService 不是 null 再呼叫
                if (MegaCrit.Sts2.Core.Runs.RunManager.Instance?.NetService?.Platform != null)
                {
                    playerName = PlatformUtil.GetPlayerNameRaw(MegaCrit.Sts2.Core.Runs.RunManager.Instance.NetService.Platform, pId);
                }
            }
            catch (System.Exception ex)
            {
                // 如果拋出異常，留下一行日誌，並用原本的 "玩家 ID" 當作替代方案
                MegaCrit.Sts2.Core.Logging.Log.Warn($"[BaiMod UI] 嘗試獲取玩家名稱失敗 (ID: {pId}): {ex.Message}");
            }

            // 格式化文字： "玩家暱稱: 1,500 (60.0%)"
            string displayText = $"{playerName}: {pData.CombatDamage:N0} ({pct:F1}%)";

            if (_playerLabels.TryGetValue(pId, out var label))
            {
                if (GodotObject.IsInstanceValid(label))
                {
                    label.Text = displayText;
                }
            }
            else
            {
                var newLabel = new Label();
                newLabel.Text = displayText;
                _vboxContainer.AddChild(newLabel);
                _playerLabels[pId] = newLabel;
            }
        }
    }
    /// <summary>
    /// 戰鬥結束，釋放 UI 資源
    /// </summary>
    public static void DestroyLayout()
    {
        if (_canvasLayer != null && GodotObject.IsInstanceValid(_canvasLayer))
        {
            _canvasLayer.QueueFree(); 
        }
        _canvasLayer = null;
        _vboxContainer = null;
        _titleLabel = null;
        _playerLabels.Clear();
    }
}