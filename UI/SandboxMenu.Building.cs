using Il2Cpp;
using Il2CppInterop.Runtime;
using NullServers.Customization;
using NullServers.Network;
using NullServers.Players;
using NullServers.Room;
using NullServers.Sandbox;
using System.Globalization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using Object = UnityEngine.Object;

namespace NullServers.UI
{
    internal sealed partial class SandboxMenu
    {
        private void Building()
        {
            _themeDirty = true;
            GameUi.Clear(_body);
            _icons.Clear();
            _catalog = null;

            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = null;

            RectTransform tabs = _ui.Row(_body, 0f, 48f);
            var pages = new[]
            {
                ("Objects", 0f, 0.23f),
                ("Object settings", 0.24f, 0.55f)
            };
            for (int index = 0; index < pages.Length; index++)
            {
                int tab = index;
                var page = pages[index];
                _ui.RowButton(tabs, page.Item1, () =>
                {
                    _buildingTab = tab;
                    Building();
                }, page.Item2, page.Item3, _buildingTab == tab ? new Color(0.75f, 0.87f, 0.97f) : null);
            }

            _count = _ui.Label(tabs, "", 19, TextAnchor.MiddleRight);
            GameUi.Area(_count.rectTransform, 0.72f, 0f, 1f, 1f);

            RectTransform tools = _ui.Row(_body, 58f);
            _ui.RowButton(tools, "Build with empty hands", () =>
            {
                _world.Building.BeginSelection();
                CloseConsole();
            }, 0f, 0.42f);

            _ui.RowButton(tools, "Undo", () => _world.Building.Undo(), 0.43f, 0.69f);

            _ui.RowButton(tools, "Return to weapon", () =>
            {
                _world.Hands.Equip(false);
                Building();
            }, 0.7f);

            _selection = _ui.Label(_ui.Row(_body, 114f, 40f), "", 20);
            _selection.color = GameUi.Blue;

            if (_buildingTab == 0)
            {
                Catalog();
            }
            else if (_buildingTab == 1)
            {
                Editor();
            }
        }

        private void Catalog()
        {
            RectTransform row = _ui.Row(_body, 158f);
            _ui.Field(row, 0f, 0.5f, _search, "Search objects...", value =>
            {
                _search = value;
                _refreshCatalog = true;
                _refreshAt = Time.unscaledTime + 0.15f;
            }, 80);

            string[] names =
            [
                "All",
                "Shapes",
                "Props",
                "Recent"
            ];
            for (int index = 0; index < names.Length; index++)
            {
                int filter = index;

                _ui.RowButton(row, names[index], () =>
                {
                    _filter = filter;
                    _refreshCatalog = true;
                    _refreshAt = 0f;
                }, 0.51f + index * 0.1225f, 0.51f + (index + 1) * 0.1225f);
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectCatalog", 216f, 44f);

            _catalog = scroll.content;

            Text help = _ui.Label(
                _body,
                "LMB: place/select. Shift + LMB: group selection. E: carry/drop. LMB while carrying: throw. R + mouse: rotate.",
                19);

            GameUi.Area(help.rectTransform, 0f, 0f, 1f, 0f);
            help.rectTransform.offsetMax = new Vector2(0f, 40f * _ui.Unit);

            _refreshCatalog = true;
            _refreshAt = 0f;
        }

        // Rebuilds the prop grid for the current category and search. Waits until the layout has a width.
        private void RefreshCatalog()
        {
            float width = _catalog.rect.width;
            if (width <= 0f)
            {
                _refreshAt = Time.unscaledTime + 0.05f;
                return;
            }

            GameUi.Clear(_catalog);

            _icons.Clear();
            _refreshCatalog = false;
            _assetRevision = _world.Assets.Revision;
            _themeDirty = true;

            SandboxAssets.Entry[] entries = CatalogEntries();
            int columns = Mathf.Max(2, Mathf.FloorToInt(width / (146f * _ui.Unit)));
            Vector2 cell = new(width / columns, 154f * _ui.Unit);

            for (int index = 0; index < entries.Length; index++)
            {
                CatalogTile(entries[index], new Vector2(index % columns * cell.x, -(index / columns) * cell.y), cell);
            }

            _ui.Height(_catalog, Mathf.Ceil(entries.Length / (float)columns) * cell.y / _ui.Unit);
            if (entries.Length == 0)
            {
                _ui.Label(_ui.Row(_catalog, 0f, 65f), "No objects found. Try another category or search.");
            }
        }

        // Filters: 0 all, 1 primitives, 2 props, 3 recently used.
        private SandboxAssets.Entry[] CatalogEntries()
        {
            var source = _filter == 3 ?
                _world.Building.Recent.Select(id => _world.Assets.Find(id)).Where(entry => entry != null) :
                _world.Assets.Entries;

            return source.Where(entry => (_filter != 1 || entry.Primitive.HasValue) &&
                (_filter != 2 || !entry.Primitive.HasValue) &&
                (string.IsNullOrWhiteSpace(_search) || entry.Name.Contains(_search, StringComparison.OrdinalIgnoreCase))).ToArray();
        }

        private void CatalogTile(SandboxAssets.Entry entry, Vector2 position, Vector2 cell)
        {
            Button tile = _ui.Button(_catalog, "", () =>
            {
                _world.Building.Start(entry.Id);
                CloseConsole();
            });

            RectTransform rect = tile.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = position;
            rect.sizeDelta = new Vector2(cell.x - 7f * _ui.Unit, cell.y - 7f * _ui.Unit);

            RectTransform iconRect = GameUi.Rect("Thumbnail", rect);
            GameUi.Area(iconRect, 0.07f, 0.27f, 0.93f, 0.96f);

            // Props from the game's own building options come with a sprite; the rest get a rendered icon that may arrive later.
            if (entry.Icon)
            {
                Image icon = _ui.Image(iconRect, Color.white, false);
                icon.sprite = entry.Icon;
                icon.preserveAspect = true;
            }
            else
            {
                RawImage icon = GameUi.Add<RawImage>(iconRect.gameObject);
                icon.raycastTarget = false;
                icon.texture = _world.Assets.Icon(entry);
                _icons.Add((icon, entry));
            }

            Text caption = _ui.Label(rect, entry.Name, 17, TextAnchor.MiddleCenter);
            GameUi.Area(caption.rectTransform, 0.04f, 0.02f, 0.96f, 0.26f);
        }

        private InputField[] VectorFields(Transform parent, float y, string title, Point3 value)
        {
            RectTransform row = _ui.Row(parent, y, 58f);
            Text titleText = _ui.Label(row, title);
            GameUi.Area(titleText.rectTransform, 0f, 0f, 0.25f, 1f);
            InputField[] result = new InputField[3];

            float[] values =
            [
                value.X,
                value.Y,
                value.Z
            ];

            for (int index = 0; index < 3; index++)
            {
                RectTransform axis = GameUi.Rect("Axis", row);
                GameUi.Area(axis, 0.26f + index * 0.245f, 0f, 0.495f + index * 0.245f, 1f, 3f * _ui.Unit);

                Text label = _ui.Label(axis, "XYZ"[index].ToString(), 18);
                GameUi.Area(label.rectTransform, 0f, 0f, 0.14f, 1f);

                InputField input = _ui.Input(
                    axis,
                    values[index].ToString("0.###", CultureInfo.InvariantCulture),
                    "0",
                    _ => Preview(),
                    12);

                GameUi.Area(input.GetComponent<RectTransform>(), 0.16f, 0f, 1f, 1f);

                result[index] = input;
            }

            return result;
        }

        // The editor for the selected objects: position, size, rotation, look and the apply/delete buttons.
        private void Editor()
        {
            BuildingObject draft = _world.Building.Draft;
            _draftId = draft?.Id ?? -1;

            if (draft == null)
            {
                _ui.Label(
                    _ui.Row(_body, 180f, 90f),
                    "Aim at a prop and left-click in build mode. Hold Shift to add more objects. E picks up nearby props and ragdolls.",
                    23);

                return;
            }

            ScrollRect scroll = _ui.Scroll(_body, "ObjectEditor", 168f);
            RectTransform content = scroll.content;

            _position = _size = _angles = null;
            _position = VectorFields(content, 0f, "Position", draft.Position);
            _size = VectorFields(content, 72f, "Size", draft.Scale);

            foreach (InputField field in _position)
            {
                field.interactable = _world.Building.Editing;
            }

            _angles = VectorFields(content, 144f, "Rotation", SandboxWorld.Point(SandboxWorld.Quaternion(draft.Rotation).eulerAngles));
            _color = draft.Color;
            _texture = draft.Texture ?? "";
            _dynamic = draft.Dynamic;
            _effectsEnabled = draft.Effects;

            EditorLook(content);
            EditorActions(content);
            _ui.Height(content, 566f);

            Preview();
        }

        private void EditorLook(RectTransform content)
        {
            RectTransform row = _ui.Row(content, 224f, 54f);
            Text label = _ui.Label(row, "Color");
            GameUi.Area(label.rectTransform, 0f, 0f, 0.25f, 1f);

            _colorButton = _ui.RowButton(row, "", () => Palette(), 0.26f, 0.34f);
            _hex = _ui.Field(row, 0.36f, 0.67f, _color, "#FFFFFF", value =>
            {
                if (RoomOptions.IsValidColor(value))
                {
                    _color = value;
                    Preview();
                }
            }, 7);

            _physics = _ui.RowButton(row, "", () =>
            {
                _dynamic = !_dynamic;
                Preview();
            }, 0.7f);

            _effects = _ui.RowButton(_ui.Row(content, 288f, 46f), "", () =>
            {
                _effectsEnabled = !_effectsEnabled;
                Preview();
            });

            RectTransform textureRow = _ui.Row(content, 350f, 54f);
            Text textureLabel = _ui.Label(textureRow, "Texture URL");
            GameUi.Area(textureLabel.rectTransform, 0f, 0f, 0.25f, 1f);

            _ui.Field(textureRow, 0.26f, 1f, _texture, "https://.../texture.png", value =>
            {
                _texture = value;
                Preview();
            }, Appearance.UrlLimit);
        }

        private void EditorActions(RectTransform content)
        {
            RectTransform controls = _ui.Row(content, 430f, 54f);

            _ui.RowButton(controls, "Apply to selection", () =>
            {
                Preview();
                _world.Building.Confirm();
                Building();
            }, 0f, 0.49f, new Color(0.74f, 0.9f, 0.79f));

            _ui.RowButton(controls, "Delete selection", () =>
            {
                _world.Building.DeleteSelected();
                Building();
            }, 0.51f, 1f, new Color(1f, 0.83f, 0.8f));

            _ui.Label(
                _ui.Row(content, 500f, 60f),
                "Type exact X/Y/Z values. Changed properties apply to every selected object; other properties are preserved.",
                19);
        }

        private static bool Number(string value, out float number) => float.TryParse(
            value.Replace(',', '.'),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out number) && float.IsFinite(number);

        private static Point3 Values(InputField[] fields)
        {
            if (fields == null || fields.Any(field => !field || !Number(field.text, out _)))
            {
                return null;
            }

            float[] values = fields.Select(field => float.Parse(field.text.Replace(',', '.'), CultureInfo.InvariantCulture)).ToArray();
            return new Point3(values[0], values[1], values[2]);
        }

        private void Preview()
        {
            Point3 position = Values(_position);
            Point3 size = Values(_size);
            Point3 angles = Values(_angles);

            if (position != null)
            {
                _world.Building.MoveSelected(position);
            }

            if (size != null && angles != null)
            {
                _world.Building.Change(
                    size,
                    SandboxWorld.Rotation(Quaternion.Euler(angles.X, angles.Y, angles.Z)),
                    _color,
                    _texture,
                    _dynamic,
                    _effectsEnabled);
            }

            GameUi.Tint(_colorButton, _color);

            if (_physics)
            {
                _physics.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text = "Physics: " + (_dynamic ? "ON" : "OFF");
            }

            if (_effects)
            {
                _effects.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text =
                    "Effects: " + (_effectsEnabled ? "ON" : "OFF");
            }
        }

        private void Palette(Action<string> changed = null)
        {
            _paletteChanged = changed;
            if (_palette)
            {
                Object.Destroy(_palette.gameObject);
            }

            _palette = GameUi.Rect("ColorPalette", _panel);
            _ui.Image(_palette, new Color(0.7f, 0.74f, 0.8f, 0.8f));

            RectTransform box = GameUi.Rect("PalettePanel", _palette);
            box.anchorMin = box.anchorMax = box.pivot = new Vector2(0.5f, 0.5f);
            box.sizeDelta = new Vector2(360f, 440f) * _ui.Unit;

            _ui.Image(box, new Color(0.96f, 0.97f, 0.98f));
            _ui.Label(_ui.Row(box, 12f, 38f), "Object color", 25, TextAnchor.MiddleCenter);
            _wheel = GameUi.Rect("ColorWheel", box);
            _wheel.anchorMin = _wheel.anchorMax = new Vector2(0.5f, 1f);
            _wheel.pivot = new Vector2(0.5f, 1f);
            _wheel.anchoredPosition = new Vector2(0f, -58 * _ui.Unit);
            _wheel.sizeDelta = new Vector2(270f, 270f) * _ui.Unit;

            GameUi.Add<RawImage>(_wheel.gameObject).texture = MenuUi.ColorWheel;

            _marker = GameUi.Rect("ColorCursor", _wheel);
            _marker.sizeDelta = new Vector2(10f, 10f) * _ui.Unit;
            _ui.Image(_marker, GameUi.Ink, false);

            ColorUtility.TryParseHtmlString(_color, out var selected);
            Color.RGBToHSV(selected, out _hue, out _saturation, out _brightness);

            Slider slider = _ui.Slider(_ui.Row(box, 342f, 20f), _brightness, value =>
            {
                _brightness = value;
                PaletteColor();
            });
            slider.GetComponent<RectTransform>().offsetMin += new Vector2(24f * _ui.Unit, 0f);
            slider.GetComponent<RectTransform>().offsetMax -= new Vector2(24f * _ui.Unit, 0f);

            _ui.RowButton(_ui.Row(box, 382f, 44f), "Done", () =>
            {
                Object.Destroy(_palette.gameObject);
                _palette = null;
                _paletteChanged = null;
            });

            PaletteColor();
        }

        private void PaletteColor()
        {
            _color = "#" + ColorUtility.ToHtmlStringRGB(Color.HSVToRGB(_hue, _saturation, _brightness));
            if (_hex)
            {
                _hex.SetTextWithoutNotify(_color);
            }

            _marker.anchorMin = _marker.anchorMax = new Vector2(
                Mathf.Cos(_hue * 2f * Mathf.PI),
                Mathf.Sin(_hue * 2f * Mathf.PI)) * (_saturation * 0.5f) + Vector2.one * 0.5f;
            _marker.anchoredPosition = Vector2.zero;

            if (_paletteChanged != null)
            {
                _paletteChanged(_color);
                GameUi.Tint(_colorButton, _color);
            }
            else
            {
                Preview();
            }
        }

        private void UpdateBuildingTab()
        {
            SandboxBuilding building = _world.Building;
            if (_count)
            {
                int own = building.States.Count(item => item.Owner == _world.LocalId);
                _count.text = own + " / " + SandboxMessage.MaxObjectsPerPlayer + " objects";
            }

            if (_selection)
            {
                _selection.text = SelectionText();
            }

            if (_buildingTab == 1 && _draftId != (building.Draft?.Id ?? -1))
            {
                Building();
            }

            if (_catalog && !_refreshCatalog && _assetRevision != _world.Assets.Revision)
            {
                _refreshCatalog = true;
                _refreshAt = Time.unscaledTime + 1f;
            }

            if (_catalog && _refreshCatalog && Time.unscaledTime >= _refreshAt)
            {
                RefreshCatalog();
            }

            foreach (var icon in _icons)
            {
                if (icon.Image && !icon.Image.texture)
                {
                    icon.Image.texture = _world.Assets.Icon(icon.Entry);
                }
            }
        }

        private string SelectionText()
        {
            SandboxBuilding building = _world.Building;
            if (building.Editing) return "Selected: " + building.SelectionCount + " object(s)";
            if (building.Draft == null) return "Choose a picture or select a placed object.";

            string name = _world.Assets.Find(building.Draft.Asset)?.Name ?? "object";
            return "Placing: " + name;
        }

        private void UpdatePaletteWheel()
        {
            Canvas canvas = _palette.GetComponentInParent<Canvas>();
            Camera camera = canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_wheel, Input.mousePosition, camera, out Vector2 local))
            {
                Vector2 point = (local - _wheel.rect.center) / (_wheel.rect.width * 0.5f);
                if (Input.GetMouseButtonDown(0) && point.sqrMagnitude <= 1f)
                {
                    _wheelDragging = true;
                }

                if (_wheelDragging && Input.GetMouseButton(0))
                {
                    _hue = Mathf.Repeat(Mathf.Atan2(point.y, point.x) / (Mathf.PI * 2f), 1f);
                    _saturation = Mathf.Clamp01(point.magnitude);
                    PaletteColor();
                }
            }

            if (Input.GetMouseButtonUp(0))
            {
                _wheelDragging = false;
            }
        }
    }
}
