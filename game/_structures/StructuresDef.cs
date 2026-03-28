using System;

namespace game
{
    public static class StructuresNew
    {
        // ─────────────────────────────────────────────────────────────────────
        // CASTILLO EN RUINAS
        // Torre central cuadrada con muralla parcial derrumbada y patio.
        // Aparece en pasto y piedra; muy raro.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef RuinedCastle = new StructureDef
        {
            Name = "RuinedCastle",
            SpawnChance = 0.008f,
            MinSpacing = 280,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone },
            Blocks = new[]
            {
                // ── Torre del homenaje (keep) 5×5, 7 pisos ──
                // Capa 0
                new BlockOverride(-2,0,-2,BlockType.Stone), new BlockOverride(-1,0,-2,BlockType.Stone), new BlockOverride(0,0,-2,BlockType.Stone), new BlockOverride(1,0,-2,BlockType.Stone), new BlockOverride(2,0,-2,BlockType.Stone),
                new BlockOverride(-2,0, 2,BlockType.Stone), new BlockOverride(-1,0, 2,BlockType.Stone), new BlockOverride(0,0, 2,BlockType.Stone), new BlockOverride(1,0, 2,BlockType.Stone), new BlockOverride(2,0, 2,BlockType.Stone),
                new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-2,0, 0,BlockType.Stone), new BlockOverride(-2,0, 1,BlockType.Stone),
                new BlockOverride( 2,0,-1,BlockType.Stone), new BlockOverride( 2,0, 0,BlockType.Stone), new BlockOverride( 2,0, 1,BlockType.Stone),
                // Capas 1-6 (huecas)
                new BlockOverride(-2,1,-2,BlockType.Stone), new BlockOverride(0,1,-2,BlockType.Stone), new BlockOverride(2,1,-2,BlockType.Stone),
                new BlockOverride(-2,1, 2,BlockType.Stone), new BlockOverride(0,1, 2,BlockType.Stone), new BlockOverride(2,1, 2,BlockType.Stone),
                new BlockOverride(-2,1,-1,BlockType.Stone), new BlockOverride(-2,1, 0,BlockType.Stone), new BlockOverride(-2,1, 1,BlockType.Stone),
                new BlockOverride( 2,1,-1,BlockType.Stone), new BlockOverride( 2,1, 0,BlockType.Stone), new BlockOverride( 2,1, 1,BlockType.Stone),

                new BlockOverride(-2,2,-2,BlockType.Stone), new BlockOverride(0,2,-2,BlockType.Stone), new BlockOverride(2,2,-2,BlockType.Stone),
                new BlockOverride(-2,2, 2,BlockType.Stone), new BlockOverride(0,2, 2,BlockType.Stone), new BlockOverride(2,2, 2,BlockType.Stone),
                new BlockOverride(-2,2,-1,BlockType.Stone), new BlockOverride(-2,2, 0,BlockType.Stone), new BlockOverride(-2,2, 1,BlockType.Stone),
                new BlockOverride( 2,2,-1,BlockType.Stone), new BlockOverride( 2,2, 0,BlockType.Stone), new BlockOverride( 2,2, 1,BlockType.Stone),

                new BlockOverride(-2,3,-2,BlockType.Stone), new BlockOverride(0,3,-2,BlockType.Stone), new BlockOverride(2,3,-2,BlockType.Stone),
                new BlockOverride(-2,3, 2,BlockType.Stone), new BlockOverride(0,3, 2,BlockType.Stone), new BlockOverride(2,3, 2,BlockType.Stone),
                new BlockOverride(-2,3,-1,BlockType.Stone), new BlockOverride(-2,3, 0,BlockType.Stone), new BlockOverride(-2,3, 1,BlockType.Stone),
                new BlockOverride( 2,3,-1,BlockType.Stone), new BlockOverride( 2,3, 0,BlockType.Stone), new BlockOverride( 2,3, 1,BlockType.Stone),

                new BlockOverride(-2,4,-2,BlockType.Stone), new BlockOverride(0,4,-2,BlockType.Stone), new BlockOverride(2,4,-2,BlockType.Stone),
                new BlockOverride(-2,4, 2,BlockType.Stone), new BlockOverride(0,4, 2,BlockType.Stone), new BlockOverride(2,4, 2,BlockType.Stone),
                new BlockOverride(-2,4,-1,BlockType.Stone), new BlockOverride(-2,4, 0,BlockType.Stone), new BlockOverride(-2,4, 1,BlockType.Stone),
                new BlockOverride( 2,4,-1,BlockType.Stone), new BlockOverride( 2,4, 0,BlockType.Stone), new BlockOverride( 2,4, 1,BlockType.Stone),

                new BlockOverride(-2,5,-2,BlockType.Stone), new BlockOverride(0,5,-2,BlockType.Stone), new BlockOverride(2,5,-2,BlockType.Stone),
                new BlockOverride(-2,5, 2,BlockType.Stone), new BlockOverride(0,5, 2,BlockType.Stone), new BlockOverride(2,5, 2,BlockType.Stone),
                new BlockOverride(-2,5,-1,BlockType.Stone), new BlockOverride(-2,5, 0,BlockType.Stone), new BlockOverride(-2,5, 1,BlockType.Stone),
                new BlockOverride( 2,5,-1,BlockType.Stone), new BlockOverride( 2,5, 0,BlockType.Stone), new BlockOverride( 2,5, 1,BlockType.Stone),

                new BlockOverride(-2,6,-2,BlockType.Stone), new BlockOverride(0,6,-2,BlockType.Stone), new BlockOverride(2,6,-2,BlockType.Stone),
                new BlockOverride(-2,6, 2,BlockType.Stone), new BlockOverride(0,6, 2,BlockType.Stone), new BlockOverride(2,6, 2,BlockType.Stone),
                new BlockOverride(-2,6,-1,BlockType.Stone), new BlockOverride(-2,6, 0,BlockType.Stone), new BlockOverride(-2,6, 1,BlockType.Stone),
                new BlockOverride( 2,6,-1,BlockType.Stone), new BlockOverride( 2,6, 0,BlockType.Stone), new BlockOverride( 2,6, 1,BlockType.Stone),

                // Almenas del keep (capa 7)
                new BlockOverride(-2,7,-2,BlockType.Stone), new BlockOverride(2,7,-2,BlockType.Stone),
                new BlockOverride(-2,7, 2,BlockType.Stone), new BlockOverride(2,7, 2,BlockType.Stone),
                new BlockOverride( 0,7,-2,BlockType.Stone),
                new BlockOverride( 0,7, 2,BlockType.Stone),
                new BlockOverride(-2,7, 0,BlockType.Stone),
                new BlockOverride( 2,7, 0,BlockType.Stone),

                // ── Muralla exterior (fragmentos, derrumbada en el lado este) ──
                // Lado norte (completo)
                new BlockOverride(-8,0,-8,BlockType.Stone), new BlockOverride(-8,1,-8,BlockType.Stone), new BlockOverride(-8,2,-8,BlockType.Stone),
                new BlockOverride(-6,0,-8,BlockType.Stone), new BlockOverride(-6,1,-8,BlockType.Stone), new BlockOverride(-6,2,-8,BlockType.Stone),
                new BlockOverride(-4,0,-8,BlockType.Stone), new BlockOverride(-4,1,-8,BlockType.Stone),
                new BlockOverride(-2,0,-8,BlockType.Stone), new BlockOverride(-2,1,-8,BlockType.Stone), new BlockOverride(-2,2,-8,BlockType.Stone),
                new BlockOverride( 0,0,-8,BlockType.Stone), new BlockOverride( 0,1,-8,BlockType.Stone), new BlockOverride( 0,2,-8,BlockType.Stone),
                new BlockOverride( 2,0,-8,BlockType.Stone), new BlockOverride( 2,1,-8,BlockType.Stone),
                new BlockOverride( 4,0,-8,BlockType.Stone), new BlockOverride( 4,1,-8,BlockType.Stone), new BlockOverride( 4,2,-8,BlockType.Stone),
                new BlockOverride( 6,0,-8,BlockType.Stone), new BlockOverride( 6,1,-8,BlockType.Stone), new BlockOverride( 6,2,-8,BlockType.Stone),
                new BlockOverride( 8,0,-8,BlockType.Stone), new BlockOverride( 8,1,-8,BlockType.Stone), new BlockOverride( 8,2,-8,BlockType.Stone),

                // Lado oeste (completo)
                new BlockOverride(-8,0,-6,BlockType.Stone), new BlockOverride(-8,1,-6,BlockType.Stone), new BlockOverride(-8,2,-6,BlockType.Stone),
                new BlockOverride(-8,0,-4,BlockType.Stone), new BlockOverride(-8,1,-4,BlockType.Stone),
                new BlockOverride(-8,0,-2,BlockType.Stone), new BlockOverride(-8,1,-2,BlockType.Stone), new BlockOverride(-8,2,-2,BlockType.Stone),
                new BlockOverride(-8,0, 0,BlockType.Stone), new BlockOverride(-8,1, 0,BlockType.Stone), new BlockOverride(-8,2, 0,BlockType.Stone),
                new BlockOverride(-8,0, 2,BlockType.Stone), new BlockOverride(-8,1, 2,BlockType.Stone),
                new BlockOverride(-8,0, 4,BlockType.Stone), new BlockOverride(-8,1, 4,BlockType.Stone), new BlockOverride(-8,2, 4,BlockType.Stone),
                new BlockOverride(-8,0, 6,BlockType.Stone), new BlockOverride(-8,1, 6,BlockType.Stone), new BlockOverride(-8,2, 6,BlockType.Stone),
                new BlockOverride(-8,0, 8,BlockType.Stone), new BlockOverride(-8,1, 8,BlockType.Stone), new BlockOverride(-8,2, 8,BlockType.Stone),

                // Lado sur (parcialmente derrumbado)
                new BlockOverride(-8,0, 8,BlockType.Stone), new BlockOverride(-8,1, 8,BlockType.Stone),
                new BlockOverride(-6,0, 8,BlockType.Stone),
                new BlockOverride(-4,0, 8,BlockType.Stone), new BlockOverride(-4,1, 8,BlockType.Stone),
                new BlockOverride(-2,0, 8,BlockType.Stone), new BlockOverride(-2,1, 8,BlockType.Stone), new BlockOverride(-2,2, 8,BlockType.Stone),
                new BlockOverride( 0,0, 8,BlockType.Stone),
                new BlockOverride( 2,0, 8,BlockType.Stone), new BlockOverride( 2,1, 8,BlockType.Stone),
                new BlockOverride( 4,0, 8,BlockType.Stone), new BlockOverride( 4,1, 8,BlockType.Stone), new BlockOverride( 4,2, 8,BlockType.Stone),
                // Este lado colapsado: sólo escombros en el suelo
                new BlockOverride( 8,0, 4,BlockType.Stone), new BlockOverride( 8,0, 6,BlockType.Stone),
                new BlockOverride( 9,0, 5,BlockType.Stone), new BlockOverride( 7,0, 7,BlockType.Stone),
                new BlockOverride( 6,0, 9,BlockType.Stone), new BlockOverride( 5,0, 8,BlockType.Stone),

                // Escombros sueltos en el patio
                new BlockOverride(-5,0,-3,BlockType.Stone),
                new BlockOverride( 3,0, 4,BlockType.Stone), new BlockOverride( 3,1, 4,BlockType.Stone),
                new BlockOverride(-3,0, 5,BlockType.Stone),
                new BlockOverride( 5,0,-5,BlockType.Stone), new BlockOverride( 6,0,-4,BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // GALEÓN VARADO
        // Casco grande de dos mástiles con cubierta y bodega visible.
        // Solo en arena; poco frecuente.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef StrandedGalleon = new StructureDef
        {
            Name = "StrandedGalleon",
            SpawnChance = 0.015f,
            MinSpacing = 180,
            ValidSurfaces = new[] { BlockType.Sand },
            Blocks = new[]
            {
                // ── Quilla y fondo del casco ──
                new BlockOverride(-7,0,0,BlockType.Wood), new BlockOverride(-6,0,0,BlockType.Wood), new BlockOverride(-5,0,0,BlockType.Wood),
                new BlockOverride(-4,0,0,BlockType.Wood), new BlockOverride(-3,0,0,BlockType.Wood), new BlockOverride(-2,0,0,BlockType.Wood),
                new BlockOverride(-1,0,0,BlockType.Wood), new BlockOverride( 0,0,0,BlockType.Wood), new BlockOverride( 1,0,0,BlockType.Wood),
                new BlockOverride( 2,0,0,BlockType.Wood), new BlockOverride( 3,0,0,BlockType.Wood), new BlockOverride( 4,0,0,BlockType.Wood),
                new BlockOverride( 5,0,0,BlockType.Wood), new BlockOverride( 6,0,0,BlockType.Wood), new BlockOverride( 7,0,0,BlockType.Wood),

                // ── Costillas/cuadernas (cada 2) ──
                new BlockOverride(-7,1,-1,BlockType.Wood), new BlockOverride(-7,1, 1,BlockType.Wood), new BlockOverride(-7,2, 0,BlockType.Wood),

                new BlockOverride(-5,1,-2,BlockType.Wood), new BlockOverride(-5,1, 2,BlockType.Wood),
                new BlockOverride(-5,2,-2,BlockType.Wood), new BlockOverride(-5,2, 2,BlockType.Wood),
                new BlockOverride(-5,3,-1,BlockType.Wood), new BlockOverride(-5,3, 1,BlockType.Wood),

                new BlockOverride(-3,1,-2,BlockType.Wood), new BlockOverride(-3,1, 2,BlockType.Wood),
                new BlockOverride(-3,2,-2,BlockType.Wood), new BlockOverride(-3,2, 2,BlockType.Wood),
                new BlockOverride(-3,3,-1,BlockType.Wood), new BlockOverride(-3,3, 1,BlockType.Wood),

                new BlockOverride(-1,1,-3,BlockType.Wood), new BlockOverride(-1,1, 3,BlockType.Wood),
                new BlockOverride(-1,2,-3,BlockType.Wood), new BlockOverride(-1,2, 3,BlockType.Wood),
                new BlockOverride(-1,3,-2,BlockType.Wood), new BlockOverride(-1,3, 2,BlockType.Wood),

                new BlockOverride( 1,1,-3,BlockType.Wood), new BlockOverride( 1,1, 3,BlockType.Wood),
                new BlockOverride( 1,2,-3,BlockType.Wood), new BlockOverride( 1,2, 3,BlockType.Wood),
                new BlockOverride( 1,3,-2,BlockType.Wood), new BlockOverride( 1,3, 2,BlockType.Wood),

                new BlockOverride( 3,1,-2,BlockType.Wood), new BlockOverride( 3,1, 2,BlockType.Wood),
                new BlockOverride( 3,2,-2,BlockType.Wood), new BlockOverride( 3,2, 2,BlockType.Wood),
                new BlockOverride( 3,3,-1,BlockType.Wood), new BlockOverride( 3,3, 1,BlockType.Wood),

                new BlockOverride( 5,1,-2,BlockType.Wood), new BlockOverride( 5,1, 2,BlockType.Wood),
                new BlockOverride( 5,2,-2,BlockType.Wood), new BlockOverride( 5,2, 2,BlockType.Wood),
                new BlockOverride( 5,3,-1,BlockType.Wood), new BlockOverride( 5,3, 1,BlockType.Wood),

                new BlockOverride( 7,1,-1,BlockType.Wood), new BlockOverride( 7,1, 1,BlockType.Wood), new BlockOverride( 7,2, 0,BlockType.Wood),

                // ── Tablones de borda (cubierta) ──
                new BlockOverride(-6,3,-1,BlockType.Wood), new BlockOverride(-6,3, 0,BlockType.Wood), new BlockOverride(-6,3, 1,BlockType.Wood),
                new BlockOverride(-4,3,-2,BlockType.Wood), new BlockOverride(-4,3,-1,BlockType.Wood), new BlockOverride(-4,3, 0,BlockType.Wood), new BlockOverride(-4,3, 1,BlockType.Wood), new BlockOverride(-4,3, 2,BlockType.Wood),
                new BlockOverride(-2,3,-2,BlockType.Wood), new BlockOverride(-2,3,-1,BlockType.Wood), new BlockOverride(-2,3, 0,BlockType.Wood), new BlockOverride(-2,3, 1,BlockType.Wood), new BlockOverride(-2,3, 2,BlockType.Wood),
                new BlockOverride( 0,3,-2,BlockType.Wood), new BlockOverride( 0,3,-1,BlockType.Wood), new BlockOverride( 0,3, 0,BlockType.Wood), new BlockOverride( 0,3, 1,BlockType.Wood), new BlockOverride( 0,3, 2,BlockType.Wood),
                new BlockOverride( 2,3,-2,BlockType.Wood), new BlockOverride( 2,3,-1,BlockType.Wood), new BlockOverride( 2,3, 0,BlockType.Wood), new BlockOverride( 2,3, 1,BlockType.Wood), new BlockOverride( 2,3, 2,BlockType.Wood),
                new BlockOverride( 4,3,-2,BlockType.Wood), new BlockOverride( 4,3,-1,BlockType.Wood), new BlockOverride( 4,3, 0,BlockType.Wood), new BlockOverride( 4,3, 1,BlockType.Wood), new BlockOverride( 4,3, 2,BlockType.Wood),
                new BlockOverride( 6,3,-1,BlockType.Wood), new BlockOverride( 6,3, 0,BlockType.Wood), new BlockOverride( 6,3, 1,BlockType.Wood),

                // ── Castillo de popa (superestructura trasera) ──
                new BlockOverride(-7,3,-1,BlockType.Wood), new BlockOverride(-7,3, 0,BlockType.Wood), new BlockOverride(-7,3, 1,BlockType.Wood),
                new BlockOverride(-7,4,-1,BlockType.Wood), new BlockOverride(-7,4, 0,BlockType.Wood), new BlockOverride(-7,4, 1,BlockType.Wood),
                new BlockOverride(-6,4,-1,BlockType.Wood), new BlockOverride(-6,4, 0,BlockType.Wood), new BlockOverride(-6,4, 1,BlockType.Wood),

                // ── Mástil mayor (de pie, inclinado) ──
                new BlockOverride( 0,4, 0,BlockType.Wood),
                new BlockOverride( 0,5, 0,BlockType.Wood),
                new BlockOverride( 0,6, 0,BlockType.Wood),
                new BlockOverride( 0,7, 0,BlockType.Wood),
                new BlockOverride(-1,8, 0,BlockType.Wood),
                new BlockOverride(-1,9, 0,BlockType.Wood),
                // Verga transversal
                new BlockOverride(-3,7, 0,BlockType.Wood), new BlockOverride(-2,7, 0,BlockType.Wood),
                new BlockOverride( 1,7, 0,BlockType.Wood), new BlockOverride( 2,7, 0,BlockType.Wood), new BlockOverride( 3,7, 0,BlockType.Wood),

                // ── Mástil de proa (caído en diagonal) ──
                new BlockOverride( 5,4, 0,BlockType.Wood),
                new BlockOverride( 6,4, 0,BlockType.Wood),
                new BlockOverride( 7,4, 0,BlockType.Wood),
                new BlockOverride( 8,3, 0,BlockType.Wood),
                new BlockOverride( 9,3, 0,BlockType.Wood),

                // ── Escombros y cofres en cubierta ──
                new BlockOverride(-2,4, 1,BlockType.Stone),
                new BlockOverride( 2,4,-1,BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // TORRE DEL RELOJ
        // Torre alta y esbelta de piedra con un cuerpo de reloj cuadrado arriba.
        // Aparece en pasto; poco frecuente.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef ClockTower = new StructureDef
        {
            Name = "ClockTower",
            SpawnChance = 0.016f,
            MinSpacing = 150,
            ValidSurfaces = new[] { BlockType.Grass },
            Blocks = new[]
            {
                // ── Fuste de la torre (hueco, 3×3, 8 pisos) ──
                // Capa 0
                new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(0,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone),
                new BlockOverride(-1,0, 0,BlockType.Stone),                                             new BlockOverride(1,0, 0,BlockType.Stone),
                new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(0,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone),
                // Capas 1-7 (paredes huecas)
                new BlockOverride(-1,1,-1,BlockType.Stone), new BlockOverride(0,1,-1,BlockType.Stone), new BlockOverride(1,1,-1,BlockType.Stone),
                new BlockOverride(-1,1, 0,BlockType.Stone),                                             new BlockOverride(1,1, 0,BlockType.Stone),
                new BlockOverride(-1,1, 1,BlockType.Stone), new BlockOverride(0,1, 1,BlockType.Stone), new BlockOverride(1,1, 1,BlockType.Stone),

                new BlockOverride(-1,2,-1,BlockType.Stone), new BlockOverride(0,2,-1,BlockType.Stone), new BlockOverride(1,2,-1,BlockType.Stone),
                new BlockOverride(-1,2, 0,BlockType.Stone),                                             new BlockOverride(1,2, 0,BlockType.Stone),
                new BlockOverride(-1,2, 1,BlockType.Stone), new BlockOverride(0,2, 1,BlockType.Stone), new BlockOverride(1,2, 1,BlockType.Stone),

                new BlockOverride(-1,3,-1,BlockType.Stone), new BlockOverride(0,3,-1,BlockType.Stone), new BlockOverride(1,3,-1,BlockType.Stone),
                new BlockOverride(-1,3, 0,BlockType.Stone),                                             new BlockOverride(1,3, 0,BlockType.Stone),
                new BlockOverride(-1,3, 1,BlockType.Stone), new BlockOverride(0,3, 1,BlockType.Stone), new BlockOverride(1,3, 1,BlockType.Stone),

                new BlockOverride(-1,4,-1,BlockType.Stone), new BlockOverride(0,4,-1,BlockType.Stone), new BlockOverride(1,4,-1,BlockType.Stone),
                new BlockOverride(-1,4, 0,BlockType.Stone),                                             new BlockOverride(1,4, 0,BlockType.Stone),
                new BlockOverride(-1,4, 1,BlockType.Stone), new BlockOverride(0,4, 1,BlockType.Stone), new BlockOverride(1,4, 1,BlockType.Stone),

                new BlockOverride(-1,5,-1,BlockType.Stone), new BlockOverride(0,5,-1,BlockType.Stone), new BlockOverride(1,5,-1,BlockType.Stone),
                new BlockOverride(-1,5, 0,BlockType.Stone),                                             new BlockOverride(1,5, 0,BlockType.Stone),
                new BlockOverride(-1,5, 1,BlockType.Stone), new BlockOverride(0,5, 1,BlockType.Stone), new BlockOverride(1,5, 1,BlockType.Stone),

                new BlockOverride(-1,6,-1,BlockType.Stone), new BlockOverride(0,6,-1,BlockType.Stone), new BlockOverride(1,6,-1,BlockType.Stone),
                new BlockOverride(-1,6, 0,BlockType.Stone),                                             new BlockOverride(1,6, 0,BlockType.Stone),
                new BlockOverride(-1,6, 1,BlockType.Stone), new BlockOverride(0,6, 1,BlockType.Stone), new BlockOverride(1,6, 1,BlockType.Stone),

                new BlockOverride(-1,7,-1,BlockType.Stone), new BlockOverride(0,7,-1,BlockType.Stone), new BlockOverride(1,7,-1,BlockType.Stone),
                new BlockOverride(-1,7, 0,BlockType.Stone),                                             new BlockOverride(1,7, 0,BlockType.Stone),
                new BlockOverride(-1,7, 1,BlockType.Stone), new BlockOverride(0,7, 1,BlockType.Stone), new BlockOverride(1,7, 1,BlockType.Stone),

                // ── Cuerpo del reloj (5×5 macizo, capa 8) ──
                new BlockOverride(-2,8,-2,BlockType.Stone), new BlockOverride(-1,8,-2,BlockType.Stone), new BlockOverride(0,8,-2,BlockType.Stone), new BlockOverride(1,8,-2,BlockType.Stone), new BlockOverride(2,8,-2,BlockType.Stone),
                new BlockOverride(-2,8,-1,BlockType.Stone), new BlockOverride(-1,8,-1,BlockType.Stone), new BlockOverride(0,8,-1,BlockType.Stone), new BlockOverride(1,8,-1,BlockType.Stone), new BlockOverride(2,8,-1,BlockType.Stone),
                new BlockOverride(-2,8, 0,BlockType.Stone), new BlockOverride(-1,8, 0,BlockType.Stone), new BlockOverride(0,8, 0,BlockType.Stone), new BlockOverride(1,8, 0,BlockType.Stone), new BlockOverride(2,8, 0,BlockType.Stone),
                new BlockOverride(-2,8, 1,BlockType.Stone), new BlockOverride(-1,8, 1,BlockType.Stone), new BlockOverride(0,8, 1,BlockType.Stone), new BlockOverride(1,8, 1,BlockType.Stone), new BlockOverride(2,8, 1,BlockType.Stone),
                new BlockOverride(-2,8, 2,BlockType.Stone), new BlockOverride(-1,8, 2,BlockType.Stone), new BlockOverride(0,8, 2,BlockType.Stone), new BlockOverride(1,8, 2,BlockType.Stone), new BlockOverride(2,8, 2,BlockType.Stone),
                // Capa 9 del cuerpo del reloj
                new BlockOverride(-2,9,-2,BlockType.Stone), new BlockOverride(-1,9,-2,BlockType.Stone), new BlockOverride(0,9,-2,BlockType.Stone), new BlockOverride(1,9,-2,BlockType.Stone), new BlockOverride(2,9,-2,BlockType.Stone),
                new BlockOverride(-2,9,-1,BlockType.Stone), new BlockOverride(1,9,-1,BlockType.Stone), new BlockOverride(2,9,-1,BlockType.Stone), new BlockOverride(-1,9,-1,BlockType.Stone),
                new BlockOverride(-2,9, 0,BlockType.Stone), new BlockOverride(2,9, 0,BlockType.Stone),
                new BlockOverride(-2,9, 1,BlockType.Stone), new BlockOverride(-1,9, 1,BlockType.Stone), new BlockOverride(1,9, 1,BlockType.Stone), new BlockOverride(2,9, 1,BlockType.Stone),
                new BlockOverride(-2,9, 2,BlockType.Stone), new BlockOverride(-1,9, 2,BlockType.Stone), new BlockOverride(0,9, 2,BlockType.Stone), new BlockOverride(1,9, 2,BlockType.Stone), new BlockOverride(2,9, 2,BlockType.Stone),

                // ── Techo piramidal ──
                new BlockOverride(-1,10,-1,BlockType.Stone), new BlockOverride(0,10,-1,BlockType.Stone), new BlockOverride(1,10,-1,BlockType.Stone),
                new BlockOverride(-1,10, 0,BlockType.Stone),                                              new BlockOverride(1,10, 0,BlockType.Stone),
                new BlockOverride(-1,10, 1,BlockType.Stone), new BlockOverride(0,10, 1,BlockType.Stone), new BlockOverride(1,10, 1,BlockType.Stone),
                new BlockOverride( 0,11, 0,BlockType.Stone),

                // ── Veleta (madera en la punta) ──
                new BlockOverride( 0,12, 0,BlockType.Wood),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // POSADA DE CAMINO
        // Edificio rectangular de madera con tejado y establo lateral.
        // Muy común en pasto.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef RoadsideInn = new StructureDef
        {
            Name = "RoadsideInn",
            SpawnChance = 0.038f,
            MinSpacing = 100,
            ValidSurfaces = new[] { BlockType.Grass },
            Blocks = new[]
            {
                // ── Cuerpo principal (7×5 planta, 3 alto) ──
                // Capa 0 – paredes exteriores
                new BlockOverride(-3,0,-2,BlockType.Wood), new BlockOverride(-2,0,-2,BlockType.Wood), new BlockOverride(-1,0,-2,BlockType.Wood), new BlockOverride(0,0,-2,BlockType.Wood), new BlockOverride(1,0,-2,BlockType.Wood), new BlockOverride(2,0,-2,BlockType.Wood), new BlockOverride(3,0,-2,BlockType.Wood),
                new BlockOverride(-3,0, 2,BlockType.Wood), new BlockOverride(-2,0, 2,BlockType.Wood), new BlockOverride(-1,0, 2,BlockType.Wood), new BlockOverride(0,0, 2,BlockType.Wood), new BlockOverride(1,0, 2,BlockType.Wood), new BlockOverride(2,0, 2,BlockType.Wood), new BlockOverride(3,0, 2,BlockType.Wood),
                new BlockOverride(-3,0,-1,BlockType.Wood), new BlockOverride(-3,0, 0,BlockType.Wood), new BlockOverride(-3,0, 1,BlockType.Wood),
                new BlockOverride( 3,0,-1,BlockType.Wood), new BlockOverride( 3,0, 0,BlockType.Wood), new BlockOverride( 3,0, 1,BlockType.Wood),
                // Capa 1
                new BlockOverride(-3,1,-2,BlockType.Wood), new BlockOverride(-2,1,-2,BlockType.Wood), new BlockOverride(-1,1,-2,BlockType.Wood), new BlockOverride(0,1,-2,BlockType.Wood), new BlockOverride(1,1,-2,BlockType.Wood), new BlockOverride(2,1,-2,BlockType.Wood), new BlockOverride(3,1,-2,BlockType.Wood),
                new BlockOverride(-3,1, 2,BlockType.Wood), new BlockOverride(-2,1, 2,BlockType.Wood), new BlockOverride(-1,1, 2,BlockType.Wood), new BlockOverride(0,1, 2,BlockType.Wood), new BlockOverride(1,1, 2,BlockType.Wood), new BlockOverride(2,1, 2,BlockType.Wood), new BlockOverride(3,1, 2,BlockType.Wood),
                new BlockOverride(-3,1,-1,BlockType.Wood), new BlockOverride(-3,1, 0,BlockType.Wood), new BlockOverride(-3,1, 1,BlockType.Wood),
                new BlockOverride( 3,1,-1,BlockType.Wood), new BlockOverride( 3,1, 0,BlockType.Wood), new BlockOverride( 3,1, 1,BlockType.Wood),
                // Capa 2
                new BlockOverride(-3,2,-2,BlockType.Wood), new BlockOverride(-2,2,-2,BlockType.Wood), new BlockOverride(-1,2,-2,BlockType.Wood), new BlockOverride(0,2,-2,BlockType.Wood), new BlockOverride(1,2,-2,BlockType.Wood), new BlockOverride(2,2,-2,BlockType.Wood), new BlockOverride(3,2,-2,BlockType.Wood),
                new BlockOverride(-3,2, 2,BlockType.Wood), new BlockOverride(-2,2, 2,BlockType.Wood), new BlockOverride(-1,2, 2,BlockType.Wood), new BlockOverride(0,2, 2,BlockType.Wood), new BlockOverride(1,2, 2,BlockType.Wood), new BlockOverride(2,2, 2,BlockType.Wood), new BlockOverride(3,2, 2,BlockType.Wood),
                new BlockOverride(-3,2,-1,BlockType.Wood), new BlockOverride(-3,2, 0,BlockType.Wood), new BlockOverride(-3,2, 1,BlockType.Wood),
                new BlockOverride( 3,2,-1,BlockType.Wood), new BlockOverride( 3,2, 0,BlockType.Wood), new BlockOverride( 3,2, 1,BlockType.Wood),

                // ── Techo a dos aguas (piedra) ──
                new BlockOverride(-3,3,-2,BlockType.Stone), new BlockOverride(-2,3,-2,BlockType.Stone), new BlockOverride(-1,3,-2,BlockType.Stone), new BlockOverride(0,3,-2,BlockType.Stone), new BlockOverride(1,3,-2,BlockType.Stone), new BlockOverride(2,3,-2,BlockType.Stone), new BlockOverride(3,3,-2,BlockType.Stone),
                new BlockOverride(-3,3, 2,BlockType.Stone), new BlockOverride(-2,3, 2,BlockType.Stone), new BlockOverride(-1,3, 2,BlockType.Stone), new BlockOverride(0,3, 2,BlockType.Stone), new BlockOverride(1,3, 2,BlockType.Stone), new BlockOverride(2,3, 2,BlockType.Stone), new BlockOverride(3,3, 2,BlockType.Stone),
                new BlockOverride(-3,3,-1,BlockType.Stone), new BlockOverride(-2,3,-1,BlockType.Stone), new BlockOverride(-1,3,-1,BlockType.Stone), new BlockOverride(0,3,-1,BlockType.Stone), new BlockOverride(1,3,-1,BlockType.Stone), new BlockOverride(2,3,-1,BlockType.Stone), new BlockOverride(3,3,-1,BlockType.Stone),
                new BlockOverride(-3,3, 1,BlockType.Stone), new BlockOverride(-2,3, 1,BlockType.Stone), new BlockOverride(-1,3, 1,BlockType.Stone), new BlockOverride(0,3, 1,BlockType.Stone), new BlockOverride(1,3, 1,BlockType.Stone), new BlockOverride(2,3, 1,BlockType.Stone), new BlockOverride(3,3, 1,BlockType.Stone),
                // Cresta
                new BlockOverride(-3,4,0,BlockType.Stone), new BlockOverride(-2,4,0,BlockType.Stone), new BlockOverride(-1,4,0,BlockType.Stone), new BlockOverride(0,4,0,BlockType.Stone), new BlockOverride(1,4,0,BlockType.Stone), new BlockOverride(2,4,0,BlockType.Stone), new BlockOverride(3,4,0,BlockType.Stone),

                // ── Chimenea ──
                new BlockOverride(-2,3, 0,BlockType.Stone),
                new BlockOverride(-2,4, 0,BlockType.Stone),
                new BlockOverride(-2,5, 0,BlockType.Stone),

                // ── Establo lateral (anexo este, 4×3, 2 alto) ──
                new BlockOverride(4,0,-1,BlockType.Wood), new BlockOverride(5,0,-1,BlockType.Wood), new BlockOverride(6,0,-1,BlockType.Wood),
                new BlockOverride(4,0, 1,BlockType.Wood), new BlockOverride(5,0, 1,BlockType.Wood), new BlockOverride(6,0, 1,BlockType.Wood),
                new BlockOverride(6,0, 0,BlockType.Wood),
                new BlockOverride(4,1,-1,BlockType.Wood), new BlockOverride(5,1,-1,BlockType.Wood), new BlockOverride(6,1,-1,BlockType.Wood),
                new BlockOverride(4,1, 1,BlockType.Wood), new BlockOverride(5,1, 1,BlockType.Wood), new BlockOverride(6,1, 1,BlockType.Wood),
                new BlockOverride(6,1, 0,BlockType.Wood),
                // Techo plano del establo
                new BlockOverride(4,2,-1,BlockType.Stone), new BlockOverride(5,2,-1,BlockType.Stone), new BlockOverride(6,2,-1,BlockType.Stone),
                new BlockOverride(4,2, 0,BlockType.Stone), new BlockOverride(5,2, 0,BlockType.Stone), new BlockOverride(6,2, 0,BlockType.Stone),
                new BlockOverride(4,2, 1,BlockType.Stone), new BlockOverride(5,2, 1,BlockType.Stone), new BlockOverride(6,2, 1,BlockType.Stone),

                // ── Suelo interior (piedra) ──
                new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(0,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone), new BlockOverride(2,0,-1,BlockType.Stone),
                new BlockOverride(-2,0, 0,BlockType.Stone), new BlockOverride(-1,0, 0,BlockType.Stone), new BlockOverride(0,0, 0,BlockType.Stone), new BlockOverride(1,0, 0,BlockType.Stone), new BlockOverride(2,0, 0,BlockType.Stone),
                new BlockOverride(-2,0, 1,BlockType.Stone), new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(0,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone), new BlockOverride(2,0, 1,BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // OBELISCO DEL DESIERTO
        // Pilar cuadrado de piedra muy alto con base escalonada.
        // Solo en arena; raro.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef DesertObelisk = new StructureDef
        {
            Name = "DesertObelisk",
            SpawnChance = 0.013f,
            MinSpacing = 210,
            ValidSurfaces = new[] { BlockType.Sand },
            Blocks = new[]
            {
                // ── Escalón base 5×5 ──
                new BlockOverride(-2,0,-2,BlockType.Stone), new BlockOverride(-1,0,-2,BlockType.Stone), new BlockOverride(0,0,-2,BlockType.Stone), new BlockOverride(1,0,-2,BlockType.Stone), new BlockOverride(2,0,-2,BlockType.Stone),
                new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(0,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone), new BlockOverride(2,0,-1,BlockType.Stone),
                new BlockOverride(-2,0, 0,BlockType.Stone), new BlockOverride(-1,0, 0,BlockType.Stone), new BlockOverride(0,0, 0,BlockType.Stone), new BlockOverride(1,0, 0,BlockType.Stone), new BlockOverride(2,0, 0,BlockType.Stone),
                new BlockOverride(-2,0, 1,BlockType.Stone), new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(0,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone), new BlockOverride(2,0, 1,BlockType.Stone),
                new BlockOverride(-2,0, 2,BlockType.Stone), new BlockOverride(-1,0, 2,BlockType.Stone), new BlockOverride(0,0, 2,BlockType.Stone), new BlockOverride(1,0, 2,BlockType.Stone), new BlockOverride(2,0, 2,BlockType.Stone),

                // ── Escalón intermedio 3×3 ──
                new BlockOverride(-1,1,-1,BlockType.Stone), new BlockOverride(0,1,-1,BlockType.Stone), new BlockOverride(1,1,-1,BlockType.Stone),
                new BlockOverride(-1,1, 0,BlockType.Stone), new BlockOverride(0,1, 0,BlockType.Stone), new BlockOverride(1,1, 0,BlockType.Stone),
                new BlockOverride(-1,1, 1,BlockType.Stone), new BlockOverride(0,1, 1,BlockType.Stone), new BlockOverride(1,1, 1,BlockType.Stone),

                // ── Fuste 1×1 (pisos 2-11) ──
                new BlockOverride(0,2, 0,BlockType.Stone),
                new BlockOverride(0,3, 0,BlockType.Stone),
                new BlockOverride(0,4, 0,BlockType.Stone),
                new BlockOverride(0,5, 0,BlockType.Stone),
                new BlockOverride(0,6, 0,BlockType.Stone),
                new BlockOverride(0,7, 0,BlockType.Stone),
                new BlockOverride(0,8, 0,BlockType.Stone),
                new BlockOverride(0,9, 0,BlockType.Stone),
                new BlockOverride(0,10,0,BlockType.Stone),
                new BlockOverride(0,11,0,BlockType.Stone),

                // ── Pirámide cúspide ──
                new BlockOverride(-1,12, 0,BlockType.Stone), new BlockOverride(1,12, 0,BlockType.Stone),
                new BlockOverride( 0,12,-1,BlockType.Stone), new BlockOverride(0,12, 1,BlockType.Stone),
                new BlockOverride( 0,12, 0,BlockType.Stone),
                new BlockOverride( 0,13, 0,BlockType.Stone),

                // ── Escombros y arena acumulada alrededor ──
                new BlockOverride( 3,0, 0,BlockType.Stone),
                new BlockOverride(-3,0, 1,BlockType.Stone),
                new BlockOverride( 2,0, 3,BlockType.Stone),
                new BlockOverride(-2,0,-3,BlockType.Stone), new BlockOverride(-1,0,-3,BlockType.Stone),
                new BlockOverride( 0,0, 4,BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // REGISTRO
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef[] All =
        {
            RuinedCastle,
            StrandedGalleon,
            ClockTower,
            RoadsideInn,
            DesertObelisk,
        };
    }
}