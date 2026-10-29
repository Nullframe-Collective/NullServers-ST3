using Il2Cpp;
using Il2CppFishNet.Transporting;
using Il2CppFishNet.Transporting.Relay;
using MelonLoader;
using MelonLoader.Utils;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Players;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NullServers.Content
{
    internal enum DownloadState
    {
        Queued,
        Listing,
        Receiving,
        Checking,
        Ready
    }

    internal sealed class DownloadJob
    {
        internal PackKind Kind;
        internal FrameworkAsset Asset;
        internal DownloadState State;
        internal ContentFile[] Files;
        internal int FileIndex;
        internal long FileWritten, FileRequested, Size, Received;
        internal FileStream Output;
        internal string Partial, Final, Cached, Source;
        internal Task<string> Check;

        internal string Name => Asset.DisplayName;
    }

    internal sealed class ContentDownloads
    {
        private const float Timeout = 20f;
        private const int Window = 2;
        private const int RangeSize = 32 * 1024;

        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly MelonLogger.Instance _logger;
        private readonly string _cache = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "Downloads");
        private readonly List<DownloadJob> _jobs = new();
        private readonly HashSet<string> _consent = new(StringComparer.Ordinal);
        private string _consentRoom;
        private float _consentUntil;
        private float _requestedAt = -1f;
        private RelayTransport _requestedRelay;
        private bool _reconnecting;
        private bool _joining;
        private string _room;
        private float _lastMessage;
        private float _sampleAt;
        private long _sampleBytes;
        private bool _completed;
        private string _offerRoom;
        private float _offerPing;
        private bool _accepted;

        internal IReadOnlyList<DownloadJob> Jobs => _jobs;
        internal AddonPack[] Offered { get; private set; }
        internal bool Busy => _jobs.Count > 0 && !_completed;
        internal bool Connecting => _requestedAt >= 0f && _jobs.Count == 0 && !_completed && _session.AddonNotice == null;
        internal bool Visible => Busy || Connecting || Offered != null;
        internal long Total => _jobs.Sum(job => job.Size);
        internal long Received => _jobs.Sum(job => job.Received);
        internal double Speed { get; private set; }
        internal event Action Requested;

        internal ContentDownloads(RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _session = session;
            _profiles = profiles;
            _logger = logger;
            profiles.ContentReceivedFromHost += Receive;
        }

        internal static bool Downloadable(AddonPack pack) => Problem(pack) == null;

        private static FrameworkAddon Addon(PackKind kind) => kind switch
        {
            PackKind.Weapon => Frameworks.Weapons,
            PackKind.Cosmetic => Frameworks.Customization,
            _ => Frameworks.NPCs
        };

        private static string Problem(AddonPack pack)
        {
            if (pack.Hash == null || PackFiles.KindOf(pack.Folder) is not { } kind)
            {
                return pack.Name + " cannot be downloaded from the host yet.";
            }

            if (Addon(kind) is not { } addon)
            {
                return "The framework for " + pack.Name + " is not available.";
            }

            if (!Frameworks.Settings.Enabled(addon.Requirement.Id))
            {
                return addon.Requirement.Id + " is disabled in ADDONS.";
            }

            return addon.Catalog.GetMethod("Mount") == null
                ? "Your " + addon.Requirement.Id + " " + addon.Requirement.Version + " is too old to download packs from the host. Update it from our Discord."
                : null;
        }

        internal static string Blocker(AddonNotice notice)
        {
            if (notice is not { Kind: AddonNoticeKind.Content, Missing.Length: 0, Packs.Length: > 0 })
            {
                return "These packs cannot be downloaded from the host.";
            }

            return notice.Packs.Select(Problem).FirstOrDefault(problem => problem != null);
        }

        internal static bool CanFetch(AddonNotice notice) => Blocker(notice) == null;

        internal void Consent(string room, IEnumerable<AddonPack> packs)
        {
            _consentRoom = room;
            _consentUntil = Time.unscaledTime + 600f;
            _requestedAt = Time.unscaledTime;
            _requestedRelay = _session.Relay;
            _reconnecting = false;
            _joining = false;
            _consent.Clear();
            _consent.UnionWith(packs.Select(pack => pack.Id + ":" + pack.Hash));
            Requested?.Invoke();
        }

        internal bool Ask(FrameworkManifest manifest)
        {
            if (Busy || _completed) return false;

            string room = _session.Relay ? _session.Relay.CurrentRoom?.Id : null;
            AddonPack[] missing = manifest.PackDifferences(FrameworkContent.Create(manifest).For(manifest));
            if (room == null || missing.Length == 0 || !missing.All(Downloadable)) return false;
            if (Offered != null && _offerRoom == room) return true;

            Offered = missing;
            _offerRoom = room;
            _offerPing = 0f;
            _logger.Debug($"The room needs {missing.Length} pack(s) from the host. Waiting for download approval.");
            Requested?.Invoke();
            return true;
        }

        internal void Accept()
        {
            if (Offered == null) return;

            Consent(_offerRoom, Offered);
            Offered = null;
            _accepted = true;
        }

        internal bool TakeAccepted()
        {
            bool accepted = _accepted;
            _accepted = false;
            return accepted;
        }

        internal void Decline()
        {
            _logger.Debug("Room content download declined. Leaving the room.");
            Cancel();
        }

        private void KeepOffer()
        {
            if (Offered == null) return;

            string room = _session.Relay ? _session.Relay.CurrentRoom?.Id : null;
            if (room != _offerRoom)
            {
                Offered = null;
                _offerRoom = null;
                return;
            }

            if (Time.unscaledTime < _offerPing) return;

            _offerPing = Time.unscaledTime + 8f;
            _profiles.SendContent(new ContentMessage(1, room, ContentStage.ListFiles, Offered[0].Id, Offered[0].Hash), toServer: true);
        }

        internal void CancelRequest()
        {
            _requestedAt = -1f;
            _requestedRelay = null;
            _reconnecting = false;
            _joining = false;
        }

        private void Connect()
        {
            if (_requestedAt < 0f) return;

            RelayTransport relay = _session.Relay;
            if (!_session.IsNullFrame || _session.IsSwitching || !_session.CommunityAccepted ||
                !relay || relay != _requestedRelay || _session.AddonNotice != null)
            {
                CancelRequest();
                return;
            }

            if (Time.unscaledTime - _requestedAt >= 45f)
            {
                Fail("Could not connect to the host to download the room content. Please try again.");
                return;
            }

            if (relay.CurrentRoom != null)
            {
                if (_joining && relay.CurrentRoom.Id != _consentRoom)
                {
                    CancelRequest();
                }
                else if (_joining && _profiles.Compatibility?.Ready == true)
                {
                    CancelRequest();
                }
                return;
            }

            if (_joining) return;

            NetBootstrap bootstrap = NetBootstrap.Current;
            if (!bootstrap || bootstrap.Network.ClientManager.Started || bootstrap.Network.SceneManager.IteratingQueue ||
                SceneManager.GetActiveScene().name != bootstrap._menuScene)
            {
                return;
            }

            if (!relay.IsConnectedToRelay)
            {
                if (!_reconnecting && relay.ClientTransport.GetConnectionState(false) == LocalConnectionState.Stopped)
                {
                    _reconnecting = true;
                    _logger.Debug("Reconnecting to NullFrame Servers to download the approved room content.");
                    relay.ConnectToRelay();
                }
                return;
            }

            if (!_session.CanJoin) return;

            _joining = true;
            if (!bootstrap.JoinRoom(_consentRoom))
            {
                if (_session.AddonNotice != null)
                {
                    CancelRequest();
                }
                else
                {
                    Fail("Could not join the room to download its packs. The room may have closed.");
                }
            }
        }

        internal bool Covers(string room, AddonNotice notice) =>
            CanFetch(notice) && Consented(room, notice.Packs);

        private bool Consented(string room, IEnumerable<AddonPack> packs) =>
            room == _consentRoom && Time.unscaledTime < _consentUntil && packs.All(pack => _consent.Contains(pack.Id + ":" + pack.Hash));

        internal bool Prepare(FrameworkManifest manifest)
        {
            if (Busy) return true;
            if (_completed) return false;

            _requestedAt = -1f;

            AddonPack[] missing = manifest.PackDifferences(FrameworkContent.Create(manifest).For(manifest));
            string room = _session.Relay ? _session.Relay.CurrentRoom?.Id : null;

            if (missing.Length == 0 || room == null || !missing.All(Downloadable) || !Consented(room, missing))
            {
                return false;
            }

            _room = room;
            foreach (AddonPack pack in missing)
            {
                PackKind kind = PackFiles.KindOf(pack.Folder).Value;
                FrameworkAsset asset = manifest.Weapons.Concat(manifest.Customization).Concat(manifest.NPCAssets)
                    .First(value => value.Id == pack.Id && value.Hash == pack.Hash);
                string name = kind.ToString().ToLowerInvariant() + "-" + pack.Hash;
                DownloadJob job = new()
                {
                    Kind = kind,
                    Asset = asset,
                    Partial = Path.Combine(_cache, ".partial-" + name),
                    Cached = Path.Combine(_cache, name),
                    Final = Target(kind, asset)
                };

                DeletePartial(job);
                string existing = Directory.Exists(job.Final) ? job.Final : Directory.Exists(job.Cached) ? job.Cached : null;
                if (existing != null)
                {
                    StartCheck(job, existing);
                }

                _jobs.Add(job);
            }

            _lastMessage = _sampleAt = Time.unscaledTime;
            _logger.Debug($"Downloading {_jobs.Count} pack(s) from the host.");
            return true;
        }

        internal bool TakeCompleted()
        {
            if (!_completed || _jobs.Count == 0) return false;

            _jobs.Clear();
            return true;
        }

        internal void Update()
        {
            Connect();
            KeepOffer();

            string room = _session.Relay ? _session.Relay.CurrentRoom?.Id : null;
            if (_room != null && room != _room)
            {
                Reset();
                return;
            }

            if (!Busy) return;

            float now = Time.unscaledTime;
            if (now - _sampleAt >= 0.5f)
            {
                long received = Received;
                Speed = Speed * 0.6 + (received - _sampleBytes) / (now - _sampleAt) * 0.4;
                _sampleBytes = received;
                _sampleAt = now;
            }

            DownloadJob job = _jobs.FirstOrDefault(value => value.State != DownloadState.Ready);
            if (job == null)
            {
                Mount();
                return;
            }

            switch (job.State)
            {
                case DownloadState.Queued:
                    Request(job, ContentStage.ListFiles);
                    job.State = DownloadState.Listing;
                    _lastMessage = now;
                    break;
                case DownloadState.Receiving:
                    RequestRanges(job);
                    break;
                case DownloadState.Checking when job.Check.IsCompleted:
                    Finish(job);
                    return;
            }

            if (job.State is DownloadState.Listing or DownloadState.Receiving && now - _lastMessage > Timeout)
            {
                Fail("The host stopped sending " + job.Name + ".");
            }
        }

        private void Receive(ContentMessage message)
        {
            DownloadJob job = _jobs.FirstOrDefault(value => value.State is DownloadState.Listing or DownloadState.Receiving);
            if (job == null || message.Room != _room || message.Pack != job.Asset.Id || message.Hash != job.Asset.Hash) return;

            _lastMessage = Time.unscaledTime;
            try
            {
                switch (message.Stage)
                {
                    case ContentStage.Failed:
                        Fail("The host refused " + job.Name + ": " + message.Error);
                        break;
                    case ContentStage.Files when job.State == DownloadState.Listing:
                        Begin(job, message.Files);
                        break;
                    case ContentStage.Chunk when job.State == DownloadState.Receiving:
                        Write(job, message);
                        break;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
            {
                Fail(job.Name + ": " + exception.Message);
            }
        }

        private void Begin(DownloadJob job, ContentFile[] files)
        {
            string[] allowed = PackFiles.Allowed(job.Kind);
            if (files.Any(file => !allowed.Contains(file.Name) || file.Size > PackFiles.Limit(file.Name)) ||
                !files.Any(file => file.Name == PackFiles.Definition(job.Kind)))
            {
                throw new InvalidDataException("The host offered an invalid file list.");
            }

            job.Files = files;
            job.Size = files.Sum(file => file.Size);

            if (Total > PackFiles.DownloadLimit)
            {
                throw new InvalidDataException("The room content is larger than 1 GB.");
            }

            if (Directory.Exists(job.Partial))
            {
                Directory.Delete(job.Partial, true);
            }

            Directory.CreateDirectory(job.Partial);
            job.State = DownloadState.Receiving;
            OpenFile(job, 0);
        }

        private void OpenFile(DownloadJob job, int index)
        {
            job.Output?.Dispose();
            job.Output = null;
            job.FileIndex = index;
            job.FileWritten = job.FileRequested = 0;

            while (job.FileIndex < job.Files.Length && job.Files[job.FileIndex].Size == 0)
            {
                File.WriteAllBytes(Path.Combine(job.Partial, job.Files[job.FileIndex].Name), Array.Empty<byte>());
                job.FileIndex++;
            }

            if (job.FileIndex >= job.Files.Length)
            {
                StartCheck(job, job.Partial);
                return;
            }

            job.Output = new FileStream(Path.Combine(job.Partial, job.Files[job.FileIndex].Name), FileMode.CreateNew, FileAccess.Write);
        }

        private void RequestRanges(DownloadJob job)
        {
            ContentFile file = job.Files[job.FileIndex];
            while (job.FileRequested < file.Size && job.FileRequested - job.FileWritten < (long)Window * RangeSize)
            {
                int length = (int)Math.Min(RangeSize, file.Size - job.FileRequested);
                Request(job, ContentStage.Range, file.Name, job.FileRequested, length);
                job.FileRequested += length;
            }
        }

        private void Write(DownloadJob job, ContentMessage message)
        {
            ContentFile file = job.Files[job.FileIndex];
            byte[] data = Convert.FromBase64String(message.Data);
            if (message.File != file.Name || message.Offset != job.FileWritten || data.Length == 0 ||
                data.Length > ContentMessage.ChunkSize || job.FileWritten + data.Length > file.Size)
            {
                throw new InvalidDataException("The host sent data out of order.");
            }

            job.Output.Write(data, 0, data.Length);
            job.FileWritten += data.Length;
            job.Received += data.Length;

            if (job.FileWritten == file.Size)
            {
                OpenFile(job, job.FileIndex + 1);
            }
        }

        private void StartCheck(DownloadJob job, string directory)
        {
            job.State = DownloadState.Checking;
            job.Source = directory;
            PackKind kind = job.Kind;
            string hash = job.Asset.Hash;
            job.Check = Task.Run(() =>
            {
                try
                {
                    return PackFiles.Check(kind, directory, hash);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    return exception.Message;
                }
            });
        }

        private void Finish(DownloadJob job)
        {
            try
            {
                Publish(job);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                Fail(job.Name + ": " + exception.Message);
            }
        }

        private void Publish(DownloadJob job)
        {
            string problem = job.Check.Result;
            bool downloaded = job.Source == job.Partial;

            if (problem != null && downloaded)
            {
                Fail(job.Name + ": " + problem);
                return;
            }

            if (problem != null)
            {
                if (job.Source == job.Cached)
                {
                    Directory.Delete(job.Cached, true);
                }

                job.Source = null;
                job.Check = null;
                job.State = DownloadState.Queued;
                return;
            }

            if (job.Source != job.Final)
            {
                Install(job);
            }

            job.State = DownloadState.Ready;
            _logger.Msg($"{job.Name}: checks passed ({(downloaded ? job.Size + " bytes" : "already installed")}), {Relative(job.Final)}.");
        }

        private void Install(DownloadJob job)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(job.Final));

            foreach (string path in Conflicts(job.Kind, job.Asset).Append(job.Final).Distinct(StringComparer.OrdinalIgnoreCase).ToArray())
            {
                if (!Directory.Exists(path)) continue;

                string backup = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "Replaced", PackFiles.Folder(job.Kind),
                    Path.GetFileName(path) + " " + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                Directory.Move(path, backup);
                _logger.Msg($"Moved {Relative(path)} to {Relative(backup)}; the room uses a different version.");
            }

            Directory.Move(job.Source, job.Final);

            if (job.Source != job.Cached && Directory.Exists(job.Cached))
            {
                Directory.Delete(job.Cached, true);
            }
        }

        private static string Target(PackKind kind, FrameworkAsset asset)
        {
            const string Legacy = "legacy:";
            string root = Path.GetFullPath(Path.Combine(MelonEnvironment.UserDataDirectory, PackFiles.Folder(kind)));

            if (kind != PackKind.NPC && asset.Id.StartsWith(Legacy, StringComparison.Ordinal))
            {
                string name = asset.Id[Legacy.Length..];
                string folder = string.Equals(asset.Folder, name, StringComparison.OrdinalIgnoreCase) && PackFiles.SafeFolder(asset.Folder) ? asset.Folder : name;
                return Path.Combine(root, PackFiles.SafeFolder(folder) ? folder : asset.Id.Replace(':', '_'));
            }

            string baseName = PackFiles.SafeFolder(asset.DisplayName) ? asset.DisplayName :
                PackFiles.SafeFolder(asset.Folder) ? asset.Folder : asset.Id.Replace(':', '_');
            string[] owned = Conflicts(kind, asset).ToArray();
            string path = Path.Combine(root, baseName);

            for (int index = 2; Directory.Exists(path) && !owned.Contains(Path.GetFullPath(path), StringComparer.OrdinalIgnoreCase); index++)
            {
                path = Path.Combine(root, $"{baseName} ({index})");
            }

            return path;
        }

        private static IEnumerable<string> Conflicts(PackKind kind, FrameworkAsset asset)
        {
            FrameworkAddon addon = Addon(kind);
            string root = Path.GetFullPath(Path.Combine(MelonEnvironment.UserDataDirectory, PackFiles.Folder(kind)));

            foreach (object item in addon?.Installed ?? Enumerable.Empty<object>())
            {
                string path = addon.Text(item, "Path");
                if (string.IsNullOrEmpty(path) || addon.Text(item, "Id") != asset.Id && addon.Text(item, "Name") != asset.DisplayName) continue;

                string full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(Path.GetDirectoryName(full), root, StringComparison.OrdinalIgnoreCase))
                {
                    yield return full;
                }
            }
        }

        private static string Relative(string path) =>
            Path.GetRelativePath(MelonEnvironment.GameRootDirectory, path).Replace('\\', '/');

        private void Mount()
        {
            Frameworks.RestoreContent();
            try
            {
                foreach (DownloadJob job in _jobs)
                {
                    FrameworkAddon addon = Addon(job.Kind);
                    addon.Call<IDisposable>(addon.Catalog, "Mount", job.Final, job.Asset.Id);
                }
            }
            catch (Exception exception) when (exception is System.Reflection.TargetInvocationException or InvalidOperationException or ArgumentException)
            {
                Fail("Cannot load the downloaded pack: " + (exception.InnerException?.Message ?? exception.Message));
                return;
            }

            _session.RefreshAddons();
            _completed = true;
            _logger.Debug("Room content is ready.");
        }

        internal void Cancel()
        {
            Reset();
            _consent.Clear();
            _consentRoom = null;
            if (_session.Relay)
            {
                _session.Relay.StopConnection(false);
            }
        }

        private void Fail(string reason)
        {
            _logger.Warning("Content download failed: " + reason);
            Reset();
            _session.ShowAddonError(reason, AddonNoticeKind.Download);

            if (_session.Relay)
            {
                _session.Relay.StopConnection(false);
            }
        }

        internal void Reset()
        {
            foreach (DownloadJob job in _jobs)
            {
                job.Output?.Dispose();
                DeletePartial(job);
            }

            _jobs.Clear();
            _completed = false;
            Offered = null;
            _offerRoom = null;
            _accepted = false;
            CancelRequest();
            _room = null;
            Speed = 0;
            _sampleBytes = 0;
        }

        private static void DeletePartial(DownloadJob job)
        {
            try
            {
                if (Directory.Exists(job.Partial))
                {
                    Directory.Delete(job.Partial, true);
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
            }
        }

        private void Request(DownloadJob job, ContentStage stage, string file = null, long offset = 0, int length = 0) =>
            _profiles.SendContent(new ContentMessage(1, _room, stage, job.Asset.Id, job.Asset.Hash, file, offset, length), toServer: true);
    }
}
