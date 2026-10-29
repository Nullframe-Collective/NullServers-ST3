using Il2CppFishNet.Connection;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Players;
using NullServers.Server;
using System.Text;
using UnityEngine;

namespace NullServers.Content
{
    internal sealed class ContentServer
    {
        private sealed class Range
        {
            internal string Path, Pack, Hash, File;
            internal long Offset, FileLength, Sent;
            internal int Length;
            internal DateTime Written;
        }

        private sealed class Peer
        {
            internal IntPtr Connection;
            internal readonly TrafficBudget Budget = new(512 * 1024, 64 * 1024);
            internal readonly Queue<Range> Ranges = new();
        }

        private const int MaxQueuedRanges = 8;

        private readonly RelaySession _session;
        private readonly ServerControl _control;
        private readonly PlayerProfiles _profiles;
        private readonly Dictionary<int, Peer> _peers = new();
        private readonly Dictionary<string, (long Length, DateTime Written, string Sha256)> _hashes = new(StringComparer.OrdinalIgnoreCase);
        private TrafficBudget _total = new(1024 * 1024, 128 * 1024);
        private string _room;

        internal ContentServer(RelaySession session, ServerControl control, PlayerProfiles profiles)
        {
            _session = session;
            _control = control;
            _profiles = profiles;
            profiles.ContentReceived += Receive;
        }

        internal void Update()
        {
            string room = _control.CanManage ? _control.Room.Id : null;
            if (room != _room)
            {
                _room = room;
                _peers.Clear();
                _total = new TrafficBudget(1024 * 1024, 128 * 1024);
            }

            if (room == null) return;

            float now = Time.unscaledTime;
            foreach (var pair in _peers.ToArray())
            {
                if (!_control.Manager.ServerManager.Clients.TryGetValue(pair.Key, out var connection) || connection.Pointer != pair.Value.Connection)
                {
                    _peers.Remove(pair.Key);
                    continue;
                }

                Serve(connection, pair.Value, now);
            }
        }

        private void Serve(NetworkConnection connection, Peer peer, float now)
        {
            while (peer.Ranges.TryPeek(out Range range))
            {
                ContentMessage chunk = Message(ContentStage.Chunk, range.Pack, range.Hash) with
                {
                    File = range.File,
                    Offset = range.Offset + range.Sent,
                    Data = ""
                };

                int header = Encoding.UTF8.GetByteCount(chunk.Serialize());
                int fits = Math.Min(ContentMessage.ChunkSize, (_profiles.PacketRoom - header) / 4 * 3);

                if (fits <= 0)
                {
                    peer.Ranges.Clear();
                    Send(connection, Failed(range.Pack, range.Hash, "The host's network packets are too small for downloads."));
                    return;
                }

                int size = (int)Math.Min(fits, range.Length - range.Sent);
                int cost = header + (size + 2) / 3 * 4;
                if (!peer.Budget.CanSpend(cost, now) || !_total.CanSpend(cost, now)) return;

                FileInfo info = new(range.Path);
                if (!info.Exists || info.Length != range.FileLength || info.LastWriteTimeUtc != range.Written)
                {
                    peer.Ranges.Clear();
                    Send(connection, Failed(range.Pack, range.Hash, "The host's pack changed during the download."));
                    return;
                }

                byte[] data = new byte[size];
                try
                {
                    using FileStream stream = new(range.Path, FileMode.Open, FileAccess.Read, FileShare.Read);
                    stream.Position = range.Offset + range.Sent;
                    int read = 0;

                    while (read < size)
                    {
                        int count = stream.Read(data, read, size - read);
                        if (count == 0) throw new IOException("Unexpected end of file.");

                        read += count;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    peer.Ranges.Clear();
                    Send(connection, Failed(range.Pack, range.Hash, "The host could not read its pack."));
                    return;
                }

                peer.Budget.Spend(cost, now);
                _total.Spend(cost, now);
                Send(connection, chunk with { Data = Convert.ToBase64String(data) });

                range.Sent += size;
                if (range.Sent >= range.Length)
                {
                    peer.Ranges.Dequeue();
                }
            }
        }

        private void Receive(NetworkConnection connection, ContentMessage message)
        {
            if (!_control.CanManage || message.Room != _room || !connection.IsValid || !connection.IsAuthenticated ||
                !_control.Manager.ServerManager.Clients.TryGetValue(connection.ClientId, out var current) || current.Pointer != connection.Pointer)
            {
                return;
            }

            if (!_peers.TryGetValue(connection.ClientId, out var peer) || peer.Connection != connection.Pointer)
            {
                _peers[connection.ClientId] = peer = new Peer { Connection = connection.Pointer };
            }

            _control.Compatibility?.KeepAlive(connection.ClientId);

            if (!TryFindPack(message.Pack, message.Hash, out PackKind kind, out string directory))
            {
                Send(connection, Failed(message.Pack, message.Hash, "The host does not offer this pack."));
                return;
            }

            if (message.Stage == ContentStage.ListFiles)
            {
                try
                {
                    Send(connection, Message(ContentStage.Files, message.Pack, message.Hash) with { Files = ListFiles(kind, directory) });
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    Send(connection, Failed(message.Pack, message.Hash, "The host could not read its pack."));
                }
            }
            else if (message.Stage == ContentStage.Range)
            {
                Enqueue(connection, peer, kind, directory, message);
            }
        }

        private void Enqueue(NetworkConnection connection, Peer peer, PackKind kind, string directory, ContentMessage message)
        {
            if (!PackFiles.Allowed(kind).Contains(message.File) || peer.Ranges.Count >= MaxQueuedRanges) return;

            string path = Path.Combine(directory, message.File);
            FileInfo info = new(path);

            if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || message.Offset + message.Length > info.Length)
            {
                Send(connection, Failed(message.Pack, message.Hash, "Invalid file range."));
                return;
            }

            peer.Ranges.Enqueue(new Range
            {
                Path = path,
                Pack = message.Pack,
                Hash = message.Hash,
                File = message.File,
                Offset = message.Offset,
                Length = message.Length,
                FileLength = info.Length,
                Written = info.LastWriteTimeUtc
            });
        }

        private ContentFile[] ListFiles(PackKind kind, string directory)
        {
            List<ContentFile> files = new();
            foreach (string name in PackFiles.Allowed(kind))
            {
                FileInfo info = new(Path.Combine(directory, name));
                if (!info.Exists || (info.Attributes & FileAttributes.ReparsePoint) != 0 || info.Length > PackFiles.Limit(name)) continue;

                if (!_hashes.TryGetValue(info.FullName, out var cached) || cached.Length != info.Length || cached.Written != info.LastWriteTimeUtc)
                {
                    cached = (info.Length, info.LastWriteTimeUtc, PackFiles.Sha256(info.FullName));
                    _hashes[info.FullName] = cached;
                }

                files.Add(new ContentFile(name, info.Length, cached.Sha256));
            }

            return files.ToArray();
        }

        private bool TryFindPack(string id, string hash, out PackKind kind, out string directory)
        {
            kind = default;
            directory = null;
            FrameworkManifest offered = _session.Content;

            foreach (var (assets, addon, packKind) in new[]
            {
                (offered.Weapons, Frameworks.Weapons, PackKind.Weapon),
                (offered.Customization, Frameworks.Customization, PackKind.Cosmetic),
                (offered.NPCAssets, Frameworks.NPCs, PackKind.NPC)
            })
            {
                if (addon == null || !assets.Any(asset => asset.Id == id && asset.Hash == hash)) continue;

                object item = addon.Installed.FirstOrDefault(value => addon.Text(value, "Id") == id && addon.Text(value, "ContentHash") == hash);
                directory = item == null ? null : addon.Text(item, "Path");
                kind = packKind;
                return directory != null && Directory.Exists(directory);
            }

            return false;
        }

        private ContentMessage Message(ContentStage stage, string pack, string hash) => new(1, _room, stage, pack, hash);

        private ContentMessage Failed(string pack, string hash, string error) => Message(ContentStage.Failed, pack, hash) with { Error = error };

        private void Send(NetworkConnection connection, ContentMessage message) => _profiles.SendContent(message, connection);
    }
}
