using KSP;
using KSP.Game;
using KSP.Messages;
using KSP.Sim;
using KSP.Sim.DeltaV;
using KSP.Sim.impl;
using Newtonsoft.Json;
using SpaceWarp.API;
using SpaceWarp.API.Mods.JSON;
using SpaceWarp.API.UI;
using SpaceWarp.API.UI.Appbar;
using SpaceWarp.API.Mods;
using SpaceWarp.API.Assets;
using SpaceWarp.UI;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

using SpaceWarp;
using BepInEx;
using BepInEx.Bootstrap;
using System.EnterpriseServices;
using KSP.Inspector;
using KSP.UI.Binding;

namespace StageInfo {

    [BepInPlugin("com.Natalia.StageInfo", "StagInfoMod", "0.4.0")]
    [BepInDependency(SpaceWarpPlugin.ModGuid, SpaceWarpPlugin.ModVer)]

    public class StageInfoMain : BaseSpaceWarpPlugin {

        private static StageInfoMain Instance { get; set; }
        private bool showGUI = false;
        private bool inVAB = false;
        private bool hideExtra = true;
        private int layoutWidth = 300;
        // private double updateInterval = 0.1;
        // private double lastUpdateUT = 0;
        private Rect layoutRect;
        private GUISkin _spaceWarpUISkin = Skins.ConsoleSkin;
        private GUIStyle horizontalDivider = new GUIStyle();
        private List<string> cels = new List<string>();
        private List<StageInfo> stageInfo = new List<StageInfo>();
        private AltUnits altUnits = AltUnits.Km;
        private enum AltUnits {
            Km=1000,
            Mm=1000000,
            Gm=1000000000
        };
        
        public void Awake() => layoutRect = new Rect((Screen.width * 0.8632f) - (layoutWidth / 2), (Screen.height / 2) - 350, 0, 0);

        public override void OnInitialized() {

            base.OnInitialized();
            Instance = this;

            Appbar.RegisterAppButton(
                "Stage Info",
                "BTN-StageInfoBtn",
                AssetManager.GetAsset<Texture2D>($"{SpaceWarpMetadata.ModID}/images/icon.png"),
                ToggleButton
            ); ; ;
            
            GameManager.Instance.Game.Messages.Subscribe<VesselDeltaVCalculationMessage>(ReceiveStageInfo);
            GameManager.Instance.Game.Messages.Subscribe<GameStateChangedMessage>(UpdateGameState);
            GameManager.Instance.Game.Messages.Subscribe<OpenEngineersReportWindowMessage>(ShowGUI);
            horizontalDivider.fixedHeight = 2;
            horizontalDivider.margin = new RectOffset(0, 0, 4, 4);
        }

        private void ToggleButton(bool toggle)
        {
            showGUI = toggle;
            GameObject.Find("BTN-StageInfoBtn")?.GetComponent<UIValue_WriteBool_Toggle>()?.SetValue(toggle);

        }

        private void ShowGUI(MessageCenterMessage msg) { showGUI = true; if (cels.Count == 0) cels = GetCelNames(); }

        private void UpdateGameState(MessageCenterMessage msg) {
            showGUI = false;
            inVAB = GameManager.Instance.Game.GlobalGameState.GetState() == GameState.VehicleAssemblyBuilder;
        }

        public void OnGUI() {
            if (!showGUI) return;
            GUI.skin = Skins.ConsoleSkin;
            layoutRect = GUILayout.Window(GUIUtility.GetControlID(FocusType.Passive), layoutRect, MainGUI, "<color=#22262E>========================</color>", GUILayout.Height(0), GUILayout.Width(layoutWidth));
        }

        private void MainGUI(int windowID) {
            GUILayout.BeginHorizontal();
            if (stageInfo.Count == 0) {
                GUILayout.Label(" <i>Waiting for updated info...</i>");
                GUILayout.FlexibleSpace();
                showGUI = !GUILayout.Button("x", GUILayout.Width(28));
                GUILayout.EndHorizontal();
                GUI.DragWindow(new Rect(0, 0, 10000, 500));
                return; }
            bool clear = GUILayout.Button("¬", GUILayout.Width(28));
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
                GUILayout.BeginHorizontal();
                GUILayout.Label($"<color=#C0C7D5>{stageInfo[i].Num:00}</color> <color=#0A0B0E><b>|</b></color>", GUILayout.Width(35));
                if (!stageInfo[i].IsEmpty()) {
                    GUILayout.Label($"{stageInfo[i].TWRFormatted(inVAB)} <color=#0A0B0E><b>|</b></color>", GUILayout.Width(57));
                    GUILayout.Label($"<color=#C0C7D5>{stageInfo[i].DeltaVFormatted(inVAB)}</color>");
                    if (inVAB) {
                        if (stageInfo[i].isCelSelected && stageInfo.Any(s => !s.isCelSelected)) {
                            GUILayout.Label(" ", GUILayout.Width(16));
                            GUILayout.Label("<color=#0A0B0E><b>|</b></color>");
                            GUILayout.FlexibleSpace();
                            GUILayout.Label($"<color=#C0C7D5>{stageInfo[i].SelectedCel}</color>");
                            GUILayout.FlexibleSpace();
                            GUILayout.Label(" ", GUILayout.Width(3));
                        } else stageInfo[i].isCelSelected = GUILayout.Toggle(stageInfo[i].isCelSelected, stageInfo[i].isCelSelected ? stageInfo[i].SelectedCel : "Confirm", "Button", GUILayout.Width(110));
                    } else GUILayout.Label($"<color=#0A0B0E><b>|</b></color>  <color=#C0C7D5>{stageInfo[i].BurnTimeFormatted()}</color>", GUILayout.Width(108));
                } else GUILayout.Label($"<color=#0D1C2A><i> ======= Empty Stage ======= </i></color>");
                GUILayout.EndHorizontal();
            }

            if (inVAB && stageInfo.Any(s => !s.isCelSelected)) {
                int i = stageInfo.FindIndex(s => !s.isCelSelected);
                GUILayout.BeginHorizontal();
                GUILayout.Label("<color=#22262E>   ----------------------------   </color>");
                GUILayout.EndHorizontal();
                GUILayout.BeginHorizontal();
                bool prevCel = GUILayout.Button("<", GUILayout.Width(28));
                GUILayout.Label($" {stageInfo[i].SelectedCel} ({(stageInfo[i].altitude / (float)altUnits).ToString(altUnits == AltUnits.Km ? "N1" : "N2")} {altUnits.Description()})");
                bool nextCel = GUILayout.Button(">", GUILayout.Width(28));
                GUILayout.EndHorizontal();
                int index = cels.IndexOf(stageInfo[i].SelectedCel);
                if (prevCel) stageInfo[i].SelectCel(cels[index - 1 > -1 ? index - 1 : cels.Count - 1]);
                else if (nextCel) stageInfo[i].SelectCel(cels[index + 1 < cels.Count ? index + 1 : 0]);
                if (stageInfo[i].SelectedCel != "Kerbin (ASL)") {
                    GUILayout.Box("", horizontalDivider);
                    GUILayout.Box("", horizontalDivider);
                    GUILayout.BeginHorizontal();
                    stageInfo[i].altitude = GUILayout.HorizontalSlider(stageInfo[i].altitude, 
                        stageInfo[i].Cel.hasAtmosphere ? (float)stageInfo[i].Cel.atmosphereDepth : 0f,
                        (altUnits == AltUnits.Km && stageInfo[i].Cel.sphereOfInfluence > 1000000) ? 1000000 : (float)stageInfo[i].Cel.sphereOfInfluence);
                    bool altCycle = GUILayout.Button(altUnits.Description(), GUILayout.Width(35));
                    if (altCycle) {
                        altUnits = altUnits == AltUnits.Km ? AltUnits.Mm : AltUnits.Km;
                        if (altUnits == AltUnits.Km) stageInfo[i].ResetAltitude(); }
                    GUILayout.EndHorizontal();
                }
                GUILayout.BeginHorizontal();
                GUILayout.Label($" ↓ {stageInfo[i].GFormatted()} g");
                if (stageInfo[i].Cel.hasAtmosphere) GUILayout.Label($" ◌ {(stageInfo[i].Cel.GetPressure(stageInfo[i].altitude) / 101.325):N0} atm");
                GUILayout.EndHorizontal();
            }
            GUI.DragWindow(new Rect(0, 0, 10000, 500));
        }

        private void ReceiveStageInfo(MessageCenterMessage mcm) {
            // if (!inVAB && (!showGUI || GameManager.Instance.Game.UniverseModel.UniversalTime < lastUpdateUT + updateInterval /* inFlightUpdateInterval */)) return;
            VesselDeltaVCalculationMessage info = mcm as VesselDeltaVCalculationMessage;
            if (info == null) return;
            // if (!inVAB) lastUpdateUT = GameManager.Instance.Game.UniverseModel.UniversalTime;
            stageInfo.Clear();
            for (int i = 0; i < info.DeltaVComponent.StageInfo.Count; i++)
                stageInfo.Add(new StageInfo(i + 1, info.DeltaVComponent.StageInfo[i], inVAB));
        }
        private List<string> GetCelNames() {
            List<string> names = new List<string>();
            foreach (CelestialBodyComponent cel in GameManager.Instance.Game.UniverseModel.GetAllCelestialBodies()) {
                if (cel.Name == GameManager.Instance.Game.UniverseModel.HomeWorld.Name) {
                    names.Add(cel.Name + " (ASL)");
                    names.Add(cel.Name);
                } else names.Add(cel.Name); }
            return names;
        }

        public class StageInfo {
            
            public int Num { get; private set; }
            public string SelectedCel { get; private set; }
            public float TWRAtm { get; private set; }
            public double DVAtm { get; private set; }
            public float TWRVac { get; private set; }
            public double DVVac { get; private set; }
            public CelestialBodyComponent Cel { get; private set; }
            public bool isCelSelected;
            public float altitude;
            private DeltaVStageInfo info;

            public StageInfo(int num, DeltaVStageInfo stageInfo, bool inVAB) {
                Num = num;
                info = stageInfo;
                if (inVAB) {
                    isCelSelected = true;
                    SelectedCel = $"{GameManager.Instance.Game.UniverseModel.HomeWorld.Name}{(Num == 1 ? " (ASL)" : "")}";
                    SetCelestial();
                    ResetAltitude();
                    TWRAtm = info.TWRASL;
                    DVAtm = info.DeltaVatASL;
                    TWRVac = info.TWRVac;
                    DVVac = info.DeltaVinVac; }
            }

            public void SelectCel(string name) {
                SelectedCel = name;
                SetCelestial();
                ResetAltitude();
                // double g = GameManager.Instance.Game.UniverseModel.HomeWorld.gravityASL / Cel.gravityASL;
                if (IsAtmo()) TWRAtm = info.TWRASL; //  * (float)g
                else TWRVac = info.TWRVac; //  * (float)g
            }

            public string TWRFormatted(bool inVAB) {
                double twr = !inVAB ? info.TWRActual : (IsAtmo() ? TWRAtm : TWRVac) * GameManager.Instance.Game.UniverseModel.HomeWorld.gravityASL / GetGAltitude();
                string str = "";
                if (twr < 1000) str = $"{twr.ToString(twr < 10 ? "N2" : (twr < 100 ? "N1" : "N0"))}{(twr < 100 ? "" : " ")}";
                else if (twr < 1000000) str = $"{(twr / 1000).ToString(twr < 10000 ? "N1" : "N0")}K{(twr < 10000 || twr > 99999 ? "" : " ")}";
                else str = $"{(twr / 1000000).ToString(twr < 10000000 ? "N1" : "N0")}M{(twr < 10000000 || twr > 100000000 ? "" : " ")}"; 
                return $"<color={(twr < 1.0 /*twrThresholdRed*/ ? "#E04949" : (twr > 1.25 /*twrThresholdGreen*/ ? "#0DBE2A" : "#E0A400"))}>{str}</color>";
            }
            public string BurnTimeFormatted() {
                int s = (int)Math.Round(info.StageBurnTime);
                if (s < 60) return $"{s}s";
                return $"{s / 60}m {s % 60}s";
            }
            public string DeltaVFormatted(bool inVAB) => (!inVAB ? info.DeltaVActual : (IsAtmo() ? DVAtm : DVVac)).ToString("N0");
            public string GFormatted() {
                double g = GetGAltitude();
                return $"{g.ToString(g < 0.1 ? (g < 0.001 ? "N5" : (g < 0.01 ? "N4" : "N3")) : "N2")}";
            }

            private bool IsAtmo() => SelectedCel.Contains("(ASL)");
            public bool IsEmpty() => info.TWRActual < 0.1;
            public double GetGAltitude() => Cel.gravityASL * Math.Pow(Cel.radius / (Cel.radius + altitude), 2); // 9.80665
            private void SetCelestial() => Cel = GameManager.Instance.Game.UniverseModel.GetAllCelestialBodies().Find(c => SelectedCel.StartsWith(c.Name));
            public void ResetAltitude() => altitude = SelectedCel == "Kerbin (ASL)" ? 0 : (Cel.hasAtmosphere ? (float)Cel.atmosphereDepth : 0);

        }

    }
}
