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
        private void Advanced()
        {
            RectTransform left = GameUi.Rect("PlayerColumn", _body);
            GameUi.Area(left, 0f, 0f, 0.29f, 1f, 3f * _ui.Unit);
            Text title = _ui.Label(left, "Players", 23);
            _ui.SetRow(title.rectTransform, 0f, 38f);

            ScrollRect list = _ui.Scroll(left, "Players", 44f);

            _players = list.content;
            _roster = null;
            _rosterAt = 0f;

            RectTransform right = GameUi.Rect("Actions", _body);
            GameUi.Area(right, 0.31f, 0f, 1f, 1f);
            RectTransform content = _ui.Scroll(right, "Actions").content;
            _ui.Label(_ui.Row(content, 0f), "PLAYER ACTIONS", 22).fontStyle = FontStyle.Bold;

            RectTransform scaleRow = _ui.Row(content, 50f);
            Text scaleLabel = _ui.Label(scaleRow, "Size (0.2–5)");
            GameUi.Area(scaleLabel.rectTransform, 0f, 0f, 0.38f, 1f);

            InputField scale = _ui.Field(scaleRow, 0.4f, 0.68f, "1", "1", limit: 8);

            _ui.RowButton(scaleRow, "Apply", () =>
            {
                if (Number(scale.text, out var value))
                {
                    _world.Admin(PlayerCommand.Scale, _target, value);
                }
            }, 0.7f);

            var actions = new[]
            {
                ("Flight", PlayerCommand.Fly),
                ("Freeze", PlayerCommand.Freeze),
                ("Invincible", PlayerCommand.Invincible),
                ("Heal", PlayerCommand.Heal),
                ("Bring here", PlayerCommand.Bring),
                ("Go to player", PlayerCommand.GoTo),
                ("Launch upwards", PlayerCommand.Launch),
                ("Respawn", PlayerCommand.Respawn),
                ("Kill", PlayerCommand.Kill),
                ("Remove character", PlayerCommand.Remove),
                ("Reset effects", PlayerCommand.Reset)
            };
            for (int index = 0; index < actions.Length; index++)
            {
                var action = actions[index];
                RectTransform row = _ui.Row(content, 106 + index / 2 * 56, 50f);

                float edge = index % 2 * 0.5f;
                Button button = _ui.RowButton(
                    row,
                    action.Item1,
                    () => _world.Admin(action.Item2, _target),
                    edge,
                    edge + 0.5f,
                    action.Item2 is PlayerCommand.Kill or PlayerCommand.Remove ? new Color(1f, 0.84f, 0.82f) : null);

                _actions.Add((button, action.Item2, action.Item1));
            }

            RectTransform rename = _ui.Row(content, 456f);
            InputField name = _ui.Field(rename, 0f, 0.65f, "", "New nickname", limit: PlayerProfile.NameLimit);

            _ui.RowButton(rename, "Rename", () => _world.Admin(PlayerCommand.Rename, _target, text: name.text), 0.67f);
            _ui.RowButton(
                _ui.Row(content, 522f),
                "Clear all building objects",
                () => _world.Submit(_world.Message(SandboxKind.Clear)),
                color: new Color(1f, 0.9f, 0.78f));

            _ui.Height(content, 584f);
        }

        private void RefreshPlayers()
        {
            var players = _world.Players;
            string roster = string.Join("|", players.Select(player => player.Id + ":" + player.Nickname)) + ":" + _target;

            if (roster != _roster)
            {
                _roster = roster;
                _themeDirty = true;
                GameUi.Clear(_players);

                if (!players.Any(player => player.Id == _target))
                {
                    _target = players.FirstOrDefault()?.Id ?? 0;
                }

                float y = 0f;
                foreach (RoomPlayer player in players)
                {
                    int id = player.Id;
                    Button button = _ui.Button(
                        _players, player.Nickname + (id == 0 ? " (Host)" : ""), () =>
                        {
                            _target = id;
                            _roster = null;
                        },
                        id == _target ? new Color(0.72f, 0.86f, 0.97f) : Color.white);

                    _ui.SetRow(button.GetComponent<RectTransform>(), y, 62f);

                    y += 68f;
                }

                _ui.Height(_players, y);
            }

            PlayerEffects effects = _world.Effects(_target);

            foreach (var action in _actions)
            {
                action.Button.interactable = _world.IsAdmin && (_world.IsHost || _target != 0);

                bool enabled = action.Command switch
                {
                    PlayerCommand.Fly => effects.Fly,
                    PlayerCommand.Freeze => effects.Frozen,
                    PlayerCommand.Invincible => effects.Invincible,
                    _ => false
                };

                action.Button.GetComponentInChildren(Il2CppType.Of<Text>(), true).Cast<Text>().text =
                    action.Name + (action.Command is PlayerCommand.Fly or PlayerCommand.Freeze or PlayerCommand.Invincible ? enabled ? ": ON" : ": OFF" : "");
            }
        }
    }
}
