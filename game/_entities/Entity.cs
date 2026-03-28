using Microsoft.Xna.Framework;
using System;

namespace game
{
    /// <summary>
    /// Estado actual de vida de la entidad.
    /// </summary>
    public enum EntityLifeState
    {
        Alive,
        Dying,   // muriendo (animación de muerte en curso)
        Dead,    // puede ser eliminada por el EntityManager
    }

    /// <summary>
    /// Instancia viva de una entidad.
    ///
    /// - Tiene posición, velocidad y física básica contra el mundo.
    /// - Tiene HP y puede recibir daño.
    /// - Si tiene IA, la actualiza cada frame.
    /// - Emite eventos OnDamaged y OnDied para que el EntityManager
    ///   genere partículas, loot, etc.
    ///
    /// NO hereda de nada: todo el comportamiento es composición.
    /// </summary>
    public sealed class Entity
    {
        // ── Identidad ─────────────────────────────────────────────────
        public readonly int              InstanceId;
        public readonly EntityDefinition Definition;

        // ── Física ────────────────────────────────────────────────────
        public Vector3 Position;
        public Vector3 Velocity;
        public float   Facing;          // yaw en radianes
        public bool    IsGrounded;

        // ── Visual ─────────────────────────────────────────────────────
        /// <summary>Posición suavizada para el renderer (bob, step-up, etc.)</summary>
        public Vector3 VisualPosition { get; private set; }
        private float  _visualYDebt;

        /// <summary>0-1: progreso de animación de muerte.</summary>
        public float DeathAnimProgress { get; private set; }

        // ── Stats ─────────────────────────────────────────────────────
        public float CurrentHealth { get; private set; }
        public float MaxHealth     => Definition.MaxHealth;

        // ── Vida ──────────────────────────────────────────────────────
        public EntityLifeState LifeState { get; private set; } = EntityLifeState.Alive;
        private const float DeathAnimDuration = 0.5f;
        private float _deathTimer = 0f;

        // ── IA ────────────────────────────────────────────────────────
        private readonly IEntityAI _ai;

        // ── Invencibilidad temporal tras golpe ────────────────────────
        private const float IFrameDuration = 0.25f;
        private float _iFrameTimer = 0f;

        // ── Hitstop visual ────────────────────────────────────────────
        public  float HitFlashTimer { get; private set; } = 0f;
        private const float HitFlashDuration = 0.12f;

        // ── Eventos (suscritos por EntityManager) ─────────────────────
        /// <summary>Lanzado justo después de recibir daño (position, amount).</summary>
        public event Action<Vector3, float> OnDamaged;

        /// <summary>Lanzado cuando HP llega a 0 y comienza la animación de muerte.</summary>
        public event Action<Vector3> OnDied;

        // ─────────────────────────────────────────────────────────────
        public Entity(int instanceId, EntityDefinition definition, Vector3 spawnPosition)
        {
            InstanceId      = instanceId;
            Definition      = definition;
            Position        = spawnPosition;
            VisualPosition  = spawnPosition;
            CurrentHealth   = definition.MaxHealth;
            _ai             = EntityAIFactory.Create(definition.AIType);
        }

        // ─────────────────────────────────────────────────────────────
        //  Update principal
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// physicsEnabled: false cuando la entidad está fuera de zona HQ.
        /// En ese caso se congela gravedad y colisiones (los chunks LP/VLP
        /// no tienen datos de bloque accesibles en tiempo real).
        /// La IA sigue corriendo para que el estado sea coherente al
        /// volver a entrar en zona HQ.
        /// </summary>
        public void Update(float dt, Vector3 playerPosition,
                           ChunkManager chunkManager, bool physicsEnabled = true)
        {
            // ── Animación de muerte ───────────────────────────────────
            if (LifeState == EntityLifeState.Dying)
            {
                _deathTimer      += dt;
                DeathAnimProgress = Math.Min(_deathTimer / DeathAnimDuration, 1f);
                if (_deathTimer >= DeathAnimDuration)
                    LifeState = EntityLifeState.Dead;
                return;
            }

            if (LifeState == EntityLifeState.Dead) return;

            // ── Timers ────────────────────────────────────────────────
            _iFrameTimer  = Math.Max(0f, _iFrameTimer  - dt);
            HitFlashTimer = Math.Max(0f, HitFlashTimer - dt);

            // ── IA ────────────────────────────────────────────────────
            // Corre siempre: mantiene estado consistente aunque no haya física.
            if (!Definition.IsStatic && _ai != null)
                _ai.Update(this, dt, playerPosition);

            // ── Física ───────────────────────────────────────────────
            // Solo si hay chunks HQ cargados bajo la entidad.
            // Fuera de esa zona se cancela la velocidad vertical acumulada
            // para evitar que explote cuando vuelvan los chunks.
            if (!Definition.IsStatic)
            {
                if (physicsEnabled)
                {
                    ApplyPhysics(dt, chunkManager);
                }
                else
                {
                    // Congelar eje Y: limpiar velocidad vertical y marcar grounded
                    // para que no acumule caída libre durante segundos.
                    Velocity.Y = 0f;
                    IsGrounded  = true;

                    // Movimiento horizontal de IA sin colisiones: aceptable porque
                    // en zona LP el terreno es relativamente plano y el error es
                    // imperceptible a esa distancia.
                    Position.X += Velocity.X * dt;
                    Position.Z += Velocity.Z * dt;
                }
            }

            // ── Visual position ───────────────────────────────────────
            UpdateVisualPosition(dt);
        }

        // ─────────────────────────────────────────────────────────────
        //  Daño
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Aplica daño a la entidad.
        /// Respeta frames de invencibilidad.
        /// Si HP llega a 0, inicia secuencia de muerte.
        /// </summary>
        /// <returns>True si el golpe conectó (no estaba en i-frames).</returns>
        public bool TakeDamage(float amount, Vector3 sourcePosition)
        {
            if (LifeState != EntityLifeState.Alive) return false;
            if (_iFrameTimer > 0f) return false;

            CurrentHealth  = Math.Max(0f, CurrentHealth - amount);
            _iFrameTimer   = IFrameDuration;
            HitFlashTimer  = HitFlashDuration;

            _ai?.OnDamaged(this, amount, sourcePosition);
            OnDamaged?.Invoke(Position, amount);

            if (CurrentHealth <= 0f)
            {
                LifeState = EntityLifeState.Dying;
                _deathTimer = 0f;
                DeathAnimProgress = 0f;
                OnDied?.Invoke(Position);
            }

            return true;
        }

        /// <summary>
        /// Cura HP (no supera MaxHealth).
        /// </summary>
        public void Heal(float amount)
        {
            if (LifeState != EntityLifeState.Alive) return;
            CurrentHealth = Math.Min(MaxHealth, CurrentHealth + amount);
        }

        // ─────────────────────────────────────────────────────────────
        //  Física básica (gravedad + colisión AABB contra el mundo)
        // ─────────────────────────────────────────────────────────────
        private const float Gravity      = -20f;
        private const float MaxFallSpeed = 40f;
        private const float MaxStepSize  = 0.35f;

        private void ApplyPhysics(float dt, ChunkManager chunkManager)
        {
            Velocity.Y = Math.Max(Velocity.Y + Gravity * dt, -MaxFallSpeed);

            float totalDx = Velocity.X * dt;
            float totalDy = Velocity.Y * dt;
            float totalDz = Velocity.Z * dt;

            float maxMove = Math.Max(Math.Abs(totalDx),
                            Math.Max(Math.Abs(totalDy), Math.Abs(totalDz)));
            int steps = Math.Max(1, (int)Math.Ceiling(maxMove / MaxStepSize));

            float sdx = totalDx / steps;
            float sdy = totalDy / steps;
            float sdz = totalDz / steps;

            for (int i = 0; i < steps; i++)
            {
                if (!Collides(Position + new Vector3(sdx, 0f, 0f), chunkManager))
                    Position.X += sdx;
                else
                {
                    Velocity.X = 0f; sdx = 0f;
                }

                if (!Collides(Position + new Vector3(0f, 0f, sdz), chunkManager))
                    Position.Z += sdz;
                else
                {
                    Velocity.Z = 0f; sdz = 0f;
                }

                if (!Collides(Position + new Vector3(0f, sdy, 0f), chunkManager))
                {
                    Position.Y += sdy;
                    if (sdy < 0f) IsGrounded = false;
                }
                else
                {
                    IsGrounded = sdy < 0f;
                    Velocity.Y = 0f;
                    sdy = 0f;
                }
            }

            if (IsGrounded && !Collides(Position + new Vector3(0f, -0.05f, 0f), chunkManager))
                IsGrounded = false;

            if (Position.Y < -200f) { Position.Y = 200f; Velocity = Vector3.Zero; }
        }

        private bool Collides(Vector3 feetPos, ChunkManager chunkManager)
        {
            float hw   = Definition.CollisionRadius;
            float h    = Definition.CollisionHeight;
            const float eps = 0.001f;

            int x0 = (int)Math.Floor(feetPos.X - hw);
            int x1 = (int)Math.Floor(feetPos.X + hw - eps);
            int y0 = (int)Math.Floor(feetPos.Y);
            int y1 = (int)Math.Floor(feetPos.Y + h - eps);
            int z0 = (int)Math.Floor(feetPos.Z - hw);
            int z1 = (int)Math.Floor(feetPos.Z + hw - eps);

            for (int x = x0; x <= x1; x++)
            for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
            {
                byte b = chunkManager.GetBlockAtWorldPosition(
                    new Vector3(x + 0.5f, y + 0.5f, z + 0.5f));
                if (b != BlockType.Air && b != BlockType.AirCave && b != BlockType.Water)
                    return true;
            }
            return false;
        }

        // ─────────────────────────────────────────────────────────────
        //  Visual position (suavizado)
        // ─────────────────────────────────────────────────────────────
        private const float StepSmoothSpeed = 10f;

        private void UpdateVisualPosition(float dt)
        {
            if (_visualYDebt > 0f)
            {
                float recover = Math.Min(_visualYDebt, StepSmoothSpeed * dt);
                _visualYDebt  = Math.Max(0f, _visualYDebt - recover);
                VisualPosition = new Vector3(Position.X, Position.Y - _visualYDebt, Position.Z);
            }
            else
            {
                _visualYDebt   = 0f;
                VisualPosition = Position;
            }
        }
    }
}