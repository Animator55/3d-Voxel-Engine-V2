using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Gestiona el ciclo de vida completo de todas las entidades del mundo.
    ///
    /// Responsabilidades:
    ///   - Spawn / despawn de entidades.
    ///   - Update de física + IA.
    ///   - Renderizado en un único pass.
    ///   - Generación de partículas al recibir daño / morir.
    ///   - (Placeholder) Generación de loot al morir.
    ///   - Detección de hit del jugador (listo para conectar con PlayerRenderer.HitStopTimer).
    ///
    /// Uso en Game1:
    ///   // LoadContent
    ///   _entityManager = new EntityManager(GraphicsDevice, _particleSystem);
    ///   _entityManager.Spawn(EntityIds.Pig, new Vector3(10, 65, 10));
    ///   _entityManager.Spawn(EntityIds.Fern, new Vector3(5, 64, 8));
    ///
    ///   // Update
    ///   _entityManager.Update(gameTime, playerPosition, _chunkManager);
    ///
    ///   // Draw (antes del HUD)
    ///   _entityManager.Draw(gameTime, view, projection);
    /// </summary>
    public sealed class EntityManager : IDisposable
    {
        // ── Entidades ─────────────────────────────────────────────────
        private readonly List<Entity>           _entities  = new List<Entity>(256);
        private readonly List<Entity>           _toRemove  = new List<Entity>(16);
        private int _nextInstanceId = 1;

        // ── Render ────────────────────────────────────────────────────
        private readonly EntityRenderer _renderer;

        // ── Partículas ────────────────────────────────────────────────
        private readonly ParticleSystem _particles;

        // ── Loot (stub — conectar con sistema de inventario) ──────────
        public event Action<Vector3, LootEntry[]> OnLootDropped;

        // ── Stats accesibles para debug HUD ───────────────────────────
        public int EntityCount => _entities.Count;
        public int ParticleCount => _particles.AliveCount;

        // ─────────────────────────────────────────────────────────────
        public EntityManager(GraphicsDevice gd, ParticleSystem particles)
        {
            _renderer  = new EntityRenderer(gd);
            _particles = particles;
        }

        // ─────────────────────────────────────────────────────────────
        //  API de spawn / despawn
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Crea una nueva instancia de una entidad según su definitionId.
        /// </summary>
        /// <returns>La instancia creada, o null si el Id no está registrado.</returns>
        public Entity Spawn(int definitionId, Vector3 position)
        {
            if (!EntityRegistry.TryGet(definitionId, out var def))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EntityManager] Spawn: no definition for Id {definitionId}");
                return null;
            }

            var entity = new Entity(_nextInstanceId++, def, position);

            // Suscribir eventos para partículas + loot
            entity.OnDamaged += (pos, amount) => OnEntityDamaged(entity, pos, amount);
            entity.OnDied    += pos            => OnEntityDied(entity, pos);

            _entities.Add(entity);
            return entity;
        }

        /// <summary>Elimina una entidad inmediatamente (sin animación).</summary>
        public void Despawn(Entity entity)
        {
            _entities.Remove(entity);
        }

        /// <summary>Elimina todas las entidades.</summary>
        public void Clear() => _entities.Clear();

        // ─────────────────────────────────────────────────────────────
        //  Hit del jugador → TakeDamage
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Intenta golpear la entidad más cercana al jugador dentro del rango.
        /// Diseñado para ser llamado desde Game1 cuando PlayerRenderer.HitStopTimer > 0.
        ///
        /// Returns: la entidad golpeada, o null si no había ninguna.
        /// </summary>
        public Entity TryHitNearest(Vector3 playerPosition, float reach, float damage)
        {
            Entity closest      = null;
            float  closestDistSq = reach * reach;

            foreach (var e in _entities)
            {
                if (e.LifeState != EntityLifeState.Alive) continue;

                float distSq = Vector3.DistanceSquared(
                    new Vector3(playerPosition.X, 0f, playerPosition.Z),
                    new Vector3(e.Position.X,     0f, e.Position.Z));

                if (distSq < closestDistSq)
                {
                    closestDistSq = distSq;
                    closest       = e;
                }
            }

            if (closest != null)
                closest.TakeDamage(damage, playerPosition);

            return closest;
        }

        /// <summary>
        /// Aplica daño a todas las entidades dentro de una esfera.
        /// Útil para ataques en área.
        /// </summary>
        public void DamageRadius(Vector3 origin, float radius, float damage)
        {
            float rSq = radius * radius;
            foreach (var e in _entities)
            {
                if (e.LifeState != EntityLifeState.Alive) continue;
                if (Vector3.DistanceSquared(e.Position, origin) < rSq)
                    e.TakeDamage(damage, origin);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Update
        // ─────────────────────────────────────────────────────────────

        public void Update(GameTime gameTime, Vector3 playerPosition, ChunkManager chunkManager)
        {
            float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);

            foreach (var entity in _entities)
            {
                entity.Update(dt, playerPosition, chunkManager);

                if (entity.LifeState == EntityLifeState.Dead)
                    _toRemove.Add(entity);
            }

            // Limpiar muertas
            foreach (var e in _toRemove)
                _entities.Remove(e);
            _toRemove.Clear();

            // Actualizar partículas
            _particles.Update(dt);
        }

        // ─────────────────────────────────────────────────────────────
        //  Draw
        // ─────────────────────────────────────────────────────────────

        /// <summary>
        /// Dibuja todas las entidades vivas/muriendo + partículas.
        /// Llamar después del render de chunks y antes del HUD.
        /// </summary>
        public void Draw(GameTime gameTime, Matrix view, Matrix projection)
        {
            foreach (var entity in _entities)
            {
                if (entity.LifeState == EntityLifeState.Dead) continue;
                _renderer.Add(entity, gameTime);
            }

            _renderer.Flush(view, projection);
            _particles.Draw(view, projection);
        }

        // ─────────────────────────────────────────────────────────────
        //  Handlers de eventos de entidades
        // ─────────────────────────────────────────────────────────────

        private void OnEntityDamaged(Entity entity, Vector3 pos, float amount)
        {
            _particles.Emit(ParticlePresets.Hit(pos + Vector3.UnitY * 0.5f,
                                                entity.Definition.HitParticleColor));

            // Polvo en los pies si está en el suelo
            if (entity.IsGrounded)
                _particles.Emit(ParticlePresets.Dust(pos));
        }

        private void OnEntityDied(Entity entity, Vector3 pos)
        {
            _particles.Emit(ParticlePresets.Death(pos, entity.Definition.DeathParticleColor));

            // Loot
            DropLoot(entity, pos);
        }

        private void DropLoot(Entity entity, Vector3 pos)
        {
            if (entity.Definition.LootTable.Count == 0) return;
            if (OnLootDropped == null) return;

            var random = new Random();
            var drops  = new List<LootEntry>();

            foreach (var entry in entity.Definition.LootTable)
            {
                if (random.NextDouble() <= entry.Chance)
                {
                    int count = entry.MinCount + random.Next(entry.MaxCount - entry.MinCount + 1);
                    drops.Add(new LootEntry(entry.ItemId, count, count, 1f));
                }
            }

            if (drops.Count > 0)
                OnLootDropped?.Invoke(pos, drops.ToArray());
        }

        // ─────────────────────────────────────────────────────────────
        //  Acceso de lectura (para HUD, serialización, etc.)
        // ─────────────────────────────────────────────────────────────

        /// <returns>Snapshot de las entidades actuales (no modificar la lista).</returns>
        public IReadOnlyList<Entity> Entities => _entities;

        public void Dispose()
        {
            _renderer?.Dispose();
        }
    }
}