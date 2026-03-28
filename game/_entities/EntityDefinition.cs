using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;

namespace game
{
    /// <summary>
    /// Tipo de comportamiento que tendrá una entidad.
    /// </summary>
    public enum EntityAIType
    {
        None,       // Sin IA (plantas, objetos estáticos)
        Wander,     // Camina aleatoriamente, huye si recibe daño
        Passive,    // Como Wander pero nunca ataca
        Hostile,    // Persigue al jugador y ataca
    }

    /// <summary>
    /// Forma visual de la entidad para el renderer.
    /// </summary>
    public enum EntityShape
    {
        Box,          // Un cubo/caja simple
        BiPed,        // Dos patas + cuerpo + cabeza (cerdo, oveja, etc.)
        QuadruPed,    // Cuatro patas + cuerpo + cabeza
        Plant,        // Forma vegetal (varios tallos/hojas)
    }

    /// <summary>
    /// Posible item de loot cuando la entidad muere.
    /// </summary>
    public readonly struct LootEntry
    {
        public readonly byte  ItemId;
        public readonly int   MinCount;
        public readonly int   MaxCount;
        public readonly float Chance;   // 0–1

        public LootEntry(byte itemId, int min, int max, float chance = 1f)
        {
            ItemId   = itemId;
            MinCount = min;
            MaxCount = max;
            Chance   = chance;
        }
    }

    /// <summary>
    /// Datos estáticos que describen un tipo de entidad.
    /// Inmutable: se comparte entre todas las instancias del mismo tipo.
    /// </summary>
    public sealed class EntityDefinition
    {
        // ── Identificación ────────────────────────────────────────────
        public readonly int    Id;
        public readonly string Name;

        // ── Stats ─────────────────────────────────────────────────────
        public readonly float MaxHealth;
        public readonly float MoveSpeed;
        public readonly float CollisionRadius;
        public readonly float CollisionHeight;

        // ── Comportamiento ────────────────────────────────────────────
        public readonly EntityAIType AIType;
        public readonly bool         IsStatic;    // no se mueve en absoluto (plantas)

        // ── Visual ────────────────────────────────────────────────────
        public readonly EntityShape Shape;
        public readonly float       VisualScale;  // multiplicador global
        public readonly Color       PrimaryColor;
        public readonly Color       SecondaryColor;
        public readonly Color       AccentColor;

        // ── Loot ──────────────────────────────────────────────────────
        public readonly IReadOnlyList<LootEntry> LootTable;

        // ── Partículas al recibir daño / morir ────────────────────────
        public readonly Color HitParticleColor;
        public float BoundingRadius { get; init; } = 2f;
        public readonly Color DeathParticleColor;

        public EntityDefinition(
            int          id,
            string       name,
            float        maxHealth,
            float        moveSpeed,
            float        collisionRadius,
            float        collisionHeight,
            EntityAIType aiType,
            bool         isStatic,
            EntityShape  shape,
            float        visualScale,
            Color        primaryColor,
            Color        secondaryColor,
            Color        accentColor,
            Color        hitParticleColor,
            Color        deathParticleColor,
            IReadOnlyList<LootEntry> lootTable = null)
        {
            Id                 = id;
            Name               = name;
            MaxHealth          = maxHealth;
            MoveSpeed          = moveSpeed;
            CollisionRadius    = collisionRadius;
            CollisionHeight    = collisionHeight;
            AIType             = aiType;
            IsStatic           = isStatic;
            Shape              = shape;
            VisualScale        = visualScale;
            PrimaryColor       = primaryColor;
            SecondaryColor     = secondaryColor;
            AccentColor        = accentColor;
            HitParticleColor   = hitParticleColor;
            DeathParticleColor = deathParticleColor;
            LootTable          = lootTable ?? Array.Empty<LootEntry>();
        }
    }
}