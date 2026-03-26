using Microsoft.Xna.Framework;
using System;

namespace game
{
    // ──────────────────────────────────────────────────────────────────
    //  Interfaz de IA
    // ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Contrato mínimo para cualquier comportamiento de IA de entidad.
    /// El método Update recibe la entidad dueña y puede modificar
    /// su Velocity y Facing directamente.
    /// </summary>
    public interface IEntityAI
    {
        /// <summary>
        /// Llamado cada frame para actualizar la IA.
        /// Puede mutar entity.Velocity y entity.Facing.
        /// </summary>
        void Update(Entity entity, float dt, Vector3 playerPosition);

        /// <summary>
        /// Notifica a la IA que la entidad recibió daño.
        /// Permite reaccionar (huir, agredir, etc.).
        /// </summary>
        void OnDamaged(Entity entity, float amount, Vector3 sourcePosition);
    }

    // ──────────────────────────────────────────────────────────────────
    //  WanderAI — camina aleatoriamente, huye al recibir daño
    // ──────────────────────────────────────────────────────────────────
    public sealed class WanderAI : IEntityAI
    {
        private static readonly Random _rng = new Random();

        private const float WanderIntervalMin = 2.0f;
        private const float WanderIntervalMax = 5.0f;
        private const float WanderPauseMin    = 1.0f;
        private const float WanderPauseMax    = 3.0f;
        private const float FleeDistance      = 8.0f;
        private const float FleeDuration      = 2.0f;

        private float _timer       = 0f;
        private float _targetTimer = 0f;
        private bool  _paused      = false;
        private float _targetYaw   = 0f;

        // huida
        private bool  _fleeing     = false;
        private float _fleeTimer   = 0f;
        private float _fleeYaw     = 0f;

        public void Update(Entity entity, float dt, Vector3 playerPosition)
        {
            // ── Huida ─────────────────────────────────────────────────
            if (_fleeing)
            {
                _fleeTimer -= dt;
                if (_fleeTimer <= 0f)
                {
                    _fleeing = false;
                }
                else
                {
                    float speed = entity.Definition.MoveSpeed * 1.5f;
                    entity.Velocity.X = -(float)Math.Sin(_fleeYaw) * speed;
                    entity.Velocity.Z = -(float)Math.Cos(_fleeYaw) * speed;
                    entity.Facing     = _fleeYaw + MathHelper.Pi;
                    return;
                }
            }

            // ── Vagabundeo ────────────────────────────────────────────
            _timer -= dt;
            if (_timer <= 0f)
            {
                if (_paused)
                {
                    // Termina pausa, elige dirección nueva
                    _paused      = false;
                    _targetYaw   = (float)(_rng.NextDouble() * MathHelper.TwoPi);
                    _targetTimer = WanderIntervalMin
                                 + (float)_rng.NextDouble() * (WanderIntervalMax - WanderIntervalMin);
                    _timer       = _targetTimer;
                }
                else
                {
                    // Termina caminata, comienza pausa
                    _paused = true;
                    _timer  = WanderPauseMin
                            + (float)_rng.NextDouble() * (WanderPauseMax - WanderPauseMin);
                }
            }

            if (_paused)
            {
                entity.Velocity.X = MathHelper.Lerp(entity.Velocity.X, 0f, Math.Min(1f, dt * 10f));
                entity.Velocity.Z = MathHelper.Lerp(entity.Velocity.Z, 0f, Math.Min(1f, dt * 10f));
            }
            else
            {
                float speed = entity.Definition.MoveSpeed;
                float tx    = -(float)Math.Sin(_targetYaw) * speed;
                float tz    = -(float)Math.Cos(_targetYaw) * speed;
                entity.Velocity.X = MathHelper.Lerp(entity.Velocity.X, tx, Math.Min(1f, dt * 5f));
                entity.Velocity.Z = MathHelper.Lerp(entity.Velocity.Z, tz, Math.Min(1f, dt * 5f));

                float hspd = (float)Math.Sqrt(entity.Velocity.X * entity.Velocity.X
                                            + entity.Velocity.Z * entity.Velocity.Z);
                if (hspd > 0.1f)
                    entity.Facing = (float)Math.Atan2(-entity.Velocity.X, -entity.Velocity.Z);
            }
        }

        public void OnDamaged(Entity entity, float amount, Vector3 sourcePosition)
        {
            // Huye alejándose del origen del daño
            Vector3 diff = entity.Position - sourcePosition;
            diff.Y = 0f;
            if (diff.LengthSquared() < 0.001f)
                diff = new Vector3(1f, 0f, 0f);
            diff.Normalize();

            _fleeYaw   = (float)Math.Atan2(-diff.X, -diff.Z);
            _fleeing   = true;
            _fleeTimer = FleeDuration;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  PassiveAI — igual a WanderAI pero huye más rápido y más lejos
    // ──────────────────────────────────────────────────────────────────
    public sealed class PassiveAI : IEntityAI
    {
        private readonly WanderAI _inner = new WanderAI();

        public void Update(Entity entity, float dt, Vector3 playerPosition)
            => _inner.Update(entity, dt, playerPosition);

        public void OnDamaged(Entity entity, float amount, Vector3 sourcePosition)
        {
            // Delega a WanderAI el comportamiento de huida base
            _inner.OnDamaged(entity, amount, sourcePosition);
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  HostileAI — persigue al jugador si está cerca, ataca si está muy cerca
    // ──────────────────────────────────────────────────────────────────
    public sealed class HostileAI : IEntityAI
    {
        private const float AggroRange    = 12f;
        private const float AttackRange   = 1.5f;
        private const float AttackCooldown = 1.2f;
        private const float DeAggroRange  = 20f;

        private bool  _aggroed     = false;
        private float _attackTimer = 0f;

        public float LastAttackDamage { get; private set; } = 0f;
        public bool  DidAttackThisFrame { get; private set; } = false;

        public void Update(Entity entity, float dt, Vector3 playerPosition)
        {
            DidAttackThisFrame = false;
            _attackTimer = Math.Max(0f, _attackTimer - dt);

            float distSq = Vector3.DistanceSquared(
                new Vector3(entity.Position.X, 0f, entity.Position.Z),
                new Vector3(playerPosition.X,  0f, playerPosition.Z));

            float dist = (float)Math.Sqrt(distSq);

            // Aggro / de-aggro
            if (!_aggroed && dist < AggroRange)  _aggroed = true;
            if (_aggroed  && dist > DeAggroRange) _aggroed = false;

            if (!_aggroed)
            {
                // Sin aggro: quédarse quieto
                entity.Velocity.X = MathHelper.Lerp(entity.Velocity.X, 0f, Math.Min(1f, dt * 8f));
                entity.Velocity.Z = MathHelper.Lerp(entity.Velocity.Z, 0f, Math.Min(1f, dt * 8f));
                return;
            }

            Vector3 toPlayer = playerPosition - entity.Position;
            toPlayer.Y = 0f;
            if (toPlayer.LengthSquared() > 0.001f) toPlayer.Normalize();

            // ── Persecución ───────────────────────────────────────────
            if (dist > AttackRange)
            {
                float speed = entity.Definition.MoveSpeed;
                entity.Velocity.X = MathHelper.Lerp(entity.Velocity.X, toPlayer.X * speed, Math.Min(1f, dt * 6f));
                entity.Velocity.Z = MathHelper.Lerp(entity.Velocity.Z, toPlayer.Z * speed, Math.Min(1f, dt * 6f));
                entity.Facing = (float)Math.Atan2(-toPlayer.X, -toPlayer.Z);
            }
            else
            {
                // ── Ataque ────────────────────────────────────────────
                entity.Velocity.X = MathHelper.Lerp(entity.Velocity.X, 0f, Math.Min(1f, dt * 10f));
                entity.Velocity.Z = MathHelper.Lerp(entity.Velocity.Z, 0f, Math.Min(1f, dt * 10f));

                if (_attackTimer <= 0f)
                {
                    _attackTimer       = AttackCooldown;
                    LastAttackDamage   = 3f;
                    DidAttackThisFrame = true;
                }
            }
        }

        public void OnDamaged(Entity entity, float amount, Vector3 sourcePosition)
        {
            // Al recibir daño siempre se agro
            _aggroed = true;
        }
    }

    // ──────────────────────────────────────────────────────────────────
    //  Factory
    // ──────────────────────────────────────────────────────────────────
    public static class EntityAIFactory
    {
        public static IEntityAI Create(EntityAIType type) => type switch
        {
            EntityAIType.Wander  => new WanderAI(),
            EntityAIType.Passive => new PassiveAI(),
            EntityAIType.Hostile => new HostileAI(),
            _                    => null,
        };
    }
}