using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Catálogo global de EntityDefinitions, accesible por Id.
    ///
    /// Uso:
    ///   EntityRegistry.Register(EntityDefinitions.Pig);
    ///   var def = EntityRegistry.Get(EntityIds.Pig);
    /// </summary>
    public static class EntityRegistry
    {
        private static readonly Dictionary<int, EntityDefinition> _defs
            = new Dictionary<int, EntityDefinition>();

        public static void Register(EntityDefinition def)
        {
            if (_defs.ContainsKey(def.Id))
                throw new InvalidOperationException(
                    $"EntityDefinition with Id {def.Id} ('{def.Name}') is already registered.");
            _defs[def.Id] = def;
        }

        public static EntityDefinition Get(int id)
        {
            if (_defs.TryGetValue(id, out var def)) return def;
            throw new KeyNotFoundException($"No EntityDefinition registered for Id {id}.");
        }

        public static bool TryGet(int id, out EntityDefinition def)
            => _defs.TryGetValue(id, out def);

        public static IEnumerable<EntityDefinition> All => _defs.Values;
    }

    // ──────────────────────────────────────────────────────────────────
    //  IDs numéricos de entidades — constantes para no usar magic numbers
    // ──────────────────────────────────────────────────────────────────
    public static class EntityIds
    {
        public const int Pig        = 1;
        public const int Fern       = 2;   // planta estática looteada
        public const int Mushroom   = 3;   // planta estática
        public const int Sheep      = 4;
        public const int Zombie     = 5;   // hostil simple
    }

    // ──────────────────────────────────────────────────────────────────
    //  Definiciones concretas — agregar aquí al crear nuevas entidades
    // ──────────────────────────────────────────────────────────────────
    public static class EntityDefinitions
    {
        /// <summary>Registra todas las definiciones builtin en el EntityRegistry.</summary>
        public static void RegisterAll()
        {
            EntityRegistry.Register(Pig);
            EntityRegistry.Register(Fern);
            EntityRegistry.Register(Mushroom);
            EntityRegistry.Register(Sheep);
            EntityRegistry.Register(Zombie);
        }

        // ── Cerdo ─────────────────────────────────────────────────────
        public static readonly EntityDefinition Pig = new EntityDefinition(
            id:                EntityIds.Pig,
            name:              "Pig",
            maxHealth:         10f,
            moveSpeed:         2.8f,
            collisionRadius:   0.4f,
            collisionHeight:   0.9f,
            aiType:            EntityAIType.Wander,
            isStatic:          false,
            shape:             EntityShape.QuadruPed,
            visualScale:       1.0f,
            primaryColor:      new Color(240, 175, 160),
            secondaryColor:    new Color(215, 140, 125),
            accentColor:       new Color(190, 100,  95),
            hitParticleColor:  new Color(240, 175, 160),
            deathParticleColor:new Color(200, 90, 80),
            lootTable: new[]
            {
                new LootEntry(itemId: 1, min: 1, max: 3, chance: 1.0f),  // carne
            });

        // ── Helecho (planta, sin IA) ──────────────────────────────────
        public static readonly EntityDefinition Fern = new EntityDefinition(
            id:                EntityIds.Fern,
            name:              "Fern",
            maxHealth:         1f,
            moveSpeed:         0f,
            collisionRadius:   0.3f,
            collisionHeight:   0.7f,
            aiType:            EntityAIType.None,
            isStatic:          true,
            shape:             EntityShape.Plant,
            visualScale:       0.9f,
            primaryColor:      new Color( 60, 160,  55),
            secondaryColor:    new Color( 40, 120,  38),
            accentColor:       new Color( 90, 200,  70),
            hitParticleColor:  new Color( 90, 200,  70),
            deathParticleColor:new Color( 60, 160,  55),
            lootTable: new[]
            {
                new LootEntry(itemId: 2, min: 1, max: 2, chance: 0.8f),  // semilla
            });

        // ── Seta (planta, sin IA) ─────────────────────────────────────
        public static readonly EntityDefinition Mushroom = new EntityDefinition(
            id:                EntityIds.Mushroom,
            name:              "Mushroom",
            maxHealth:         1f,
            moveSpeed:         0f,
            collisionRadius:   0.25f,
            collisionHeight:   0.5f,
            aiType:            EntityAIType.None,
            isStatic:          true,
            shape:             EntityShape.Plant,
            visualScale:       0.7f,
            primaryColor:      new Color(180,  60,  40),
            secondaryColor:    new Color(220, 210, 195),
            accentColor:       new Color(240, 230, 210),
            hitParticleColor:  new Color(180,  60,  40),
            deathParticleColor:new Color(180,  60,  40),
            lootTable: new[]
            {
                new LootEntry(itemId: 3, min: 1, max: 1, chance: 1.0f),  // seta
            });

        // ── Oveja ─────────────────────────────────────────────────────
        public static readonly EntityDefinition Sheep = new EntityDefinition(
            id:                EntityIds.Sheep,
            name:              "Sheep",
            maxHealth:         8f,
            moveSpeed:         2.4f,
            collisionRadius:   0.45f,
            collisionHeight:   1.0f,
            aiType:            EntityAIType.Passive,
            isStatic:          false,
            shape:             EntityShape.QuadruPed,
            visualScale:       1.05f,
            primaryColor:      new Color(235, 232, 228),
            secondaryColor:    new Color(200, 196, 190),
            accentColor:       new Color(160, 140, 120),
            hitParticleColor:  new Color(235, 232, 228),
            deathParticleColor:new Color(200, 196, 190),
            lootTable: new[]
            {
                new LootEntry(itemId: 4, min: 1, max: 3, chance: 1.0f),  // lana
            });

        // ── Zombie (hostil) ───────────────────────────────────────────
        public static readonly EntityDefinition Zombie = new EntityDefinition(
            id:                EntityIds.Zombie,
            name:              "Zombie",
            maxHealth:         20f,
            moveSpeed:         1.8f,
            collisionRadius:   0.4f,
            collisionHeight:   1.8f,
            aiType:            EntityAIType.Hostile,
            isStatic:          false,
            shape:             EntityShape.BiPed,
            visualScale:       1.0f,
            primaryColor:      new Color( 80, 130,  80),
            secondaryColor:    new Color( 55, 100,  55),
            accentColor:       new Color( 40,  80,  40),
            hitParticleColor:  new Color(100, 200, 100),
            deathParticleColor:new Color( 60, 160,  60),
            lootTable: new[]
            {
                new LootEntry(itemId: 5, min: 0, max: 2, chance: 0.5f),  // carne podrida
            });
    }
}