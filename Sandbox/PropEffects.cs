using UnityEngine;

namespace NullServers.Sandbox
{
    internal sealed class PropEffects
    {
        private readonly ParticleSystem[] _particles;
        private readonly (Behaviour Component, bool Enabled)[] _lights;
        private readonly (Renderer Component, bool Enabled)[] _trails;
        private bool? _enabled;

        internal PropEffects(GameObject root)
        {
            _particles = root.GetComponentsInChildren<ParticleSystem>(true).ToArray();
            _lights = root.GetComponentsInChildren<Light>(true)
                .Select(value => ((Behaviour)value, value.enabled)).ToArray();
            _trails = root.GetComponentsInChildren<TrailRenderer>(true)
                .Select(value => ((Renderer)value, value.enabled)).ToArray();
        }

        internal void Apply(bool enabled)
        {
            if (_enabled == enabled) return;

            _enabled = enabled;
            foreach (ParticleSystem particles in _particles)
            {
                if (!particles) continue;

                if (enabled && particles.gameObject.activeInHierarchy)
                {
                    particles.Play(false);
                }
                else
                {
                    particles.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                }
            }

            foreach (var item in _lights)
            {
                if (item.Component)
                {
                    item.Component.enabled = enabled && item.Enabled;
                }
            }

            foreach (var item in _trails)
            {
                if (item.Component)
                {
                    item.Component.enabled = enabled && item.Enabled;
                }
            }
        }
    }
}
