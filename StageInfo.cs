using I2.Loc;
using KSP.Game;
using KSP.Messages;
using KSP.Sim.DeltaV;
using KSP.Sim.impl;
using Newtonsoft.Json;
using SpaceWarp.API;
using SpaceWarp.API.Configuration;
using SpaceWarp.API.Mods;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace StageInfo {

    //[ModConfig]
    //[JsonObject(MemberSerialization.OptOut)]
    //public class StageInfoConfig {
    //    [ConfigField("Update frequency during flight (in seconds)")]
    //    [ConfigDefaultValue(0.1)]
    //    public double inFlightUpdateInterval;
    //    [ConfigField("TWR above this value displays as green")]
    //    [ConfigDefaultValue(1.25)]
    //    public double twrThresholdGreen;
    //    [ConfigField("TWR below this value displays as red")]
    //    [ConfigDefaultValue(1.0)]
    //    public double twrThresholdRed;
    //}



    [MainMod]
    public class StageInfoMain : Mod {

        private bool showGUI = false;
        private bool inVAB = false;
        private bool hideExtra = true;
        private int layoutWidth = 300;
        private double updateInterval = 0.1; // replace with inFlightUpdateInterval
        private double lastUpdateUT = 0;
        private Rect layoutRect;
        private GUISkin _spaceWarpUISkin;
        private GUIStyle horizontalDivider = new GUIStyle();
        private List<string> cels = new List<string>();
        private List<StageInfo> stageInfo = new List<StageInfo>();

        public void Awake() => layoutRect = new Rect((Screen.width * 0.8632f) - (layoutWidth / 2), (Screen.height / 2) - 350, 0, 0);
        public override void OnInitialized() {
            SpaceWarp.API.AssetBundles.ResourceManager.TryGetAsset($"space_warp/swconsoleui/swconsoleUI/spacewarpConsole.guiskin", out _spaceWarpUISkin);
            SpaceWarpManager.RegisterAppButton("Stage Info", "BTN-StageInfoButton", SpaceWarpManager.LoadIcon(), delegate { showGUI = !showGUI; });
            GameManager.Instance.Game.Messages.Subscribe<VesselDeltaVCalculationMessage>(ReceiveStageInfo);
            GameManager.Instance.Game.Messages.Subscribe<GameStateChangedMessage>(UpdateGameState);
            GameManager.Instance.Game.Messages.Subscribe<OpenEngineersReportWindowMessage>(ShowGUI);
            horizontalDivider.fixedHeight = 2;
            horizontalDivider.margin = new RectOffset(0, 0, 4, 4);
        }
        
        private void ShowGUI(MessageCenterMessage msg) { showGUI = true; if (cels.Count == 0) cels = GetCelNames(); }
        private void UpdateGameState(MessageCenterMessage msg) { showGUI = false; inVAB = GameManager.Instance.Game.GlobalGameState.GetState() == GameState.VehicleAssemblyBuilder; }

        public void OnGUI() {
            if (!showGUI) return;
            GUI.skin = _spaceWarpUISkin;
            layoutRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), layoutRect, MainGUI, "<color=#22262E>========================</color>", GUILayout.Height(0), GUILayout.Width(layoutWidth));
        }
        private void MainGUI(int windowID) {
            GUILayout.BeginHorizontal();
            if (stageInfo.Count == 0) {
                GUILayout.Label(" <i>Awaiting Stage Info...</i>");
                GUILayout.FlexibleSpace();
                showGUI = !GUILayout.Button("x", GUILayout.Width(28));
                GUILayout.EndHorizontal();
                GUI.DragWindow(new Rect(0, 0, 10000, 500));
                return; }
            bool clear = GUILayout.Button("▲", GUILayout.Width(28));
            GUILayout.FlexibleSpace();
            GUILayout.Label("<color=#696DFF>// STAGE INFO </color>");
            GUILayout.FlexibleSpace();
            hideExtra = GUILayout.Toggle(hideExtra, "-", "Button", GUILayout.Width(28));
            showGUI = !GUILayout.Button("x", GUILayout.Width(28));
            GUILayout.EndHorizontal();
            if (clear) { stageInfo.Clear(); return; }
            GUILayout.Box("", horizontalDivider);
            GUILayout.BeginHorizontal();
            GUILayout.Label("<b><color=#C0C7D5>S.</color> <color=#0A0B0E>|</color></b>", GUILayout.Width(35));
            GUILayout.Label("<b><color=#C0C7D5>T/W</color>  <color=#0A0B0E>|</color></b>", GUILayout.Width(57));
            GUILayout.Label($"<color=#C0C7D5><b>∆<i>v</i></b> (m/s)</color><b><color=#0A0B0E>{(inVAB ? "" : " ")}|</color></b>");
            GUILayout.Label($"<b>{(inVAB ? "<color=#C0C7D5>SITUATION" : "<color=#C0C7D5>BURN TIME")}</color></b>");
            GUILayout.EndHorizontal();
            GUILayout.Box("", horizontalDivider);
            for (int i = stageInfo.Count - (inVAB ? 2 : 1); i > -1; i--) {
                if (inVAB && hideExtra && stageInfo[i].IsEmpty()) continue;
                if (!inVAB && hideExtra && i > 0) continue;
                if (inVAB && !stageInfo[i].isCelSelected) {
                    GUILayout.BeginHorizontal();
                    bool prevCel = GUILayout.Button("<", GUILayout.Width(28));
                    GUILayout.Label($" {stageInfo[i].SelectedCel}");
                    bool nextCel = GUILayout.Button(">", GUILayout.Width(28));
                    stageInfo[i].isCelSelected = GUILayout.Toggle(stageInfo[i].isCelSelected, "Select", "Button", GUILayout.Width(78));
                    GUILayout.EndHorizontal();
                    int index = cels.IndexOf(stageInfo[i].SelectedCel);
                    if (prevCel) stageInfo[i].SelectCel(cels[index - 1 > -1 ? index - 1 : cels.Count - 1]);
                    else if (nextCel) stageInfo[i].SelectCel(cels[index + 1 < cels.Count ? index + 1 : 0]);
                } else {
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"<color=#C0C7D5>{stageInfo[i].Num:00}</color> <color=#0A0B0E><b>|</b></color>", GUILayout.Width(35));
                    GUILayout.Label($"{stageInfo[i].TWRFormatted(inVAB)} <color=#0A0B0E><b>|</b></color>", GUILayout.Width(57));
                    GUILayout.Label($"<color=#C0C7D5>{stageInfo[i].DeltaVFormatted(inVAB)}</color>");
                    if (inVAB) stageInfo[i].isCelSelected = GUILayout.Toggle(stageInfo[i].isCelSelected, stageInfo[i].SelectedCel, "Button", GUILayout.Width(110));
                    else GUILayout.Label($"<color=#0A0B0E><b>|</b></color>  <color=#C0C7D5>{stageInfo[i].BurnTimeFormatted()}</color>", GUILayout.Width(108));
                    GUILayout.EndHorizontal(); }}
            GUI.DragWindow(new Rect(0, 0, 10000, 500));
        }

        private void ReceiveStageInfo(MessageCenterMessage mcm) {
            if (!inVAB && (!showGUI || GameManager.Instance.Game.UniverseModel.UniversalTime < lastUpdateUT + updateInterval /* inFlightUpdateInterval */)) return;
            VesselDeltaVCalculationMessage info = mcm as VesselDeltaVCalculationMessage;
            if (info == null) return;
            if (!inVAB) lastUpdateUT = GameManager.Instance.Game.UniverseModel.UniversalTime;
            stageInfo.Clear();
            for (int i = 0; i < info.DeltaVComponent.StageInfo.Count; i++) {
                stageInfo.Add(new StageInfo(i + 1, info.DeltaVComponent.StageInfo[i], inVAB));
                Debug.Log(info.DeltaVComponent.StageInfo[i].SeparationIndex);
            }
        }
        private List<string> GetCelNames() {
            List<string> names = new List<string>();
            foreach (CelestialBodyComponent cb in GameManager.Instance.Game.UniverseModel.GetAllCelestialBodies()) {
                if (cb.Name == GameManager.Instance.Game.UniverseModel.HomeWorld.Name) { // add atmospheric data - cb.hasAtmosphere
                    names.Add(cb.Name + " (A)");
                    names.Add(cb.Name + " (V)");
                } else names.Add(cb.Name); }
            return names;
        }
        
        public class StageInfo {
            
            public int Num { get; private set; }
            public string SelectedCel { get; private set; }
            // public float TWRCur { get; private set; }
            // public double DVCur { get; private set; }
            public float TWRAtm { get; private set; }
            public double DVAtm { get; private set; }
            public float TWRVac { get; private set; }
            public double DVVac { get; private set; }
            // public double Burn { get; private set; }
            public bool isCelSelected;
            private DeltaVStageInfo info;

            public StageInfo(int num, DeltaVStageInfo stageInfo, bool inVAB) {
                Num = num;
                info = stageInfo;
                if (inVAB) {
                    isCelSelected = true;
                    SelectedCel = $"{GameManager.Instance.Game.UniverseModel.HomeWorld.Name} {(Num == 1 ? "(A)" : "(V)")}";
                    TWRAtm = info.TWRASL;
                    DVAtm = info.DeltaVatASL;
                    TWRVac = info.TWRVac;
                    DVVac = info.DeltaVinVac;
                } // else {
                    // Burn = Info.StageBurnTime;
                    // TWRCur = Info.TWRActual;
                    // DVCur = Info.DeltaVActual; }
            }

            public void SelectCel(string name) {
                SelectedCel = name == null ? SelectedCel : name;
                double g = GameManager.Instance.Game.UniverseModel.HomeWorld.gravityASL / GameManager.Instance.Game.UniverseModel.GetAllCelestialBodies().Find(c => SelectedCel.StartsWith(c.Name)).gravityASL;
                if (IsAtmo()) TWRAtm = info.TWRASL * (float)g;
                else TWRVac = info.TWRVac * (float)g;
            }

            public string TWRFormatted(bool inVAB) {
                float twr = !inVAB ? info.TWRActual : (IsAtmo() ? TWRAtm : TWRVac);
                return $"<color={(twr < 1.0 /*twrThresholdRed*/ ? "#E04949" : (twr > 1.25 /*twrThresholdGreen*/ ? "#0DBE2A" : "#E0A400"))}>{twr.ToString(twr < 10 ? "N2" : (twr < 100 ? "N1" : "N0"))}{(twr < 100 ? "" : " ")}</color>";
            }
            public string BurnTimeFormatted() {
                int s = (int)Math.Round(info.StageBurnTime);
                if (s < 60) return $"{s}s";
                return $"{s / 60}m {s % 60}s";
            }
            public string DeltaVFormatted(bool inVAB) => (!inVAB ? info.DeltaVActual : (IsAtmo() ? DVAtm : DVVac)).ToString("N0");
            
            private bool IsAtmo() => SelectedCel.Contains("(A)");
            public bool IsEmpty() => info.TWRActual < 0.1;

        }

    }
}
