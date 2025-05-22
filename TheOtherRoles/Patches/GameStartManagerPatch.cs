  
using HarmonyLib;
using UnityEngine;
using System.Reflection;
using System.Collections.Generic;
using Hazel;
using System;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using TMPro;

namespace TheOtherRoles.Patches {
    public class GameStartManagerPatch  {
        public static Dictionary<int, PlayerVersion> playerVersions = new Dictionary<int, PlayerVersion>();
        private static float timer = 600f;
        private static float kickingTimer = 0f;
        private static bool versionSent = false;
        private static string lobbyCodeText = "";

        [HarmonyPatch(typeof(AmongUsClient), nameof(AmongUsClient.OnPlayerJoined))]
        public class AmongUsClientOnPlayerJoinedPatch {
            public static void Postfix() {
                if (PlayerControl.LocalPlayer != null) {
                    Helpers.shareGameVersion();
                }
            }
        }
        public static TextMeshPro ErrorText;

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Start))]
        public class GameStartManagerStartPatch {
            public static void Postfix(GameStartManager __instance) {
                // Trigger version refresh
                versionSent = false;
                // Reset lobby countdown timer
                timer = 600f; 
                // Reset kicking timer
                kickingTimer = 0f;
                // Copy lobby code
                string code = InnerNet.GameCode.IntToGameName(AmongUsClient.Instance.GameId);
                GUIUtility.systemCopyBuffer = code;
                lobbyCodeText = DestroyableSingleton<TranslationController>.Instance.GetString(StringNames.RoomCode, new Il2CppReferenceArray<Il2CppSystem.Object>(0)) + "\r\n" + code;
                ErrorText = new GameObject("ErrorText").AddComponent<TextMeshPro>();
                ErrorText.transform.SetParent(HudManager.Instance.gameObject.transform);
                ErrorText.text = "";
                ErrorText.fontSizeMax = ErrorText.fontSizeMin = ErrorText.fontSize = 5;
                ErrorText.gameObject.layer = 5;
                ErrorText.transform.localPosition = new Vector3(0, 0, -15);
                ErrorText.color = Color.red;
                ErrorText.material =    HudManager.Instance.Chat.quickChatMenu.timer.text.fontMaterial;
                ErrorText.alignment = TextAlignmentOptions.Center;
                ErrorText.autoSizeTextContainer = true;
                ErrorText.enableWordWrapping = false;
            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.Update))]
        public class GameStartManagerUpdatePatch {
            private static bool update = false;
            private static string currentText = "";
        
            public static void Prefix(GameStartManager __instance) {
                if (!AmongUsClient.Instance.AmHost  || !GameData.Instance || AmongUsClient.Instance.AmLocalHost) return; // Not host or no instance
                update = GameData.Instance.PlayerCount != __instance.LastPlayerCount;
            }

            public static void Postfix(GameStartManager __instance) {
                // Send version as soon as PlayerControl.LocalPlayer exists
                if (PlayerControl.LocalPlayer != null && !versionSent) {
                    versionSent = true;
                    Helpers.shareGameVersion();
                }

                // Host update with version handshake infos
                if (AmongUsClient.Instance.AmHost) {
                    bool blockStart = false;
                    string message = "";
                    foreach (InnerNet.ClientData client in AmongUsClient.Instance.allClients.ToArray()) {
                        if (client.Character == null) continue;
                        var dummyComponent = client.Character.GetComponent<DummyBehaviour>();
                        if (dummyComponent != null && dummyComponent.enabled)
                            continue;
                        else if (!playerVersions.ContainsKey(client.Id))  {
                            blockStart = true;
                            message += $"{client.Character.Data.PlayerName}:  {ModTranslation.GetString("errorNotInstalled")}\n";
                        } else {
                            PlayerVersion PV = playerVersions[client.Id];
                            int diff = TheOtherRolesPlugin.Version.CompareTo(PV.version);
                            if (diff > 0) {
                                message += $"{client.Character.Data.PlayerName}:  {ModTranslation.GetString("errorOlderVersion")} (v{playerVersions[client.Id].version})\n";
                                blockStart = true;
                            } else if (diff < 0) {
                                message += $"{client.Character.Data.PlayerName}:  {ModTranslation.GetString("errorNewerVersion")} (v{playerVersions[client.Id].version})\n";
                                blockStart = true;
                            } else if (!PV.GuidMatches()) { // version presumably matches, check if Guid matches
                                message += $"{client.Character.Data.PlayerName}:  {ModTranslation.GetString("errorWrongVersion")} v{playerVersions[client.Id].version} <size=30%>({PV.guid})</size>\n";
                                blockStart = true;
                            }
                        }
                    }
                    if (blockStart) {
                        __instance.StartButton.SetButtonEnableState(false);
                        ErrorText.text = message;
                    } else {
                        __instance.StartButton.SetButtonEnableState(true);
                        ErrorText.text = "";
                    }
                }

                // Client update with handshake infos
                if (!AmongUsClient.Instance.AmHost) {
                    if (!playerVersions.ContainsKey(AmongUsClient.Instance.HostId) || TheOtherRolesPlugin.Version.CompareTo(playerVersions[AmongUsClient.Instance.HostId].version) != 0) {
                        kickingTimer += Time.deltaTime;
                        if (kickingTimer > 10) {
                            kickingTimer = 0;
                            TheOtherRolesPlugin.Logger.LogError("ExitGame : Version is different.");
			                AmongUsClient.Instance.ExitGame(DisconnectReasons.ExitGame);
                            SceneChanger.ChangeScene("MainMenu");
                        }
                        ErrorText.text = String.Format(ModTranslation.GetString("errorHostNoVersion"), Math.Round(10 - kickingTimer));
                        //ErrorText.transform.localPosition = __instance.StartButton.transform.localPosition + Vector3.up * 2;
                    } else {
                        if (__instance.startState != GameStartManager.StartingStates.Countdown) {
                            ErrorText.text = String.Empty;
                        }
                    }
                }

                // Lobby code replacement
                //if (AmongUsClient.Instance.NetworkMode == NetworkModes.LocalGame) __instance.GameRoomNameCode.text = TheOtherRolesPlugin.StreamerMode.Value ? $"<color={TheOtherRolesPlugin.StreamerModeReplacementColor.Value}>{TheOtherRolesPlugin.StreamerModeReplacementText.Value}</color>" : lobbyCodeText;

                // Lobby timer
                if (!AmongUsClient.Instance.AmHost || !GameData.Instance || AmongUsClient.Instance.AmLocalHost) return; // Not host or no instance

                if (update) currentText = __instance.PlayerCounter.text;

                timer = Mathf.Max(0f, timer -= Time.deltaTime);
                int minutes = (int)timer / 60;
                int seconds = (int)timer % 60;
                string suffix = $" ({minutes:00}:{seconds:00})";

                __instance.PlayerCounter.text = currentText + suffix;
                __instance.PlayerCounter.autoSizeTextContainer = true;

            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.BeginGame))]
        public class GameStartManagerBeginGame {
            public static bool Prefix(GameStartManager __instance) {
                // Block game start if not everyone has the same mod version
                bool continueStart = true;

                if (AmongUsClient.Instance.AmHost) {
                    foreach (InnerNet.ClientData client in AmongUsClient.Instance.allClients) {
                        if (client.Character == null) continue;
                        var dummyComponent = client.Character.GetComponent<DummyBehaviour>();
                        if (dummyComponent != null && dummyComponent.enabled)
                            continue;
                        
                        if (!playerVersions.ContainsKey(client.Id)) {
                            continueStart = false;
                            break;
                        }
                        
                        PlayerVersion PV = playerVersions[client.Id];
                        int diff = TheOtherRolesPlugin.Version.CompareTo(PV.version);
                        if (diff != 0 || !PV.GuidMatches()) {
                            continueStart = false;
                            break;
                        }
                    }

                    if (CustomOptionHolder.uselessOptions.getBool() && CustomOptionHolder.dynamicMap.getBool() && continueStart) {
                        // 0 = Skeld
                        // 1 = Mira HQ
                        // 2 = Polus
                        // 3 = Dleks - deactivated
                        // 4 = Airship
                        List<byte> possibleMaps = new List<byte>();
                        if (CustomOptionHolder.dynamicMapEnableSkeld.getBool())
                            possibleMaps.Add(0);
                        if (CustomOptionHolder.dynamicMapEnableMira.getBool())
                            possibleMaps.Add(1);
                        if (CustomOptionHolder.dynamicMapEnablePolus.getBool())
                            possibleMaps.Add(2);
                        if (CustomOptionHolder.dynamicMapEnableDleks.getBool())
                            possibleMaps.Add(3);
                        if (CustomOptionHolder.dynamicMapEnableAirShip.getBool())
                            possibleMaps.Add(4);
                        byte chosenMapId  = possibleMaps[TheOtherRoles.rnd.Next(possibleMaps.Count)];

                        MessageWriter writer = AmongUsClient.Instance.StartRpcImmediately(PlayerControl.LocalPlayer.NetId, (byte)CustomRPC.DynamicMapOption, Hazel.SendOption.Reliable, -1);
                        writer.Write(chosenMapId);
                        AmongUsClient.Instance.FinishRpcImmediately(writer);
                        RPCProcedure.dynamicMapOption(chosenMapId);
                    }
                }
                return continueStart;
            }
        }

        [HarmonyPatch(typeof(GameStartManager), nameof(GameStartManager.SetStartCounter))]
        public static class SetStartCounterPatch
        {
            public static void Postfix(GameStartManager __instance, sbyte sec)
            {
                if (sec > 0)
                {
                    __instance.startState = GameStartManager.StartingStates.Countdown;
                }

                if (sec <= 0)
                {
                    __instance.startState = GameStartManager.StartingStates.NotStarting;
                }
            }
        }

        public class PlayerVersion {
            public readonly Version version;
            public readonly Guid guid;

            public PlayerVersion(Version version, Guid guid) {
                this.version = version;
                this.guid = guid;
            }

            public bool GuidMatches() {
                return Assembly.GetExecutingAssembly().ManifestModule.ModuleVersionId.Equals(this.guid);
            }
        }
    }
}
