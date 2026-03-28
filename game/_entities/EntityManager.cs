using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;

namespace game
{
    public sealed class EntityManager : IDisposable
    {
        // ── Entidades ─────────────────────────────────────────────────
        private readonly List<Entity> _entities = new List<Entity>(256);
        private readonly List<Entity> _toRemove = new List<Entity>(16);
        private int _nextInstanceId = 1;

        // ── Render ────────────────────────────────────────────────────
        private readonly EntityRenderer _renderer;

        // ── Partículas ────────────────────────────────────────────────
        private readonly ParticleSystem _particles;

        // ── Loot ──────────────────────────────────────────────────────
        public event Action<Vector3, LootEntry[]> OnLootDropped;

        // ── Stats para debug HUD ──────────────────────────────────────
        public int EntityCount   => _entities.Count;
        public int ParticleCount => _particles.AliveCount;

        // ── Constantes de spawn: animales ─────────────────────────────
        private const int   MaxAnimalsPerType    = 8;
        private const float AnimalSpawnInterval  = 6f;
        private const float MinSpawnDist         = 30f;
        private const float MaxSpawnDist         = 30f;

        // ── Constantes de spawn: vegetación ──────────────────────────
        private const int MaxVegetationPerType = 20;
        private const int VegetationGridSize   = 5;

        // ── Estado de spawn ───────────────────────────────────────────
        private readonly Dictionary<int, float> _spawnCooldowns = new();
        private readonly Random _spawnRng = new Random();

        // ─────────────────────────────────────────────────────────────
        public EntityManager(GraphicsDevice gd, ParticleSystem particles)
        {
            _renderer  = new EntityRenderer(gd);
            _particles = particles;
        }

        // ─────────────────────────────────────────────────────────────
        //  Spawn / despawn
        // ─────────────────────────────────────────────────────────────

        public Entity Spawn(int definitionId, Vector3 position)
        {
            if (!EntityRegistry.TryGet(definitionId, out var def))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[EntityManager] Spawn: no definition for Id {definitionId}");
                return null;
            }

            var entity = new Entity(_nextInstanceId++, def, position);

            entity.OnDamaged += (pos, amount) => OnEntityDamaged(entity, pos, amount);
            entity.OnDied    += pos            => OnEntityDied(entity, pos);

            _entities.Add(entity);
            return entity;
        }

        public void Despawn(Entity entity) => _entities.Remove(entity);

        public void Clear() => _entities.Clear();

        // ─────────────────────────────────────────────────────────────
        //  Hit del jugador
        // ─────────────────────────────────────────────────────────────

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

        public void Update(GameTime gameTime, Vector3 playerPosition,
                           ChunkManager chunkManager, int loadDistance,
                           WorldGenerator worldGen)
        {
            float dt = Math.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 0.05f);

            float chunkSize  = 32f;
            float hqRadius   = (loadDistance + 0.5f) * chunkSize;
            float hqRadiusSq = hqRadius * hqRadius;
            float cullRadius = (loadDistance + 1) * chunkSize;
            float cullRadiusSq = cullRadius * cullRadius;

            // ── Actualizar entidades existentes ───────────────────────
            foreach (var entity in _entities)
            {
                float dx     = entity.Position.X - playerPosition.X;
                float dz     = entity.Position.Z - playerPosition.Z;
                float distSq = dx * dx + dz * dz;

                if (distSq > cullRadiusSq)
                {
                    _toRemove.Add(entity);
                    continue;
                }

                bool physicsEnabled = distSq <= hqRadiusSq;
                entity.Update(dt, playerPosition, chunkManager, physicsEnabled);

                if (entity.LifeState == EntityLifeState.Dead)
                    _toRemove.Add(entity);
            }

            foreach (var e in _toRemove) _entities.Remove(e);
            _toRemove.Clear();

            // ── Spawn ─────────────────────────────────────────────────
            TrySpawnAnimals(dt, playerPosition, worldGen);
            TrySpawnVegetation(playerPosition, worldGen);

            // ── Partículas ────────────────────────────────────────────
            _particles.Update(dt);
        }

        // ─────────────────────────────────────────────────────────────
        //  Spawn: animales
        // ─────────────────────────────────────────────────────────────

        private void TrySpawnAnimals(float dt, Vector3 playerPos, WorldGenerator worldGen)
        {
            int[] types = { EntityIds.Pig, EntityIds.Sheep };

            foreach (int typeId in types)
            {
                // ── cooldown por tipo ─────────────────────────────────
                if (!_spawnCooldowns.ContainsKey(typeId))
                    _spawnCooldowns[typeId] = 0f;

                _spawnCooldowns[typeId] -= dt;
                if (_spawnCooldowns[typeId] > 0f) continue;

                // ── cuota por tipo ────────────────────────────────────
                int count = 0;
                foreach (var e in _entities)
                    if (e.Definition.Id == typeId && e.LifeState == EntityLifeState.Alive)
                        count++;

                if (count >= MaxAnimalsPerType)
                {
                    _spawnCooldowns[typeId] = AnimalSpawnInterval;
                    continue;
                }

                // ── buscar posición válida (3 intentos) ───────────────
                bool spawned = false;
                for (int attempt = 0; attempt < 3 && !spawned; attempt++)
                {
                    float angle    = (float)(_spawnRng.NextDouble() * Math.PI * 2);
                    float distance = MinSpawnDist +
                                     (float)_spawnRng.NextDouble() * (MaxSpawnDist - MinSpawnDist);

                    float x = playerPos.X + (float)Math.Cos(angle) * distance;
                    float z = playerPos.Z + (float)Math.Sin(angle) * distance;
                    float y = worldGen.GetSurfaceHeight(x, z);

                    if (y <= WorldGenerator.SeaLevel) continue;

                    Spawn(typeId, new Vector3(x, y + 1f, z));
                    spawned = true;
                }

                _spawnCooldowns[typeId] = AnimalSpawnInterval;
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Spawn: vegetación (determinista por seed)
        // ─────────────────────────────────────────────────────────────

        private void TrySpawnVegetation(Vector3 playerPos, WorldGenerator worldGen)
        {
            int gridOriginX = (int)Math.Floor(playerPos.X / VegetationGridSize) * VegetationGridSize;
            int gridOriginZ = (int)Math.Floor(playerPos.Z / VegetationGridSize) * VegetationGridSize;

            const int CheckRadius = 12;

            for (int cx = -CheckRadius; cx <= CheckRadius; cx++)
            for (int cz = -CheckRadius; cz <= CheckRadius; cz++)
            {
                int wx = gridOriginX + cx * VegetationGridSize;
                int wz = gridOriginZ + cz * VegetationGridSize;

                // ── decisión determinista para esta celda ─────────────
                float rnd     = worldGen.HashCell(wx, wz, seed: 900);
                float typeRnd = worldGen.HashCell(wx, wz, seed: 901);

                float surfaceY = worldGen.GetSurfaceHeight(wx, wz);

                worldGen.GetBiomeWeights(wx, wz,
                    out float wPlains, out float wForest, out float wTaiga,
                    out _, out _, out _);

                // ── filtros de terreno ────────────────────────────────
                if (surfaceY <= WorldGenerator.SeaLevel)        continue;
                if (surfaceY >  WorldGenerator.SeaLevel + 45f)  continue;

                // ── probabilidad por bioma ────────────────────────────
                float fernProb     = wPlains * 0.08f + wForest * 0.55f + wTaiga * 0.40f;
                float mushroomProb = wPlains * 0.02f + wForest * 0.18f + wTaiga * 0.28f;

                // Hongos solo en zonas bajas y húmedas
                if (surfaceY > WorldGenerator.SeaLevel + 20f) mushroomProb = 0f;

                float totalProb = fernProb + mushroomProb;
                if (rnd > totalProb) continue;

                int entityTypeId = typeRnd < fernProb / (totalProb + 0.0001f)
                    ? EntityIds.Fern
                    : EntityIds.Mushroom;

                // ── cuota por tipo ────────────────────────────────────
                int count = 0;
                foreach (var e in _entities)
                    if (e.Definition.Id == entityTypeId && e.LifeState == EntityLifeState.Alive)
                        count++;

                if (count >= MaxVegetationPerType) continue;

                // ── ¿ya existe una planta en esta celda? ──────────────
                var spawnPos    = new Vector3(wx, surfaceY + 1f, wz);
                bool alreadyExists = false;
                foreach (var e in _entities)
                {
                    if (e.Definition.Id != EntityIds.Fern &&
                        e.Definition.Id != EntityIds.Mushroom) continue;

                    if (Math.Abs(e.Position.X - spawnPos.X) < VegetationGridSize * 0.5f &&
                        Math.Abs(e.Position.Z - spawnPos.Z) < VegetationGridSize * 0.5f)
                    {
                        alreadyExists = true;
                        break;
                    }
                }
                if (alreadyExists) continue;

                Spawn(entityTypeId, spawnPos);
            }
        }

        // ─────────────────────────────────────────────────────────────
        //  Draw
        // ─────────────────────────────────────────────────────────────

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
            if (entity.IsGrounded)
                _particles.Emit(ParticlePresets.Dust(pos));
        }

        private void OnEntityDied(Entity entity, Vector3 pos)
        {
            _particles.Emit(ParticlePresets.Death(pos, entity.Definition.DeathParticleColor));
            DropLoot(entity, pos);
        }

        private void DropLoot(Entity entity, Vector3 pos)
        {
            if (entity.Definition.LootTable.Count == 0) return;
            if (OnLootDropped == null) return;

            var drops = new List<LootEntry>();
            foreach (var entry in entity.Definition.LootTable)
            {
                if (_spawnRng.NextDouble() <= entry.Chance)
                {
                    int count = entry.MinCount +
                                _spawnRng.Next(entry.MaxCount - entry.MinCount + 1);
                    drops.Add(new LootEntry(entry.ItemId, count, count, 1f));
                }
            }

            if (drops.Count > 0)
                OnLootDropped?.Invoke(pos, drops.ToArray());
        }

        // ─────────────────────────────────────────────────────────────
        //  Acceso de lectura
        // ─────────────────────────────────────────────────────────────

        public IReadOnlyList<Entity> Entities => _entities;

        public void Dispose()
        {
            _renderer?.Dispose();
        }
    }
}