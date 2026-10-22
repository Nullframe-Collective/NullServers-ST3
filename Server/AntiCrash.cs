using HarmonyLib;
using Il2Cpp;
using Il2CppFishNet.Connection;
using Il2CppFishNet.Managing;
using Il2CppFishNet.Managing.Server;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using Il2CppInterop.Runtime;
using MelonLoader;
using NullServers.Network;
using UnityEngine;

namespace NullServers.Server
{
    internal sealed class AntiCrash
    {
        private enum Rejection
        {
            Malformed,
            Flood,
            Prohibited,
            Throttled
        }

        private sealed class Client
        {
            internal NetworkConnection Connection;
            internal string Account;
            internal string Nickname;
            internal RpcBudget Budget;
            internal readonly Dictionary<int, NetworkObject> Objects = new();
            internal string PendingBan;
            internal bool Removed;
            internal float NextLog;
        }

        private sealed record Frame(PooledReader Reader, Client Client, int End, bool Reliable)
        {
            internal bool ExpensiveSpawn;
            internal bool ConsoleSpawn;
        }

        [ThreadStatic] private static Frame _frame;
        private static AntiCrash _instance;
        private readonly ServerControl _control;
        private readonly MelonLogger.Instance _logger;
        private readonly Dictionary<int, Client> _clients = new();
        private readonly Dictionary<IntPtr, RpcCategory> _categories = new();
        private readonly Queue<Client> _pending = new();
        private bool _active;
        private string _roomId;
        private float _nextMaintenance;

        internal int Blocked { get; private set; }
        internal int Removed { get; private set; }

        internal AntiCrash(HarmonyLib.Harmony harmony, ServerControl control, MelonLogger.Instance logger)
        {
            _instance = this;
            _control = control;
            _logger = logger;

            harmony.Patch(
                AccessTools.Method(typeof(ServerObjects), nameof(ServerObjects.ParseServerRpc)),
                prefix: First(nameof(BeforeRpc)),
                finalizer: new HarmonyMethod(typeof(AntiCrash), nameof(AfterPacket)));

            harmony.Patch(
                AccessTools.Method(typeof(ServerManager), nameof(ServerManager.ParseBroadcast)),
                prefix: First(nameof(BeforeBroadcast)),
                finalizer: new HarmonyMethod(typeof(AntiCrash), nameof(AfterPacket)));

            harmony.Patch(
                AccessTools.Method(typeof(NetworkBehaviour), nameof(NetworkBehaviour.ReadServerRpc)),
                prefix: First(nameof(BeforeMethod)));

            harmony.Patch(
                AccessTools.Method(typeof(Reader), nameof(Reader.IsPossibleAllocationAttack)),
                postfix: new HarmonyMethod(typeof(AntiCrash), nameof(AfterAllocationCheck)));

            harmony.Patch(
                AccessTools.Method
                (
                    typeof(CPPBDGGKHCB),
                    nameof(CPPBDGGKHCB.FONDGACBGAB),
                    [
                        typeof(GameObject),
                        typeof(Vector3),
                        typeof(Quaternion),
                        typeof(NetworkConnection)
                    ]
                ),
                prefix: First(nameof(BeforeSpawnPrefab)),
                postfix: new HarmonyMethod(typeof(AntiCrash), nameof(AfterSpawned)),
                finalizer: new HarmonyMethod(typeof(AntiCrash), nameof(AfterSpawnPrefab)));

            harmony.Patch(
                AccessTools.Method(typeof(RelayTransport), nameof(RelayTransport.CanSpawnNetworkObject)),
                prefix: First(nameof(BeforeObjectSpawn)));
                PatchReader(harmony, typeof(MultiplayerChat), nameof(MultiplayerChat.NKOMILAFHFN), nameof(CheckChat));
                PatchReader(harmony, typeof(SyncCustomization), nameof(SyncCustomization.LAEBMFBEDKM), nameof(CheckAppearance));
                PatchReader(harmony, typeof(Volume), nameof(Volume.AKLPGOIKAHF), nameof(CheckSpawn));
                PatchReader(harmony, typeof(PlayerNetworkController), nameof(PlayerNetworkController.EFIBFBEGIOE), nameof(CheckPlayerState));

            foreach (string method in new[]
            {
                nameof(PlayerNetworkController.DGMPNAIOHNG),
                nameof(PlayerNetworkController.FKGFMBJEFAM),
                nameof(PlayerNetworkController.PPFKMGCCMKI)
            })
            {
                PatchReader(harmony, typeof(PlayerNetworkController), method, nameof(CheckStringFloat));
            }
        }

        private static HarmonyMethod First(string name) => new(typeof(AntiCrash), name)
        {
            priority = Priority.First
        };

        private static void PatchReader(HarmonyLib.Harmony harmony, Type type, string method, string check) => harmony.Patch
        (
            AccessTools.Method(type, method),
            prefix: First(check)
        );

        internal void Update()
        {
            _active = _control.CanManage;

            string room = _active ? _control.Room.Id : null;
            if (_roomId != room)
            {
                _roomId = room;
                _clients.Clear();
                _pending.Clear();
                Blocked = Removed = 0;
            }

            if (!_active) return;

            while (_pending.TryDequeue(out var blocked))
            {
                if 
                (
                    !blocked.Removed &&
                    _control.RemoveAbusivePlayer
                    (
                        blocked.Connection,
                        blocked.Account,
                        blocked.Nickname,
                        blocked.PendingBan
                    )
                )
                {
                    blocked.Removed = true;
                    Removed++;
                    int cleaned = Cleanup(blocked);
                    _logger?.Warning($"Anti-Crash cleanup for client {blocked.Connection.ClientId}: removed {cleaned} network objects; current round preserved.");
                }
            }

            if (Time.unscaledTime < _nextMaintenance) return;

            _nextMaintenance = Time.unscaledTime + 1f;

            foreach (var pair in _clients.ToArray())
            {
                Client client = pair.Value;
                if 
                (
                    !_control.Manager.ServerManager.Clients.TryGetValue(pair.Key, out var current) ||
                    current.Pointer != client.Connection.Pointer ||
                    !current.IsValid
                )
                {
                    _clients.Remove(pair.Key);
                    continue;
                }

                foreach (var entry in client.Objects.ToArray())
                {
                    if (!entry.Value || !entry.Value.IsServerInitialized || entry.Value.ObjectId != entry.Key)
                    {
                        client.Objects.Remove(entry.Key);
                    }
                }
            }
        }

        private int Cleanup(Client client)
        {
            var objects = _control.Manager.ServerManager.Objects.Spawned;
            int removed = 0;

            foreach (var entry in client.Objects)
            {
                if 
                (
                    !objects.TryGetValue(entry.Key, out var current) ||
                    !current ||
                    current.Pointer != entry.Value.Pointer ||
                    current.IsSceneObject
                )
                {
                    continue;
                }

                CPPBDGGKHCB.OAFNAAMPNHG(current.gameObject);
                removed++;
            }

            client.Objects.Clear();

            return removed;
        }

        private Client For(NetworkConnection connection)
        {
            if (!_active || connection == null || connection.ClientId == 0 || !connection.IsAuthenticated || !connection.IsValid)
            {
                return null;
            }

            if (_clients.TryGetValue(connection.ClientId, out var client) && client.Connection.Pointer == connection.Pointer)
            {
                return client;
            }

            if (!_control.Manager.ServerManager.Clients.TryGetValue(connection.ClientId, out var current) ||
                current.Pointer != connection.Pointer)
            {
                return null;
            }

            _control.TryIdentity(connection.ClientId, out var identity);

            return _clients[connection.ClientId] = new Client
            {
                Connection = connection,
                Account = identity != null && !identity.IsGuest ? identity.Username : null,
                Budget = new RpcBudget(Time.unscaledTime)
            };
        }

        private static bool Data(PooledReader reader, out ReadOnlySpan<byte> data)
        {
            data = default;

            var buffer = reader?._buffer;
            if 
            (
                buffer == null ||
                reader.Position < 0 ||
                reader.Remaining < 0 ||
                reader.Position > buffer.Length - reader.Remaining
            )
            {
                return false;
            }

            data = buffer.AsSpan().Slice(reader.Position, reader.Remaining);

            return true;
        }

        private static bool Purge(PooledReader reader)
        {
            reader.Skip(Math.Max(0, reader.Remaining));
            return false;
        }

        private bool Reject(Client client, PooledReader reader, string reason, Rejection rejection)
        {
            Blocked++;

            float now = Time.unscaledTime;
            bool queueBan = rejection switch
            {
                Rejection.Malformed => client.Budget.Malformed(now),
                Rejection.Flood => client.Budget.Flood(now),
                Rejection.Prohibited => true,
                _ => false
            };

            if (queueBan && client.PendingBan == null)
            {
                if 
                (
                    client.Account == null &&
                    _control.TryIdentity(client.Connection.ClientId, out var identity) &&
                    !identity.IsGuest
                )
                {
                    client.Account = identity.Username;
                }

                client.Nickname = _control.PlayerName(client.Connection.ClientId, null);
                client.PendingBan = reason;
                _pending.Enqueue(client);
                _logger.Warning($"Anti-Crash queued ban for client {client.Connection.ClientId}: {reason}.");
            }

            if (now >= client.NextLog)
            {
                client.NextLog = now + 5f;
                string action = rejection == Rejection.Throttled ? "throttled" : "blocked";
                _logger.Warning($"Anti-Crash {action} client {client.Connection.ClientId}: {reason}.");
            }

            if 
            (
                rejection == Rejection.Throttled &&
                _frame is { Reliable: true } frame &&
                frame.Reader.Pointer == reader.Pointer &&
                frame.End >= reader.Position &&
                frame.End - reader.Position <= reader.Remaining
            )
            {
                reader.Skip(frame.End - reader.Position);
                
                return false;
            }

            return Purge(reader);
        }

        private static bool BeforeRpc(PooledReader reader, NetworkConnection conn, Channel channel, out Frame __state)
        {
            __state = _frame;

            AntiCrash instance = _instance;
            Client client = instance.For(conn);
            if (client == null) return true;

            if (client.PendingBan != null)
            {
                return Purge(reader);
            }

            if (!Data(reader, out var data) || !RpcPacket.TryHeader(data, channel == Channel.Reliable, out var header))
            {
                return instance.Reject(client, reader, "invalid RPC header or length", Rejection.Malformed);
            }

            if (!client.Budget.AllowPacket(Time.unscaledTime, header.PayloadOffset + header.PayloadLength))
            {
                return instance.Reject(client, reader, "RPC traffic limit", Rejection.Flood);
            }

            if 
            (
                !instance._control.Manager.ServerManager.Objects.Spawned.TryGetValue(header.ObjectId, out var target) ||
                !target ||
                header.Component >= target.NetworkBehaviours.Count
            )
            {
                instance.Blocked++;

                if (channel == Channel.Reliable)
                {
                    reader.Skip(header.PayloadOffset + header.PayloadLength);
                }
                else
                {
                    Purge(reader);
                }

                return false;
            }

            _frame = new Frame
            (
                reader,
                client,
                reader.Position + header.PayloadOffset + header.PayloadLength,
                channel == Channel.Reliable
            );

            return true;
        }

        private static bool BeforeBroadcast(PooledReader reader, NetworkConnection conn, out Frame __state)
        {
            __state = _frame;

            AntiCrash instance = _instance;
            Client client = instance.For(conn);
            if (client == null) return true;

            if (client.PendingBan != null)
            {
                return Purge(reader);
            }

            if (!Data(reader, out var data) || !RpcPacket.TryBroadcast(data, out var length))
            {
                return instance.Reject(client, reader, "invalid broadcast length", Rejection.Malformed);
            }

            if 
            (
                !client.Budget.AllowPacket(Time.unscaledTime, length) ||
                !client.Budget.AllowMethod(Time.unscaledTime, IntPtr.Zero, 0, RpcCategory.Broadcast)
            )
            {
                return instance.Reject(client, reader, "broadcast traffic limit", Rejection.Flood);
            }

            _frame = new Frame(reader, client, reader.Position + data.Length, true);

            return true;
        }

        private static bool BeforeMethod(NetworkBehaviour __instance, PooledReader reader)
        {
            Frame frame = _frame;
            if (frame == null || frame.Reader.Pointer != reader.Pointer)
            {
                return true;
            }

            AntiCrash instance = _instance;
            byte size = __instance._rpcHashSize;
            int remaining = frame.End - reader.Position;
            if (size is not (1 or 2) || remaining < size || !Data(reader, out var data))
            {
                return instance.Reject(frame.Client, reader, "truncated RPC method", Rejection.Malformed);
            }

            uint hash = (uint)(size == 1 ? data[0] : data[0] | data[1] << 8);
            if (!__instance._serverRpcDelegates.ContainsKey(hash))
            {
                return instance.Reject(frame.Client, reader, "unregistered RPC method", Rejection.Malformed);
            }

            IntPtr type = IL2CPP.il2cpp_object_get_class(__instance.Pointer);
            if (!instance._categories.TryGetValue(type, out var category))
            {
                instance._categories[type] = category = 
                    __instance.TryCast<MultiplayerChat>() ? RpcCategory.Chat :
                    __instance.TryCast<SyncCustomization>() ? RpcCategory.Appearance : 
                    __instance.TryCast<Volume>() ? RpcCategory.Spawn :
                    __instance.TryCast<WhoKilledWho>() ? RpcCategory.Notification : RpcCategory.General;
            }

            if (!frame.Client.Budget.AllowMethod(Time.unscaledTime, type, category == RpcCategory.General ? hash : 0, category))
            {
                return instance.Reject
                (
                    frame.Client,
                    reader,
                    "RPC method rate limit",
                    category == RpcCategory.Spawn ? Rejection.Throttled : Rejection.Flood
                );
            }

            frame.ExpensiveSpawn = category == RpcCategory.Spawn || __instance.TryCast<RoomMultiplayerMenu>();

            if 
            (
                category == RpcCategory.Notification &&
                !RpcPacket.ValidBody
                (
                    data.Slice(size, remaining - size),
                    RpcSchema.Notification,
                    frame.Reliable
                )
            )
            {
                return instance.Reject(frame.Client, reader, "invalid notification data", Rejection.Malformed);
            }

            return true;
        }

        private static bool Check(PooledReader reader, RpcSchema schema)
        {
            Frame frame = _frame;
            if (frame == null || frame.Reader.Pointer != reader.Pointer)
            {
                return true;
            }

            int length = frame.End - reader.Position;
            return 
                Data(reader, out var data) &&
                length >= 0 &&
                length <= data.Length &&
                RpcPacket.ValidBody(data.Slice(0, length), schema, frame.Reliable) ||
                _instance.Reject(frame.Client, reader, "invalid " + schema + " data", Rejection.Malformed);
        }

        private static bool CheckChat(PooledReader __0) => Check(__0, RpcSchema.Chat);

        private static bool CheckAppearance(PooledReader __0) => Check(__0, RpcSchema.Appearance);

        private static bool CheckSpawn(PooledReader __0)
        {
            if (!Check(__0, RpcSchema.Spawn)) return false;

            if (_frame?.Reader.Pointer == __0.Pointer)
            {
                _frame.ConsoleSpawn = true;
            }

            return true;
        }

        private static bool CheckPlayerState(PooledReader __0) => Check(__0, RpcSchema.PlayerState);

        private static bool CheckStringFloat(PooledReader __0) => Check(__0, RpcSchema.StringFloat);

        private static void AfterAllocationCheck(Reader __instance, bool __result)
        {
            if (__result && _frame?.Reader.Pointer == __instance.Pointer)
            {
                _instance.Reject(_frame.Client, _frame.Reader, "invalid allocation size", Rejection.Malformed);
            }
        }

        private static bool BeforeSpawnPrefab(GameObject __0, out bool __state)
        {
            __state = _frame?.ExpensiveSpawn == true;
            if (_frame == null || !__0) return true;

            if (_frame.Client.PendingBan != null) return false;

            if 
            (
                __0.GetComponentInChildren(Il2CppType.Of<RoomMultiplayerMenu>(), true) ||
                __0.GetComponentInChildren(Il2CppType.Of<NetBootstrap>(), true) ||
                __0.GetComponentInChildren(Il2CppType.Of<NetworkManager>(), true)
            )
            {
                return _instance.Reject
                (
                    _frame.Client,
                    _frame.Reader,
                    "forbidden network prefab: " + __0.name,
                    Rejection.Prohibited
                );
            }

            _frame.ExpensiveSpawn |= 
                __0.GetComponent(Il2CppType.Of<PlayerNetworkController>()) ||
                __0.GetComponent(Il2CppType.Of<Bot>()) ||
                __0.GetComponent(Il2CppType.Of<BossBot>());

            return true;
        }

        private static void AfterSpawned(NetworkObject __result)
        {
            if (_frame != null && __result && __result.IsServerInitialized && !__result.IsSceneObject)
            {
                _frame.Client.Objects[__result.ObjectId] = __result;
            }
        }

        private static void AfterSpawnPrefab(bool __state)
        {
            if (_frame != null)
            {
                _frame.ExpensiveSpawn = __state;
            }
        }

        private static bool BeforeObjectSpawn(ref bool __result)
        {
            Frame frame = _frame;
            if (frame == null) return true;

            if 
            (
                frame.Client.PendingBan != null ||
                !frame.Client.Budget.AllowObject
                (
                    Time.unscaledTime,
                    frame.ExpensiveSpawn,
                    frame.ConsoleSpawn
                )
            )
            {
                _instance.Reject(frame.Client, frame.Reader, "network object spawn limit", Rejection.Throttled);
                __result = false;

                return false;
            }

            return true;
        }

        private static Exception AfterPacket(Exception __exception, Frame __state)
        {
            _frame = __state;

            return __exception;
        }
    }
}
