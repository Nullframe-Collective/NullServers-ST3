using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Object;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace NullServers
{
    internal static class NetBootstrapMembers
    {
        extension(NetBootstrap)
        {
            internal static NetBootstrap Current => NetBootstrap.GJDGDIGECJN;
        }

        extension(NetBootstrap bootstrap)
        {
            internal NetworkManager Network => bootstrap.HLNKMGIBPIG;

            internal bool JoinRoom(string roomId) => bootstrap.FPFDMJBMCAN(roomId);

            internal RoomProperties RoomSettings
            {
                get => bootstrap._GCBPPKIDDHB_k__BackingField;
                set => bootstrap._GCBPPKIDDHB_k__BackingField = value;
            }

            internal string PendingScene
            {
                get => bootstrap.HNHLFANHCFG;
                set => bootstrap.HNHLFANHCFG = value;
            }
        }
    }

    internal static class RoomMenuMembers
    {
        extension(RoomMultiplayerMenu)
        {
            internal static RoomMultiplayerMenu Current => RoomMultiplayerMenu.GJDGDIGECJN;
        }

        extension(RoomMultiplayerMenu menu)
        {
            internal bool IsInitialized => menu.CJNLEDLNGID;
            internal bool IsPaused => menu.IOMIAHNBDOG;
            internal Il2CppSystem.Collections.Generic.List<Il2CppSystem.Collections.Generic.KeyValuePair<PlayerInfo, Text>> PlayerRows => menu.HOKHMBNMGHC;
        }
    }

    internal static class ChatMembers
    {
        extension(MultiplayerChat)
        {
            internal static MultiplayerChat Current => MultiplayerChat.CFNPNLGIHMH;
        }

        extension(MultiplayerChat chat)
        {
            internal GUIStyle NameStyle => chat.JELNPHBIKHK;
            internal InputField Input => chat.BOCAABIECMF;
            internal void AddLocalMessage(string sender, string message, string color) => chat.HLIDELGJEON(sender, message, color);
        }
    }

    internal static class PlayerInfoMembers
    {
        extension(PlayerInfo)
        {
            internal static PlayerInfo Local => PlayerInfo.KEBDMAALEDI;
        }

        extension(PlayerInfo player)
        {
            internal int ClientId => player.COJDPMKCLCM;
            internal string Nickname => player.HOGIPFLCOLH;
            internal string Team => player.NDAEOCECHBM;
            internal int Kills => player.ANGFKCCEJKD;
            internal int Deaths => player.DDLEOEFHMKL;

            internal void SetTeam(string team) => player.EIDHGMOHFPI(team);
            internal void ResetScore() => player.LEEDMMPNCJB();
        }
    }

    internal static class GameMembers
    {
        extension(FPScontroller movement)
        {
            internal CharacterController Controller => movement.PJJBJAPJODD;
            internal FPScontroller.FPScontrollerMovement Movement => movement.HKCDMBALAAK;
            internal FPScontroller.FPScontrollerJumping Jumping => movement.AALHECCKHFD;

            internal bool Grounded
            {
                get => movement.DBNLEAKGLGC;
                set => movement.DBNLEAKGLGC = value;
            }
        }

        extension(SyncCustomization sync)
        {
            internal Il2CppSystem.Collections.Generic.List<Transform> Hats => sync.GKCNMADGAAF;
            internal Transform HatRoot => sync.LEEICKEPPEB;
            internal Transform AccentRoot => sync.ELPJDJFCCNJ;
            internal bool SkinsDisabled => sync.CNNHJCHFEBO;
            internal bool FromLegacyReplay => sync.IGDAIDNEJBO;
            internal MeshRenderer Head => sync.KAMAAAJBLLG;
            internal MeshRenderer SkinHead => sync.EKMFOOLPMCC;
            internal SkinnedMeshRenderer Body => sync.NGBLPBOAANH;
            internal SkinnedMeshRenderer LeftArm => sync.DCBPBGEFBHD;
            internal SkinnedMeshRenderer RightArm => sync.FFDDAKMAHKI;
            internal SkinnedMeshRenderer LeftLeg => sync.JCGAKKLDCKJ;
            internal SkinnedMeshRenderer RightLeg => sync.IIGKOHECBEL;

            internal int HatId
            {
                get => sync.CLENBEHPEEL;
                set => sync.CLENBEHPEEL = value;
            }

            internal Vector3 FurColor
            {
                get => sync.LJALDBMBHHN;
                set => sync.LJALDBMBHHN = value;
            }

            internal string SkinData
            {
                get => sync.CLABNNBNKAF;
                set => sync.CLABNNBNKAF = value;
            }

            internal void SendAppearance(int hat, Vector3 furColor, string skinData) => sync.BIHMIMOKJLM(hat, furColor, skinData);
        }

        extension(CharacterCustomization customization)
        {
            internal Il2CppSystem.Collections.Generic.List<CharacterCustomization.hat> Hats => customization.GKCNMADGAAF;

            internal IEnumerable<CharacterCustomization.hat> AllHats
            {
                get
                {
                    foreach (var list in new[] { customization.GKCNMADGAAF, customization.PFGCEJKOIDO })
                    {
                        if (list == null) continue;

                        foreach (CharacterCustomization.hat hat in list)
                        {
                            yield return hat;
                        }
                    }

                    if (customization.DEGGIDJJNDO == null) yield break;

                    foreach (CharacterCustomization.hatList group in customization.DEGGIDJJNDO)
                    {
                        if (group?.hat == null) continue;

                        foreach (CharacterCustomization.hat hat in group.hat)
                        {
                            yield return hat;
                        }
                    }
                }
            }
        }

        extension(PlayerNetworkController player)
        {
            internal PlayerDamage Damage => player.FJEJDPCPOJC;
            internal Transform WeaponRoot => player.HDCJMMEMDEJ;
            internal Transform Model => player.JCGAJPLLOLC;

            internal string DisplayName
            {
                get => player.DJHKBCAIADF;
                set => player.DJHKBCAIADF = value;
            }

            internal bool HasNameColor
            {
                get => player.AJGABBOMGJB;
                set => player.AJGABBOMGJB = value;
            }

            internal Color NameColor
            {
                get => player.NDHICLFEEKL;
                set => player.NDHICLFEEKL = value;
            }
        }

        extension(PlayerDamage damage)
        {
            internal float Health => damage.PIIDNIGPCDK;
            internal Transform RagdollPrefab => damage.FNKDCFDBINH;
            internal void TakeDamage(float amount, PlayerInfo attacker) => damage.LAKFCNKPBPM(amount, attacker);
        }

        extension(Bot bot)
        {
            internal float Health => bot.PIIDNIGPCDK;
            internal Transform RagdollPrefab => bot.OGJJNCKKILE;
        }

        extension(BossBot boss)
        {
            internal float Health => boss.PIIDNIGPCDK;
            internal Transform RagdollPrefab => boss.OGJJNCKKILE;
        }

        extension(Volume volume)
        {
            internal Il2CppSystem.Collections.Generic.List<Volume.catagory> Categories => volume.PDKPIOHFCCK;
            internal Il2CppSystem.Collections.Generic.List<RectTransform> CategoryButtons => volume.AIJBBOEEOIE;
            internal RectTransform Panel => volume.GGNOPAOFGDO;
            internal RectTransform ButtonTemplate => volume.ONPICMKGDHB;
            internal Text InfoText => volume.PIEIJMCFDEE;
            internal Il2CppFishNet.Object.Synchronizing.SyncVar<int> ConsoleState => volume.APJODFNHLED;
        }

        extension(LobbyMenu lobby)
        {
            internal GUISkin Skin => lobby.BIPMFIBNFBC;
            internal string RoomName => lobby.JEGIAEANOII;
            internal bool IsModeListOpen => lobby.EGDMPMDBJIK;
            internal bool IsCreatingRoom => lobby.EJLDOIOJGPC;
            internal Il2CppSystem.Collections.Generic.List<RelayRoomInfo> Rooms => lobby.NIBAENPHGKO;
            internal Il2CppSystem.Collections.Generic.List<LobbyMenu.AllModes> Modes => lobby.MHDLCNKEEGN;

            internal IEnumerable<LobbyMenu.AllMaps> MapEntries
            {
                get
                {
                    foreach (var list in new[] { lobby.IILJODEKLIC, lobby.EMDCLAPMCEB, lobby.CFJBONPPILK, lobby.KFFNCDCDCPI })
                    {
                        if (list == null) continue;

                        foreach (LobbyMenu.AllMaps map in list)
                        {
                            yield return map;
                        }
                    }
                }
            }
        }

        extension(MainMenu menu)
        {
            internal GameObject ServerPanel => menu.OJKJNHPHFFO;
            internal GameObject LobbyObject => menu.LFGIAIMOBLB;
            internal GameObject UpdateManagerObject => menu.IKJFGOGMBHM;
            internal UpdaterV2 Updater => menu.LNJBJHMGKNJ;
        }

        extension(UpdaterV2 updater)
        {
            internal Il2CppSystem.Collections.Generic.List<UpdaterV2.serverInfo> Servers
            {
                get => updater.OJKJNHPHFFO;
                set => updater.OJKJNHPHFFO = value;
            }
        }

        extension(CheckForUpdatesManager manager)
        {
            internal Il2CppReferenceArray<Text> ServerLabels => manager.KKCNCJAPANO;
            internal void RefreshServers() => manager.ANHGIKMEEFD();
        }

        extension(RagdollMode ragdoll)
        {
            internal MecanimControl Mecanim => ragdoll.HCOEAICIGEB;
            internal Il2CppReferenceArray<SkinnedMeshRenderer> VisualRenderers => ragdoll.JCGAJPLLOLC;
            internal Il2CppReferenceArray<SkinnedMeshRenderer> PhysicalRenderers => ragdoll.EGDJFJMGKOF;
        }

        extension(DeadBot corpse)
        {
            internal MecanimControl Mecanim => corpse.POAAPMGDFEG;
            internal Il2CppStringArray DeathAnimations => corpse.HJPBPFHIIMB;
            internal float DestroyDelay => corpse.CPBLCBMLFAJ;
        }

        extension(WaitForDestroy timer)
        {
            internal float Delay => timer.PHADIEJAKDA;
        }

        extension(MecanimControl mecanim)
        {
            internal void Play(string animation, float blend) => mecanim.KJEDCAONLCG(animation, blend);
        }

        extension(DrawPlayerName label)
        {
            internal PlayerNetworkController Player => label.JGOGBDGJKAG;

            internal string DisplayName
            {
                get => label.DJHKBCAIADF;
                set => label.DJHKBCAIADF = value;
            }
        }

        extension(WeaponScript weapon)
        {
            internal Vector3 RestPosition => weapon.KKCIIEDNLEO;
        }

        extension(UI_Button button)
        {
            internal Volume Volume => button.COIGCEOALIN;
        }
    }

    internal static class RoomPropertiesMembers
    {
        extension(RoomProperties)
        {
            internal static RoomProperties Parse(string data) => RoomProperties.HFBENCJFGPO(data);
        }
    }

    internal static class NetworkSpawner
    {
        internal static NetworkObject Spawn(string resource, Vector3 position, Quaternion rotation, NetworkConnection owner = null) =>
            CPPBDGGKHCB.FONDGACBGAB(resource, position, rotation, owner);
        internal static void Despawn(GameObject target) => CPPBDGGKHCB.OAFNAAMPNHG(target);
    }
}
