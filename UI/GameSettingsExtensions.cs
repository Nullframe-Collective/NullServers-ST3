using Il2Cpp;
using MelonLoader;
using NullServers.Room;
using UnityEngine;

namespace NullServers.UI
{
    public static class GameSettingsExtensions
    {
        private static readonly Dictionary<string, Registration> _registrations = [];

        public static bool IsActive(LobbyMenu menu) => RoomCustomization.OwnsSettings(menu);

        public static IDisposable Register(
            string id,
            Func<LobbyMenu, float, float, float, float> measure,
            Action<LobbyMenu, Rect, float, float> draw)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("A Game Settings section requires an ID.");

            ArgumentNullException.ThrowIfNull(measure);
            ArgumentNullException.ThrowIfNull(draw);

            if (_registrations.ContainsKey(id))
            {
                throw new ArgumentException($"Game Settings section is already registered: {id}.");
            }

            Registration registration = new(id, measure, draw);
            _registrations.Add(id, registration);

            return registration;
        }

        internal static List<Section> Measure(LobbyMenu menu, float width, float row, float padding)
        {
            List<Section> sections = [];

            foreach (Registration registration in _registrations.Values.ToArray())
            {
                try
                {
                    float height = registration.Measure(menu, width, row, padding);

                    if (float.IsFinite(height) && height > 0f)
                    {
                        sections.Add(new Section(registration, height));
                    }
                }
                catch (Exception exception)
                {
                    registration.Report(exception);
                }
            }

            return sections;
        }

        internal sealed class Section
        {
            private readonly Registration _registration;

            internal float Height { get; }

            internal Section(Registration registration, float height)
            {
                _registration = registration;
                Height = height;
            }

            internal void Draw(LobbyMenu menu, Rect area, float row, float padding)
            {
                try
                {
                    _registration.Draw(menu, area, row, padding);
                }
                catch (Exception exception)
                {
                    if (exception.GetType().Name.Contains("ExitGUI")) throw;

                    _registration.Report(exception);
                }
            }
        }

        internal sealed class Registration : IDisposable
        {
            private readonly string _id;
            private string _lastError;

            internal Func<LobbyMenu, float, float, float, float> Measure { get; }
            internal Action<LobbyMenu, Rect, float, float> Draw { get; }

            internal Registration(string id, Func<LobbyMenu, float, float, float, float> measure,
                Action<LobbyMenu, Rect, float, float> draw)
            {
                _id = id;
                Measure = measure;
                Draw = draw;
            }

            internal void Report(Exception exception)
            {
                if (_lastError == exception.Message) return;

                _lastError = exception.Message;
                MelonLogger.Warning($"Game Settings section {_id}: {_lastError}");
            }

            public void Dispose()
            {
                if (_registrations.TryGetValue(_id, out Registration current) && ReferenceEquals(current, this))
                {
                    _registrations.Remove(_id);
                }
            }
        }
    }
}
