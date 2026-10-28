using Il2CppInterop.Runtime;
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
        private RectTransform _root, _fill;
        private Text _stats;

        internal DownloadPanel(ContentDownloads downloads, PauseMenu pause)
        {
            _downloads = downloads;
            _pause = pause;
        }

        internal void Update()
        {
            if (!_downloads.Busy)
            {
                Hide();
                return;
            }

            if (!_root && !Build()) return;

            foreach (var row in _rows)
            {
                row.Status.text = Status(row.Job);
            }

            long total = _downloads.Total;
            long received = _downloads.Received;
            float progress = total > 0 ? Mathf.Clamp01((float)received / total) : 0f;
            _fill.anchorMax = new Vector2(progress, 1f);
            _stats.text = total > 0
                ? $"{Speed(_downloads.Speed)}     {Megabytes(received)} / {Megabytes(total)} MB     {Mathf.FloorToInt(progress * 100f)}%"
                : "Checking packs...";
        }

        private bool Build()
        {
            Button template = _pause.Settings;
            Canvas source = template ? template.GetComponentInParent(Il2CppType.Of<Canvas>())?.Cast<Canvas>() : null;
            if (!source) return false;

            _ui = new GameUi(template);
            GameObject target = new("NullFrameDownloadPanel");
            target.layer = source.gameObject.layer;
            _root = GameUi.Add<RectTransform>(target);

            Canvas canvas = GameUi.Add<Canvas>(target);
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32760;

            CanvasScaler original = source.GetComponent<CanvasScaler>();
            if (original)
            {
                CanvasScaler scaler = GameUi.Add<CanvasScaler>(target);
                scaler.uiScaleMode = original.uiScaleMode;
                scaler.referenceResolution = original.referenceResolution;
                scaler.screenMatchMode = original.screenMatchMode;
                scaler.matchWidthOrHeight = original.matchWidthOrHeight;
                scaler.scaleFactor = original.scaleFactor;
                scaler.referencePixelsPerUnit = original.referencePixelsPerUnit;
            }

            GameUi.Add<GraphicRaycaster>(target);
            _ui.Image(_root, Background);

            RectTransform body = GameUi.Rect("Body", _root);
            GameUi.Area(body, 0.10f, 0.10f, 0.90f, 0.90f);

            Text title = _ui.Label(body, "DOWNLOADING ROOM CONTENT", 42, TextAnchor.MiddleCenter);
            GameUi.Area(title.rectTransform, 0f, 0.87f, 1f, 1f);

            Text description = _ui.Label(body,
                "The host's packs are downloaded and checked before they load. Checks reduce the risk, but cannot guarantee that host content is safe.", 27);
            GameUi.Area(description.rectTransform, 0f, 0.69f, 1f, 0.85f);
            GameUi.Fit(description, Mathf.RoundToInt(16f * _ui.Unit), description.fontSize);

            ScrollRect scroll = _ui.Scroll(body, "Packs");
            GameUi.Area(scroll.GetComponent<RectTransform>(), 0f, 0.40f, 1f, 0.65f);

            float top = 0f;
            _rows.Clear();

            foreach (DownloadJob job in _downloads.Jobs)
            {
                _rows.Add((job, Row(scroll.content, top, job)));
                top += 118f;
            }

            _ui.Height(scroll.content, top);

            _stats = _ui.Label(body, "", 26, TextAnchor.MiddleCenter);
            GameUi.Area(_stats.rectTransform, 0f, 0.29f, 1f, 0.36f);

            RectTransform bar = GameUi.Rect("Progress", body);
            GameUi.Area(bar, 0f, 0.23f, 1f, 0.27f);
            _ui.Image(bar, Track, false);

            _fill = GameUi.Rect("Fill", bar);
            GameUi.Area(_fill, 0f, 0f, 0f, 1f);
            _ui.Image(_fill, Fill, false);

            Button cancel = _ui.Button(body, "Cancel", _downloads.Cancel);
            GameUi.Area(cancel.GetComponent<RectTransform>(), 0.25f, 0f, 0.75f, 0.10f);
            return true;
        }

        private Text Row(RectTransform parent, float top, DownloadJob job)
        {
            RectTransform row = _ui.Row(parent, top, 106f);
            _ui.SetRow(row, top, 84f, 0f, 18f);
            _ui.Image(row, new Color(1f, 1f, 1f, 0.035f), false);

            Text title = _ui.Label(row, job.Name, 28);
            GameUi.Area(title.rectTransform, 0.02f, 0.55f, 0.64f, 1f);
            GameUi.Fit(title, Mathf.RoundToInt(16f * _ui.Unit), title.fontSize);
            title.verticalOverflow = VerticalWrapMode.Truncate;

            Text detail = _ui.Label(row, job.Kind + "  ·  " + job.Asset.Id, 19);
            GameUi.Area(detail.rectTransform, 0.02f, 0.02f, 0.70f, 0.55f);

            Text status = _ui.Label(row, "", 23, TextAnchor.MiddleRight);
            GameUi.Area(status.rectTransform, 0.72f, 0.08f, 0.98f, 0.92f);
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
