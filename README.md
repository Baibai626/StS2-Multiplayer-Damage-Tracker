# StS2-Multiplayer-Damage-Tracker
Multiplayer damage tracker for each stage


本專案為獨立開發之開源遊戲模組（Mod），新增計算傷害之功能，方便對比、優化策略。
並解決多人連線模式下，狀態傷害（毒、消亡）等難以處理的DPS分配。

## 📂 核心檔案導覽
* `DmgCount.cs`：核心運算層（包含三維記帳字典與兩大核心演算法）。
* `DmgPatch.cs` / `StatusPatch.cs`：攔截層（負責監聽遊戲底層戰鬥與狀態事件）。
* `ModUI.cs`：展示層（基於 Godot 引擎之 CanvasLayer 繪製，實作滑鼠點擊穿透）。
