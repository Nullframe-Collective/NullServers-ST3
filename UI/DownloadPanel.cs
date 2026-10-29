using Il2CppInterop.Runtime;
using NullServers.Additional;
using NullServers.Content;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed class DownloadPanel
    {
        private static readonly Color Background = new(0.025f, 0.03f, 0.045f, 1f);
        private static readonly Color Track = new(1f, 1f, 1f, 0.08f);
        private static readonly Color Fill = new(0.75f, 0.25f, 0.28f, 1f);

        private readonly ContentDownloads _downloads;
        private readonly PauseMenu _pause;
        private readonly List<(DownloadJob Job, Text Status)> _rows = new();
        private GameUi _ui;
        private RectTransform _root, _fill, _list;
        private Text _stats;
        private float _nextScan;
        private bool _offer;
        private int _shownJobs = -1;

        internal DownloadPanel(ContentDownloads downloads, PauseMenu pause)
        {
            _downloads = downloads;
            _pause = pause;
            downloads.Requested += Show;
        }

        internal void Update()
        {
            if (!_downloads.Visible || _root && _offer != (_downloads.Offered != null))
            {
                Hide();
                if (!_downloads.Visible) return;
            }

            if (!_root)
            {
                if (Time.unscaledTime < _nextScan) return;

                _nextScan = Time.unscaledTime + 0.5f;
                Show();
                if (!_root) return;
            }

            if (_offer)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
                _stats.text = _downloads.Offered.Length + " pack(s) needed";
                return;
            }

            if (_shownJobs != _downloads.Jobs.Count)
            {
                Rows();
            }

            foreach (var row in _rows)
            {
                row.Status.text = Status(row.Job);
            }

            long total = _downloads.Total;
            long received = _downloads.Received;
            float progress = total > 0 ? Mathf.Clamp01((float)received / total) : 0f;
            _fill.anchorMax = new Vector2(progress, 1f);
            _stats.text = _downloads.Connecting ? "Connecting to the host..."
                : total > 0 ? $"{Speed(_downloads.Speed)}     {Megabytes(received)} / {Megabytes(total)} MB     {Mathf.FloorToInt(progress * 100f)}%"
                : "Checking packs...";
        }

        private const float ListHeight = 340f;
        private const int VisibleRows = 4;

        private void Show()
        {
            if (_root) return;

            Button template = _pause.Settings ? _pause.Settings : AddonMenu.MenuTemplate(inactive: true);
            Canvas source = template ? template.GetComponentInParent(Il2CppType.Of<Canvas>(), true)?.Cast<Canvas>() : null;
            if (!source) return;

            _ui = new GameUi(template, 1f);
            _offer = _downloads.Offered != null;
            GameObject target = new("NullFrameDownloadPanel");
            target.layer = source.gameObject.layer;
            Object.DontDestroyOnLoad(target);
            _root = GameUi.Add<RectTransform>(target);

            Canvas canvas = GameUi.Add<Canvas>(target);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;

            CanvasScaler scaler = GameUi.Add<CanvasScaler>(target);
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 1f;

            GameUi.Add<GraphicRaycaster>(target);
            _ui.Image(_root, Background);

            RectTransform body = GameUi.Rect("Body", _root);
            GameUi.Area(body, 0.10f, 0.06f, 0.90f, 0.94f);

            Text title = _ui.Label(body, _offer ? "ROOM CONTENT REQUIRED" : "DOWNLOADING ROOM CONTENT", 44, TextAnchor.MiddleCenter);
            GameUi.Area(title.rectTransform, 0f, 0.89f, 1f, 1f);

            Text description = _ui.Label(body, _offer
                ? "This room uses packs you do not have. Download them from the host? They are checked before they load. Checks reduce the risk, but cannot guarantee that host content is safe."
                : "The host's packs are downloaded and checked before they load. Checks reduce the risk, but cannot guarantee that host content is safe.", 25, TextAnchor.MiddleCenter);
            GameUi.Area(description.rectTransform, 0.04f, 0.76f, 0.96f, 0.88f);

            _list = GameUi.Rect("Packs", body);
            GameUi.Area(_list, 0f, 0.74f, 1f, 0.74f);
            _list.offsetMin = new Vector2(0f, -ListHeight);

            _stats = _ui.Label(body, "", 28, TextAnchor.MiddleCenter);
            GameUi.Area(_stats.rectTransform, 0f, 0.24f, 1f, 0.31f);

            RectTransform bar = GameUi.Rect("Progress", body);
            GameUi.Area(bar, 0f, 0.19f, 1f, 0.225f);
            _ui.Image(bar, Track, false);

            _fill = GameUi.Rect("Fill", bar);
            GameUi.Area(_fill, 0f, 0f, 0f, 1f);
            _ui.Image(_fill, Fill, false);

            _rows.Clear();
            _shownJobs = -1;
            if (_offer)
            {
                bar.gameObject.SetActive(false);
                AddonPack[] packs = _downloads.Offered;
                for (int index = 0; index < packs.Length && index < VisibleRows; index++)
                {
                    Row(index, packs.Length, packs[index].Name, packs[index].Path).text = "Not installed";
                }

                Button leave = _ui.Button(body, "Leave", _downloads.Decline);
                GameUi.Area(leave.GetComponent<RectTransform>(), 0f, 0f, 0.49f, 0.11f);
                Button download = _ui.Button(body, "Download", _downloads.Accept);
                GameUi.Area(download.GetComponent<RectTransform>(), 0.51f, 0f, 1f, 0.11f);
                return;
            }

            Button cancel = _ui.Button(body, "Cancel", _downloads.Cancel);
            GameUi.Area(cancel.GetComponent<RectTransform>(), 0.25f, 0f, 0.75f, 0.11f);
        }

        private void Rows()
        {
            GameUi.Clear(_list);
            _rows.Clear();
            int count = _downloads.Jobs.Count;
            _shownJobs = count;

            for (int index = 0; index < count; index++)
            {
                DownloadJob job = _downloads.Jobs[index];
                if (index < VisibleRows)
                {
                    _rows.Add((job, Row(index, count, job.Name, job.Kind + "  ·  " + Megabytes(job.Size) + " MB")));
                }
            }
        }

        private Text Row(int index, int count, string name, string details)
        {
            float height = ListHeight / Mathf.Clamp(count, 3, VisibleRows);
            RectTransform row = GameUi.Rect("Pack", _list);
            _ui.SetRow(row, index * height, height - 10f);
            _ui.Image(row, new Color(1f, 1f, 1f, 0.05f), false);

            string more = index == VisibleRows - 1 && count > VisibleRows ? $"  (+{count - VisibleRows} more)" : "";
            Text title = _ui.Label(row, name + more, 28);
            GameUi.Area(title.rectTransform, 0.02f, 0.48f, 0.68f, 0.96f);
            GameUi.Fit(title, 18, title.fontSize);
            title.verticalOverflow = VerticalWrapMode.Truncate;

            Text detail = _ui.Label(row, details, 19);
            GameUi.Area(detail.rectTransform, 0.02f, 0.06f, 0.68f, 0.48f);
            GameUi.Fit(detail, 14, detail.fontSize);
            detail.verticalOverflow = VerticalWrapMode.Truncate;

            Text status = _ui.Label(row, "", 24, TextAnchor.MiddleRight);
            GameUi.Area(status.rectTransform, 0.68f, 0.08f, 0.98f, 0.92f);
            return status;
        }

        private static string Status(DownloadJob job) => job.State switch
        {
            DownloadState.Queued => "Waiting",
            DownloadState.Listing => "Connecting",
            DownloadState.Receiving => "Downloading " + (job.Size > 0 ? job.Received * 100 / job.Size : 0) + "%",
            DownloadState.Checking => "Checking",
            _ => "Ready"
        };

        private static string Megabytes(long bytes) => (bytes / 1048576.0).ToString("0.0");

        private static string Speed(double bytesPerSecond) => bytesPerSecond >= 1048576
            ? (bytesPerSecond / 1048576).ToString("0.0") + " MB/s"
            : (bytesPerSecond / 1024).ToString("0") + " KB/s";

        private void Hide()
        {
            if (_root)
            {
                _root.gameObject.SetActive(false);
                Object.Destroy(_root.gameObject);
            }

            _root = null;
            _rows.Clear();
        }
    }
}
