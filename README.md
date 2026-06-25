# STS2-Multiplayer-Damage-Tracker
Multiplayer damage tracker for each stage

本專案為獨立開發之開源遊戲模組（Mod），新增計算傷害之功能，方便對比、優化策略。
並解決多人連線模式下，狀態傷害（毒、消亡）等難以處理的DPS分配。

### About the Project
This project is an independently developed, open-source game module (Mod) designed to calculate and track in-game damage, helping players easily compare and optimize their combat strategies. 

Furthermore, it successfully resolves the long-standing challenge of DPS attribution for status-inflicted damage (such as Poison and Demise) in multiplayer environments, ensuring fair and accurate data distribution.

## 📂 Project Structure / 檔案架構
* `DmgCount.cs`：Damage tracking and core logic / 傷害統計與核心邏輯處理。
* `DmgPatch.cs`：Combat event hooks / 戰鬥事件攔截。
* `StatusPatch.cs`：Status effect hooks / 狀態事件攔截。
* `ModUI.cs`：In-game UI display / 遊戲內使用者介面顯示。
