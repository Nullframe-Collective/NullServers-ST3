using MelonLoader;
using MelonLoader.Utils;
using NullServers.Additional;
using NullServers.Network;
using NullServers.Players;
using UnityEngine;

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
        internal string Partial, Final;
        internal Task<string> Check;

        internal string Name => Asset.DisplayName;
    }

    internal sealed class ContentDownloads
    {
        private const float Timeout = 20f;
        private const int Window = 4;

        private readonly RelaySession _session;
        private readonly PlayerProfiles _profiles;
        private readonly MelonLogger.Instance _logger;
        private readonly string _cache = Path.Combine(MelonEnvironment.UserDataDirectory, "NullFrame", "Downloads");
        private readonly List<DownloadJob> _jobs = new();
        private readonly List<IDisposable> _mounts = new();
        private readonly HashSet<string> _consent = new(StringComparer.Ordinal);
        private string _consentRoom;
        private float _consentUntil;
        private string _room;
        private float _lastMessage;
        private float _sampleAt;
        private long _sampleBytes;
        private bool _completed;

        internal IReadOnlyList<DownloadJob> Jobs => _jobs;
        internal bool Busy => _jobs.Count > 0 && !_completed;
        internal long Total => _jobs.Sum(job => job.Size);
        internal long Received => _jobs.Sum(job => job.Received);
        internal double Speed { get; private set; }

        internal ContentDownloads(RelaySession session, PlayerProfiles profiles, MelonLogger.Instance logger)
        {
            _session = session;
            _profiles = profiles;
            _logger = logger;
            profiles.ContentReceivedFromHost += Receive;
        }

        internal static bool Downloadable(AddonPack pack) =>
            pack.Hash != null && PackFiles.KindOf(pack.Folder) is { } kind && Mountable(kind);

        private static FrameworkAddon Addon(PackKind kind) => kind switch
        {
            PackKind.Weapon => Frameworks.Weapons,
            PackKind.Cosmetic => Frameworks.Customization,
            _ => Frameworks.NPCs
        };

        private static bool Mountable(PackKind kind) =>
            Addon(kind) is { } addon && Frameworks.Settings.Enabled(addon.Requirement.Id) && addon.Catalog.GetMethod("Mount") != null;

        internal static bool CanFetch(AddonNotice notice) =>
            notice is { Kind: AddonNoticeKind.Content, Missing.Length: 0, Packs.Length: > 0 } && notice.Packs.All(Downloadable);

        internal void Consent(string room, IEnumerable<AddonPack> packs)
        {
            _consentRoom = room;
            _consentUntil = Time.unscaledTime + 600f;
            _consent.Clear();
            _consent.UnionWith(packs.Select(pack => pack.Id + ":" + pack.Hash));
        }

        internal bool Covers(string room, AddonNotice notice) =>
            CanFetch(notice) && Consented(room, notice.Packs);

        private bool Consented(string room, IEnumerable<AddonPack> packs) =>
            room == _consentRoom && Time.unscaledTime < _consentUntil && packs.All(pack => _consent.Contains(pack.Id + ":" + pack.Hash));

        internal bool Prepare(FrameworkManifest manifest)
        {
            if (Busy) return true;
            if (_completed) return false;

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
                    Final = Path.Combine(_cache, name)
                };

                DeletePartial(job);
                if (Directory.Exists(job.Final))
                {
                    StartCheck(job, job.Final);
                }

                _jobs.Add(job);
            }

            _lastMessage = _sampleAt = Time.unscaledTime;
            _logger.Msg($"Downloading {_jobs.Count} pack(s) from the host.");
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
            while (job.FileRequested < file.Size && job.FileRequested - job.FileWritten < (long)Window * ContentMessage.MaxRange)
            {
                int length = (int)Math.Min(ContentMessage.MaxRange, file.Size - job.FileRequested);
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
            bool cached = !Directory.Exists(job.Partial);

            if (problem != null)
            {
                if (cached)
                {
                    Directory.Delete(job.Final, true);
                }

                Fail(job.Name + ": " + problem);
                return;
            }

            if (!cached)
            {
                if (Directory.Exists(job.Final))
                {
                    Directory.Delete(job.Final, true);
                }

                Directory.Move(job.Partial, job.Final);
            }

            job.State = DownloadState.Ready;
            _logger.Msg($"{job.Name}: checks passed ({(cached ? "cached" : job.Size + " bytes")}).");
        }

        private void Mount()
        {
            Frameworks.RestoreContent();
            try
            {
                foreach (DownloadJob job in _jobs)
                {
                    FrameworkAddon addon = Addon(job.Kind);
                    _mounts.Add(addon.Call<IDisposable>(addon.Catalog, "Mount", job.Final, job.Asset.Id));
                }
            }
            catch (Exception exception) when (exception is System.Reflection.TargetInvocationException or InvalidOperationException or ArgumentException)
            {
                Fail("Cannot load the downloaded pack: " + (exception.InnerException?.Message ?? exception.Message));
                return;
            }

            _session.RefreshAddons();
            _completed = true;
            _logger.Msg("Room content is ready.");
        }

        internal void Cancel()
        {
            Reset();
            if (_session.Relay)
            {
                _session.Relay.StopConnection(false);
            }
        }

        private void Fail(string reason)
        {
            _logger.Warning("Content download failed: " + reason);
            Reset();
            _session.ShowAddonError("Download failed. " + reason, AddonNoticeKind.Unavailable);

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
            if (_mounts.Count > 0)
            {
                Frameworks.RestoreContent();
                foreach (IDisposable mount in _mounts)
                {
                    mount?.Dispose();
                }

                _mounts.Clear();
                _session.RefreshAddons();
            }

            _completed = false;
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
