using System;

namespace game
{
    public readonly struct BlockOverride
    {
        public readonly int Dx, Dy, Dz;
        public readonly byte BlockType;
        public readonly bool OnlyIfAir;

        public BlockOverride(int dx, int dy, int dz, byte blockType, bool onlyIfAir = false)
        {
            Dx = dx; Dy = dy; Dz = dz;
            BlockType = blockType;
            OnlyIfAir = onlyIfAir;
        }
    }

    public class StructureDef
    {
        public string Name;
        public float SpawnChance;
        public int MinSpacing;
        public byte[] ValidSurfaces;
        public BlockOverride[] Blocks;
    }

    public readonly struct StructurePlacement
    {
        public readonly string StructureName;
        public readonly int WorldX, WorldY, WorldZ;
        public readonly BlockOverride[] Blocks;

        public StructurePlacement(string name, int wx, int wy, int wz, BlockOverride[] blocks)
        {
            StructureName = name; WorldX = wx; WorldY = wy; WorldZ = wz; Blocks = blocks;
        }
    }

    public static class Structures
    {
        // ─────────────────────────────────────────────────────────────────────
        // CAMPAMENTO BANDIDO
        // Una fogata central rodeada de troncos y una pequeña empalizada.
        // Aparece en praderas y nieve; raro pero no rarísimo.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef BanditCamp = new StructureDef
        {
            Name = "BanditCamp",
            SpawnChance = 0.025f,
            MinSpacing = 130,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Snow },
            Blocks = new[]
            {
                // Fogata central (glowstone = fuego/ascua)
                new BlockOverride( 0, 0,  0, BlockType.Stone),
                new BlockOverride( 0, 1,  0, BlockType.Glowstone),

                // Troncos alrededor de la fogata (madera)
                new BlockOverride( 2, 0,  0, BlockType.Wood),
                new BlockOverride(-2, 0,  0, BlockType.Wood),
                new BlockOverride( 0, 0,  2, BlockType.Wood),
                new BlockOverride( 0, 0, -2, BlockType.Wood),

                // Empalizada: estacas de madera (postes verticales)
                new BlockOverride( 4, 0, -4, BlockType.Wood),
                new BlockOverride( 4, 1, -4, BlockType.Wood),
                new BlockOverride( 4, 2, -4, BlockType.Wood),

                new BlockOverride( 4, 0,  0, BlockType.Wood),
                new BlockOverride( 4, 1,  0, BlockType.Wood),
                new BlockOverride( 4, 2,  0, BlockType.Wood),

                new BlockOverride( 4, 0,  4, BlockType.Wood),
                new BlockOverride( 4, 1,  4, BlockType.Wood),
                new BlockOverride( 4, 2,  4, BlockType.Wood),

                new BlockOverride(-4, 0, -4, BlockType.Wood),
                new BlockOverride(-4, 1, -4, BlockType.Wood),
                new BlockOverride(-4, 2, -4, BlockType.Wood),

                new BlockOverride(-4, 0,  0, BlockType.Wood),
                new BlockOverride(-4, 1,  0, BlockType.Wood),
                new BlockOverride(-4, 2,  0, BlockType.Wood),

                new BlockOverride(-4, 0,  4, BlockType.Wood),
                new BlockOverride(-4, 1,  4, BlockType.Wood),
                new BlockOverride(-4, 2,  4, BlockType.Wood),

                new BlockOverride( 0, 0, -4, BlockType.Wood),
                new BlockOverride( 0, 1, -4, BlockType.Wood),
                new BlockOverride( 0, 2, -4, BlockType.Wood),

                new BlockOverride( 0, 0,  4, BlockType.Wood),
                new BlockOverride( 0, 1,  4, BlockType.Wood),
                new BlockOverride( 0, 2,  4, BlockType.Wood),

                // Cofre/caja de botín (stone = crate representado)
                new BlockOverride( 2, 0, -2, BlockType.Stone),
                new BlockOverride(-2, 0, -2, BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // TORRE DE GUARDIA
        // Torre cuadrada de piedra de ~8 bloques de alto con farola en la cima.
        // Aparece en pasto y piedra; poco frecuente.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef WatchTower = new StructureDef
        {
            Name = "WatchTower",
            SpawnChance = 0.018f,
            MinSpacing = 160,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone },
            Blocks = new[]
            {
                // Base 3×3
                new BlockOverride(-1, 0, -1, BlockType.Stone), new BlockOverride(0, 0, -1, BlockType.Stone), new BlockOverride(1, 0, -1, BlockType.Stone),
                new BlockOverride(-1, 0,  0, BlockType.Stone),                                                new BlockOverride(1, 0,  0, BlockType.Stone),
                new BlockOverride(-1, 0,  1, BlockType.Stone), new BlockOverride(0, 0,  1, BlockType.Stone), new BlockOverride(1, 0,  1, BlockType.Stone),

                // Piso 1
                new BlockOverride(-1, 1, -1, BlockType.Stone), new BlockOverride(0, 1, -1, BlockType.Stone), new BlockOverride(1, 1, -1, BlockType.Stone),
                new BlockOverride(-1, 1,  0, BlockType.Stone),                                                new BlockOverride(1, 1,  0, BlockType.Stone),
                new BlockOverride(-1, 1,  1, BlockType.Stone), new BlockOverride(0, 1,  1, BlockType.Stone), new BlockOverride(1, 1,  1, BlockType.Stone),

                // Piso 2
                new BlockOverride(-1, 2, -1, BlockType.Stone), new BlockOverride(0, 2, -1, BlockType.Stone), new BlockOverride(1, 2, -1, BlockType.Stone),
                new BlockOverride(-1, 2,  0, BlockType.Stone),                                                new BlockOverride(1, 2,  0, BlockType.Stone),
                new BlockOverride(-1, 2,  1, BlockType.Stone), new BlockOverride(0, 2,  1, BlockType.Stone), new BlockOverride(1, 2,  1, BlockType.Stone),

                // Piso 3
                new BlockOverride(-1, 3, -1, BlockType.Stone), new BlockOverride(0, 3, -1, BlockType.Stone), new BlockOverride(1, 3, -1, BlockType.Stone),
                new BlockOverride(-1, 3,  0, BlockType.Stone),                                                new BlockOverride(1, 3,  0, BlockType.Stone),
                new BlockOverride(-1, 3,  1, BlockType.Stone), new BlockOverride(0, 3,  1, BlockType.Stone), new BlockOverride(1, 3,  1, BlockType.Stone),

                // Piso 4
                new BlockOverride(-1, 4, -1, BlockType.Stone), new BlockOverride(0, 4, -1, BlockType.Stone), new BlockOverride(1, 4, -1, BlockType.Stone),
                new BlockOverride(-1, 4,  0, BlockType.Stone),                                                new BlockOverride(1, 4,  0, BlockType.Stone),
                new BlockOverride(-1, 4,  1, BlockType.Stone), new BlockOverride(0, 4,  1, BlockType.Stone), new BlockOverride(1, 4,  1, BlockType.Stone),

                // Plataforma techo + almenas
                new BlockOverride(-1, 5, -1, BlockType.Stone), new BlockOverride(0, 5, -1, BlockType.Stone), new BlockOverride(1, 5, -1, BlockType.Stone),
                new BlockOverride(-1, 5,  0, BlockType.Stone), new BlockOverride(0, 5,  0, BlockType.Stone), new BlockOverride(1, 5,  0, BlockType.Stone),
                new BlockOverride(-1, 5,  1, BlockType.Stone), new BlockOverride(0, 5,  1, BlockType.Stone), new BlockOverride(1, 5,  1, BlockType.Stone),

                // Almenas (esquinas más altas)
                new BlockOverride(-1, 6, -1, BlockType.Stone), new BlockOverride(1, 6, -1, BlockType.Stone),
                new BlockOverride(-1, 6,  1, BlockType.Stone), new BlockOverride(1, 6,  1, BlockType.Stone),

                // Antorcha/farola
                new BlockOverride( 0, 6,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // CABAÑA CAMPESINA
        // Casa pequeña de madera y piedra, con techo a dos aguas.
        // Muy común en praderas.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef PeasantHut = new StructureDef
        {
            Name = "PeasantHut",
            SpawnChance = 0.04f,
            MinSpacing = 90,
            ValidSurfaces = new[] { BlockType.Grass },
            Blocks = new[]
            {
                // Paredes (huecas): 5×5 planta, 3 alto
                // Capa 0
                new BlockOverride(-2, 0, -2, BlockType.Wood), new BlockOverride(-1, 0, -2, BlockType.Wood), new BlockOverride(0, 0, -2, BlockType.Wood), new BlockOverride(1, 0, -2, BlockType.Wood), new BlockOverride(2, 0, -2, BlockType.Wood),
                new BlockOverride(-2, 0,  2, BlockType.Wood), new BlockOverride(-1, 0,  2, BlockType.Wood), new BlockOverride(0, 0,  2, BlockType.Wood), new BlockOverride(1, 0,  2, BlockType.Wood), new BlockOverride(2, 0,  2, BlockType.Wood),
                new BlockOverride(-2, 0, -1, BlockType.Wood), new BlockOverride(-2, 0,  0, BlockType.Wood), new BlockOverride(-2, 0,  1, BlockType.Wood),
                new BlockOverride( 2, 0, -1, BlockType.Wood), new BlockOverride( 2, 0,  0, BlockType.Wood), new BlockOverride( 2, 0,  1, BlockType.Wood),

                // Capa 1
                new BlockOverride(-2, 1, -2, BlockType.Wood), new BlockOverride(-1, 1, -2, BlockType.Wood), new BlockOverride(0, 1, -2, BlockType.Wood), new BlockOverride(1, 1, -2, BlockType.Wood), new BlockOverride(2, 1, -2, BlockType.Wood),
                new BlockOverride(-2, 1,  2, BlockType.Wood), new BlockOverride(-1, 1,  2, BlockType.Wood), new BlockOverride(0, 1,  2, BlockType.Wood), new BlockOverride(1, 1,  2, BlockType.Wood), new BlockOverride(2, 1,  2, BlockType.Wood),
                new BlockOverride(-2, 1, -1, BlockType.Wood), new BlockOverride(-2, 1,  0, BlockType.Wood), new BlockOverride(-2, 1,  1, BlockType.Wood),
                new BlockOverride( 2, 1, -1, BlockType.Wood), new BlockOverride( 2, 1,  0, BlockType.Wood), new BlockOverride( 2, 1,  1, BlockType.Wood),

                // Capa 2
                new BlockOverride(-2, 2, -2, BlockType.Wood), new BlockOverride(-1, 2, -2, BlockType.Wood), new BlockOverride(0, 2, -2, BlockType.Wood), new BlockOverride(1, 2, -2, BlockType.Wood), new BlockOverride(2, 2, -2, BlockType.Wood),
                new BlockOverride(-2, 2,  2, BlockType.Wood), new BlockOverride(-1, 2,  2, BlockType.Wood), new BlockOverride(0, 2,  2, BlockType.Wood), new BlockOverride(1, 2,  2, BlockType.Wood), new BlockOverride(2, 2,  2, BlockType.Wood),
                new BlockOverride(-2, 2, -1, BlockType.Wood), new BlockOverride(-2, 2,  0, BlockType.Wood), new BlockOverride(-2, 2,  1, BlockType.Wood),
                new BlockOverride( 2, 2, -1, BlockType.Wood), new BlockOverride( 2, 2,  0, BlockType.Wood), new BlockOverride( 2, 2,  1, BlockType.Wood),

                // Techo a dos aguas (piedra, escalonado)
                new BlockOverride(-2, 3, -2, BlockType.Stone), new BlockOverride(-1, 3, -2, BlockType.Stone), new BlockOverride(0, 3, -2, BlockType.Stone), new BlockOverride(1, 3, -2, BlockType.Stone), new BlockOverride(2, 3, -2, BlockType.Stone),
                new BlockOverride(-2, 3,  2, BlockType.Stone), new BlockOverride(-1, 3,  2, BlockType.Stone), new BlockOverride(0, 3,  2, BlockType.Stone), new BlockOverride(1, 3,  2, BlockType.Stone), new BlockOverride(2, 3,  2, BlockType.Stone),
                new BlockOverride(-2, 3, -1, BlockType.Stone), new BlockOverride(-1, 3, -1, BlockType.Stone), new BlockOverride(0, 3, -1, BlockType.Stone), new BlockOverride(1, 3, -1, BlockType.Stone), new BlockOverride(2, 3, -1, BlockType.Stone),
                new BlockOverride(-2, 3,  1, BlockType.Stone), new BlockOverride(-1, 3,  1, BlockType.Stone), new BlockOverride(0, 3,  1, BlockType.Stone), new BlockOverride(1, 3,  1, BlockType.Stone), new BlockOverride(2, 3,  1, BlockType.Stone),
                // Cresta del techo
                new BlockOverride(-2, 4,  0, BlockType.Stone), new BlockOverride(-1, 4,  0, BlockType.Stone), new BlockOverride(0, 4,  0, BlockType.Stone), new BlockOverride(1, 4,  0, BlockType.Stone), new BlockOverride(2, 4,  0, BlockType.Stone),

                // Chimenea con luz
                new BlockOverride( 2, 3,  0, BlockType.Stone),
                new BlockOverride( 2, 4,  0, BlockType.Stone),
                new BlockOverride( 2, 5,  0, BlockType.Glowstone),

                // Suelo interior
                new BlockOverride(-1, 0, -1, BlockType.Stone), new BlockOverride(0, 0, -1, BlockType.Stone), new BlockOverride(1, 0, -1, BlockType.Stone),
                new BlockOverride(-1, 0,  0, BlockType.Stone), new BlockOverride(0, 0,  0, BlockType.Stone), new BlockOverride(1, 0,  0, BlockType.Stone),
                new BlockOverride(-1, 0,  1, BlockType.Stone), new BlockOverride(0, 0,  1, BlockType.Stone), new BlockOverride(1, 0,  1, BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // TUMBA OLVIDADA
        // Una lápida de piedra sola, con un pequeño cerco y luz tenue.
        // Aparece en pasto, piedra y nieve.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef ForgottenGrave = new StructureDef
        {
            Name = "ForgottenGrave",
            SpawnChance = 0.035f,
            MinSpacing = 70,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone, BlockType.Snow },
            Blocks = new[]
            {
                // Suelo de la tumba
                new BlockOverride( 0, 0,  0, BlockType.Stone),
                new BlockOverride(-1, 0,  0, BlockType.Stone),
                new BlockOverride( 1, 0,  0, BlockType.Stone),

                // Lápida (vertical)
                new BlockOverride( 0, 1,  0, BlockType.Stone),
                new BlockOverride( 0, 2,  0, BlockType.Stone),
                new BlockOverride(-1, 2,  0, BlockType.Stone),
                new BlockOverride( 1, 2,  0, BlockType.Stone),

                // Cerco de madera
                new BlockOverride(-2, 0, -1, BlockType.Wood),
                new BlockOverride(-2, 1, -1, BlockType.Wood),
                new BlockOverride(-2, 0,  0, BlockType.Wood),
                new BlockOverride(-2, 1,  0, BlockType.Wood),
                new BlockOverride(-2, 0,  1, BlockType.Wood),
                new BlockOverride(-2, 1,  1, BlockType.Wood),

                new BlockOverride( 2, 0, -1, BlockType.Wood),
                new BlockOverride( 2, 1, -1, BlockType.Wood),
                new BlockOverride( 2, 0,  0, BlockType.Wood),
                new BlockOverride( 2, 1,  0, BlockType.Wood),
                new BlockOverride( 2, 0,  1, BlockType.Wood),
                new BlockOverride( 2, 1,  1, BlockType.Wood),

                new BlockOverride(-1, 0, -1, BlockType.Wood),
                new BlockOverride( 0, 0, -1, BlockType.Wood),
                new BlockOverride( 1, 0, -1, BlockType.Wood),

                new BlockOverride(-1, 0,  1, BlockType.Wood),
                new BlockOverride( 0, 0,  1, BlockType.Wood),
                new BlockOverride( 1, 0,  1, BlockType.Wood),

                // Farolito fantasmal
                new BlockOverride( 0, 3,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // POZO DEL PUEBLO
        // Pozo circular de piedra con brocal y farola.
        // Muy común en praderas.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef VillageWell = new StructureDef
        {
            Name = "VillageWell",
            SpawnChance = 0.04f,
            MinSpacing = 80,
            ValidSurfaces = new[] { BlockType.Grass },
            Blocks = new[]
            {
                // Brocal (anillo exterior)
                new BlockOverride(-1, 0, -2, BlockType.Stone), new BlockOverride(0, 0, -2, BlockType.Stone), new BlockOverride(1, 0, -2, BlockType.Stone),
                new BlockOverride(-1, 0,  2, BlockType.Stone), new BlockOverride(0, 0,  2, BlockType.Stone), new BlockOverride(1, 0,  2, BlockType.Stone),
                new BlockOverride(-2, 0, -1, BlockType.Stone), new BlockOverride(-2, 0,  0, BlockType.Stone), new BlockOverride(-2, 0,  1, BlockType.Stone),
                new BlockOverride( 2, 0, -1, BlockType.Stone), new BlockOverride( 2, 0,  0, BlockType.Stone), new BlockOverride( 2, 0,  1, BlockType.Stone),

                // Borde alto del brocal
                new BlockOverride(-1, 1, -2, BlockType.Stone), new BlockOverride(0, 1, -2, BlockType.Stone), new BlockOverride(1, 1, -2, BlockType.Stone),
                new BlockOverride(-1, 1,  2, BlockType.Stone), new BlockOverride(0, 1,  2, BlockType.Stone), new BlockOverride(1, 1,  2, BlockType.Stone),
                new BlockOverride(-2, 1, -1, BlockType.Stone), new BlockOverride(-2, 1,  0, BlockType.Stone), new BlockOverride(-2, 1,  1, BlockType.Stone),
                new BlockOverride( 2, 1, -1, BlockType.Stone), new BlockOverride( 2, 1,  0, BlockType.Stone), new BlockOverride( 2, 1,  1, BlockType.Stone),

                // Postes del techo
                new BlockOverride(-2, 2, -2, BlockType.Wood), new BlockOverride(-2, 3, -2, BlockType.Wood), new BlockOverride(-2, 4, -2, BlockType.Wood),
                new BlockOverride( 2, 2, -2, BlockType.Wood), new BlockOverride( 2, 3, -2, BlockType.Wood), new BlockOverride( 2, 4, -2, BlockType.Wood),
                new BlockOverride(-2, 2,  2, BlockType.Wood), new BlockOverride(-2, 3,  2, BlockType.Wood), new BlockOverride(-2, 4,  2, BlockType.Wood),
                new BlockOverride( 2, 2,  2, BlockType.Wood), new BlockOverride( 2, 3,  2, BlockType.Wood), new BlockOverride( 2, 4,  2, BlockType.Wood),

                // Viga horizontal
                new BlockOverride(-1, 4, -2, BlockType.Wood), new BlockOverride(0, 4, -2, BlockType.Wood), new BlockOverride(1, 4, -2, BlockType.Wood),
                new BlockOverride(-1, 4,  2, BlockType.Wood), new BlockOverride(0, 4,  2, BlockType.Wood), new BlockOverride(1, 4,  2, BlockType.Wood),
                new BlockOverride(-2, 4, -1, BlockType.Wood), new BlockOverride(-2, 4,  0, BlockType.Wood), new BlockOverride(-2, 4,  1, BlockType.Wood),
                new BlockOverride( 2, 4, -1, BlockType.Wood), new BlockOverride( 2, 4,  0, BlockType.Wood), new BlockOverride( 2, 4,  1, BlockType.Wood),

                // Farola colgante central
                new BlockOverride( 0, 4,  0, BlockType.Wood),
                new BlockOverride( 0, 3,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // TEMPLO EN RUINAS
        // Columnas caídas y altar central; ambiente de civilización perdida.
        // Aparece en pasto, piedra y arena; raro.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef AncientTemple = new StructureDef
        {
            Name = "AncientTemple",
            SpawnChance = 0.012f,
            MinSpacing = 200,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone, BlockType.Sand },
            Blocks = new[]
            {
                // Plataforma elevada 7×7
                new BlockOverride(-3,0,-3,BlockType.Stone), new BlockOverride(-2,0,-3,BlockType.Stone), new BlockOverride(-1,0,-3,BlockType.Stone), new BlockOverride(0,0,-3,BlockType.Stone), new BlockOverride(1,0,-3,BlockType.Stone), new BlockOverride(2,0,-3,BlockType.Stone), new BlockOverride(3,0,-3,BlockType.Stone),
                new BlockOverride(-3,0,-2,BlockType.Stone), new BlockOverride(-2,0,-2,BlockType.Stone), new BlockOverride(-1,0,-2,BlockType.Stone), new BlockOverride(0,0,-2,BlockType.Stone), new BlockOverride(1,0,-2,BlockType.Stone), new BlockOverride(2,0,-2,BlockType.Stone), new BlockOverride(3,0,-2,BlockType.Stone),
                new BlockOverride(-3,0,-1,BlockType.Stone), new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(0,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone), new BlockOverride(2,0,-1,BlockType.Stone), new BlockOverride(3,0,-1,BlockType.Stone),
                new BlockOverride(-3,0, 0,BlockType.Stone), new BlockOverride(-2,0, 0,BlockType.Stone), new BlockOverride(-1,0, 0,BlockType.Stone), new BlockOverride(0,0, 0,BlockType.Stone), new BlockOverride(1,0, 0,BlockType.Stone), new BlockOverride(2,0, 0,BlockType.Stone), new BlockOverride(3,0, 0,BlockType.Stone),
                new BlockOverride(-3,0, 1,BlockType.Stone), new BlockOverride(-2,0, 1,BlockType.Stone), new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(0,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone), new BlockOverride(2,0, 1,BlockType.Stone), new BlockOverride(3,0, 1,BlockType.Stone),
                new BlockOverride(-3,0, 2,BlockType.Stone), new BlockOverride(-2,0, 2,BlockType.Stone), new BlockOverride(-1,0, 2,BlockType.Stone), new BlockOverride(0,0, 2,BlockType.Stone), new BlockOverride(1,0, 2,BlockType.Stone), new BlockOverride(2,0, 2,BlockType.Stone), new BlockOverride(3,0, 2,BlockType.Stone),
                new BlockOverride(-3,0, 3,BlockType.Stone), new BlockOverride(-2,0, 3,BlockType.Stone), new BlockOverride(-1,0, 3,BlockType.Stone), new BlockOverride(0,0, 3,BlockType.Stone), new BlockOverride(1,0, 3,BlockType.Stone), new BlockOverride(2,0, 3,BlockType.Stone), new BlockOverride(3,0, 3,BlockType.Stone),

                // Columnas (en pie y caídas)
                // Columna NE (en pie)
                new BlockOverride( 3, 1, -3, BlockType.Stone), new BlockOverride( 3, 2, -3, BlockType.Stone), new BlockOverride( 3, 3, -3, BlockType.Stone), new BlockOverride( 3, 4, -3, BlockType.Stone),
                // Columna NO (rota a mitad)
                new BlockOverride(-3, 1, -3, BlockType.Stone), new BlockOverride(-3, 2, -3, BlockType.Stone),
                // Columna SE (en pie)
                new BlockOverride( 3, 1,  3, BlockType.Stone), new BlockOverride( 3, 2,  3, BlockType.Stone), new BlockOverride( 3, 3,  3, BlockType.Stone), new BlockOverride( 3, 4,  3, BlockType.Stone),
                // Columna SO (caída en el suelo)
                new BlockOverride(-3, 1,  3, BlockType.Stone),
                new BlockOverride(-2, 1,  3, BlockType.Stone),
                new BlockOverride(-1, 1,  3, BlockType.Stone),

                // Altar central
                new BlockOverride( 0, 1,  0, BlockType.Stone),
                new BlockOverride( 0, 2,  0, BlockType.Stone),
                new BlockOverride(-1, 1,  0, BlockType.Stone), new BlockOverride( 1, 1,  0, BlockType.Stone),
                new BlockOverride( 0, 1, -1, BlockType.Stone), new BlockOverride( 0, 1,  1, BlockType.Stone),

                // Llama del altar
                new BlockOverride( 0, 3,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // MONOLITO RÚNICO
        // Una sola piedra vertical gigante con inscripciones (glowstone).
        // Aparece en cualquier bioma sólido; muy raro.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef RunicMonolith = new StructureDef
        {
            Name = "RunicMonolith",
            SpawnChance = 0.014f,
            MinSpacing = 220,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Stone, BlockType.Snow, BlockType.Sand },
            Blocks = new[]
            {
                // Base ancha
                new BlockOverride(-1, 0, -1, BlockType.Stone), new BlockOverride(0, 0, -1, BlockType.Stone), new BlockOverride(1, 0, -1, BlockType.Stone),
                new BlockOverride(-1, 0,  0, BlockType.Stone),                                                new BlockOverride(1, 0,  0, BlockType.Stone),
                new BlockOverride(-1, 0,  1, BlockType.Stone), new BlockOverride(0, 0,  1, BlockType.Stone), new BlockOverride(1, 0,  1, BlockType.Stone),
                new BlockOverride( 0, 0,  0, BlockType.Stone),

                // Columna central alta
                new BlockOverride( 0, 1, 0, BlockType.Stone),
                new BlockOverride( 0, 2, 0, BlockType.Stone),
                new BlockOverride( 0, 3, 0, BlockType.Stone),
                new BlockOverride( 0, 4, 0, BlockType.Stone),
                new BlockOverride( 0, 5, 0, BlockType.Stone),
                new BlockOverride( 0, 6, 0, BlockType.Stone),
                new BlockOverride( 0, 7, 0, BlockType.Stone),

                // Runas brillantes (glowstone incrustado)
                new BlockOverride( 0, 2, 0, BlockType.Glowstone),
                new BlockOverride( 0, 4, 0, BlockType.Glowstone),
                new BlockOverride( 0, 6, 0, BlockType.Glowstone),

                // Cima
                new BlockOverride(-1, 8, 0, BlockType.Stone), new BlockOverride(0, 8, 0, BlockType.Stone), new BlockOverride(1, 8, 0, BlockType.Stone),
                new BlockOverride( 0, 9, 0, BlockType.Stone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // NAVE ENCALLADA
        // Casco de barco de madera varado; solo en arena (costas).
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef ShipwreckHull = new StructureDef
        {
            Name = "ShipwreckHull",
            SpawnChance = 0.022f,
            MinSpacing = 140,
            ValidSurfaces = new[] { BlockType.Sand },
            Blocks = new[]
            {
                // Quilla (keel)
                new BlockOverride(-4, 0,  0, BlockType.Wood), new BlockOverride(-3, 0,  0, BlockType.Wood), new BlockOverride(-2, 0,  0, BlockType.Wood),
                new BlockOverride(-1, 0,  0, BlockType.Wood), new BlockOverride( 0, 0,  0, BlockType.Wood), new BlockOverride( 1, 0,  0, BlockType.Wood),
                new BlockOverride( 2, 0,  0, BlockType.Wood), new BlockOverride( 3, 0,  0, BlockType.Wood), new BlockOverride( 4, 0,  0, BlockType.Wood),

                // Costillar (cuadernas) cada 2 bloques
                new BlockOverride(-4, 1, -1, BlockType.Wood), new BlockOverride(-4, 1,  1, BlockType.Wood), new BlockOverride(-4, 2,  0, BlockType.Wood),
                new BlockOverride(-2, 1, -2, BlockType.Wood), new BlockOverride(-2, 1,  2, BlockType.Wood), new BlockOverride(-2, 2, -2, BlockType.Wood), new BlockOverride(-2, 2,  2, BlockType.Wood),
                new BlockOverride( 0, 1, -2, BlockType.Wood), new BlockOverride( 0, 1,  2, BlockType.Wood), new BlockOverride( 0, 2, -2, BlockType.Wood), new BlockOverride( 0, 2,  2, BlockType.Wood),
                new BlockOverride( 2, 1, -2, BlockType.Wood), new BlockOverride( 2, 1,  2, BlockType.Wood), new BlockOverride( 2, 2, -2, BlockType.Wood), new BlockOverride( 2, 2,  2, BlockType.Wood),
                new BlockOverride( 4, 1, -1, BlockType.Wood), new BlockOverride( 4, 1,  1, BlockType.Wood), new BlockOverride( 4, 2,  0, BlockType.Wood),

                // Tablones de borda (bordes superiores)
                new BlockOverride(-3, 2, -1, BlockType.Wood), new BlockOverride(-3, 2,  1, BlockType.Wood),
                new BlockOverride(-1, 2, -2, BlockType.Wood), new BlockOverride(-1, 2,  2, BlockType.Wood),
                new BlockOverride( 1, 2, -2, BlockType.Wood), new BlockOverride( 1, 2,  2, BlockType.Wood),
                new BlockOverride( 3, 2, -1, BlockType.Wood), new BlockOverride( 3, 2,  1, BlockType.Wood),

                // Mástil caído (horizontal)
                new BlockOverride(-1, 3,  0, BlockType.Wood),
                new BlockOverride( 0, 3,  0, BlockType.Wood),
                new BlockOverride( 1, 3,  0, BlockType.Wood),
                new BlockOverride( 2, 3,  0, BlockType.Wood),
                new BlockOverride( 3, 3,  0, BlockType.Wood),

                // Farolito en la proa
                new BlockOverride(-4, 2,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // PORTAL ARCANO
        // Un arco de piedra con piedras caídas y energía mágica brillando.
        // Muy raro, aparece en pasto y nieve.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef ArcanePortal = new StructureDef
        {
            Name = "ArcanePortal",
            SpawnChance = 0.009f,
            MinSpacing = 250,
            ValidSurfaces = new[] { BlockType.Grass, BlockType.Snow },
            Blocks = new[]
            {
                // Base del arco (dos pilares)
                new BlockOverride(-2, 0, 0, BlockType.Stone), new BlockOverride(-2, 1, 0, BlockType.Stone), new BlockOverride(-2, 2, 0, BlockType.Stone),
                new BlockOverride(-2, 3, 0, BlockType.Stone), new BlockOverride(-2, 4, 0, BlockType.Stone),
                new BlockOverride( 2, 0, 0, BlockType.Stone), new BlockOverride( 2, 1, 0, BlockType.Stone), new BlockOverride( 2, 2, 0, BlockType.Stone),
                new BlockOverride( 2, 3, 0, BlockType.Stone), new BlockOverride( 2, 4, 0, BlockType.Stone),

                // Arco superior
                new BlockOverride(-1, 5, 0, BlockType.Stone), new BlockOverride(0, 5, 0, BlockType.Stone), new BlockOverride(1, 5, 0, BlockType.Stone),
                new BlockOverride(-2, 5, 0, BlockType.Stone), new BlockOverride(2, 5, 0, BlockType.Stone),

                // Clave del arco con brillo
                new BlockOverride( 0, 6, 0, BlockType.Glowstone),

                // Piedras caídas alrededor (ruinas)
                new BlockOverride(-3, 0,  1, BlockType.Stone), new BlockOverride(-3, 1,  1, BlockType.Stone),
                new BlockOverride( 3, 0, -1, BlockType.Stone),
                new BlockOverride(-4, 0,  0, BlockType.Stone),
                new BlockOverride( 4, 0,  2, BlockType.Stone), new BlockOverride( 3, 0,  2, BlockType.Stone),
                new BlockOverride( 4, 0, -2, BlockType.Stone),

                // Energía interior del portal (brillante)
                new BlockOverride(-1, 1, 0, BlockType.Glowstone), new BlockOverride(0, 1, 0, BlockType.Glowstone), new BlockOverride(1, 1, 0, BlockType.Glowstone),
                new BlockOverride(-1, 2, 0, BlockType.Glowstone), new BlockOverride(0, 2, 0, BlockType.Glowstone), new BlockOverride(1, 2, 0, BlockType.Glowstone),
                new BlockOverride(-1, 3, 0, BlockType.Glowstone), new BlockOverride(0, 3, 0, BlockType.Glowstone), new BlockOverride(1, 3, 0, BlockType.Glowstone),
                new BlockOverride(-1, 4, 0, BlockType.Glowstone), new BlockOverride(0, 4, 0, BlockType.Glowstone), new BlockOverride(1, 4, 0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // FORTALEZA ENANA
        // Estructura compacta, baja y robusta de piedra; solo en montaña/piedra.
        // Muy rara.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef DwarvenOutpost = new StructureDef
        {
            Name = "DwarvenOutpost",
            SpawnChance = 0.01f,
            MinSpacing = 240,
            ValidSurfaces = new[] { BlockType.Stone, BlockType.Snow },
            Blocks = new[]
            {
                // Plataforma base 5×5
                new BlockOverride(-2,0,-2,BlockType.Stone), new BlockOverride(-1,0,-2,BlockType.Stone), new BlockOverride(0,0,-2,BlockType.Stone), new BlockOverride(1,0,-2,BlockType.Stone), new BlockOverride(2,0,-2,BlockType.Stone),
                new BlockOverride(-2,0,-1,BlockType.Stone), new BlockOverride(-1,0,-1,BlockType.Stone), new BlockOverride(0,0,-1,BlockType.Stone), new BlockOverride(1,0,-1,BlockType.Stone), new BlockOverride(2,0,-1,BlockType.Stone),
                new BlockOverride(-2,0, 0,BlockType.Stone), new BlockOverride(-1,0, 0,BlockType.Stone), new BlockOverride(0,0, 0,BlockType.Stone), new BlockOverride(1,0, 0,BlockType.Stone), new BlockOverride(2,0, 0,BlockType.Stone),
                new BlockOverride(-2,0, 1,BlockType.Stone), new BlockOverride(-1,0, 1,BlockType.Stone), new BlockOverride(0,0, 1,BlockType.Stone), new BlockOverride(1,0, 1,BlockType.Stone), new BlockOverride(2,0, 1,BlockType.Stone),
                new BlockOverride(-2,0, 2,BlockType.Stone), new BlockOverride(-1,0, 2,BlockType.Stone), new BlockOverride(0,0, 2,BlockType.Stone), new BlockOverride(1,0, 2,BlockType.Stone), new BlockOverride(2,0, 2,BlockType.Stone),

                // Paredes (2 alto, huecas)
                new BlockOverride(-2,1,-2,BlockType.Stone), new BlockOverride(-1,1,-2,BlockType.Stone), new BlockOverride(0,1,-2,BlockType.Stone), new BlockOverride(1,1,-2,BlockType.Stone), new BlockOverride(2,1,-2,BlockType.Stone),
                new BlockOverride(-2,1, 2,BlockType.Stone), new BlockOverride(-1,1, 2,BlockType.Stone), new BlockOverride(0,1, 2,BlockType.Stone), new BlockOverride(1,1, 2,BlockType.Stone), new BlockOverride(2,1, 2,BlockType.Stone),
                new BlockOverride(-2,1,-1,BlockType.Stone), new BlockOverride(-2,1, 0,BlockType.Stone), new BlockOverride(-2,1, 1,BlockType.Stone),
                new BlockOverride( 2,1,-1,BlockType.Stone), new BlockOverride( 2,1, 0,BlockType.Stone), new BlockOverride( 2,1, 1,BlockType.Stone),

                new BlockOverride(-2,2,-2,BlockType.Stone), new BlockOverride(-1,2,-2,BlockType.Stone), new BlockOverride(0,2,-2,BlockType.Stone), new BlockOverride(1,2,-2,BlockType.Stone), new BlockOverride(2,2,-2,BlockType.Stone),
                new BlockOverride(-2,2, 2,BlockType.Stone), new BlockOverride(-1,2, 2,BlockType.Stone), new BlockOverride(0,2, 2,BlockType.Stone), new BlockOverride(1,2, 2,BlockType.Stone), new BlockOverride(2,2, 2,BlockType.Stone),
                new BlockOverride(-2,2,-1,BlockType.Stone), new BlockOverride(-2,2, 0,BlockType.Stone), new BlockOverride(-2,2, 1,BlockType.Stone),
                new BlockOverride( 2,2,-1,BlockType.Stone), new BlockOverride( 2,2, 0,BlockType.Stone), new BlockOverride( 2,2, 1,BlockType.Stone),

                // Techo macizo
                new BlockOverride(-2,3,-2,BlockType.Stone), new BlockOverride(-1,3,-2,BlockType.Stone), new BlockOverride(0,3,-2,BlockType.Stone), new BlockOverride(1,3,-2,BlockType.Stone), new BlockOverride(2,3,-2,BlockType.Stone),
                new BlockOverride(-2,3,-1,BlockType.Stone), new BlockOverride(-1,3,-1,BlockType.Stone), new BlockOverride(0,3,-1,BlockType.Stone), new BlockOverride(1,3,-1,BlockType.Stone), new BlockOverride(2,3,-1,BlockType.Stone),
                new BlockOverride(-2,3, 0,BlockType.Stone), new BlockOverride(-1,3, 0,BlockType.Stone), new BlockOverride(0,3, 0,BlockType.Stone), new BlockOverride(1,3, 0,BlockType.Stone), new BlockOverride(2,3, 0,BlockType.Stone),
                new BlockOverride(-2,3, 1,BlockType.Stone), new BlockOverride(-1,3, 1,BlockType.Stone), new BlockOverride(0,3, 1,BlockType.Stone), new BlockOverride(1,3, 1,BlockType.Stone), new BlockOverride(2,3, 1,BlockType.Stone),
                new BlockOverride(-2,3, 2,BlockType.Stone), new BlockOverride(-1,3, 2,BlockType.Stone), new BlockOverride(0,3, 2,BlockType.Stone), new BlockOverride(1,3, 2,BlockType.Stone), new BlockOverride(2,3, 2,BlockType.Stone),

                // Torres esquineras (2 bloques extra)
                new BlockOverride(-2,4,-2,BlockType.Stone), new BlockOverride(-2,5,-2,BlockType.Stone),
                new BlockOverride( 2,4,-2,BlockType.Stone), new BlockOverride( 2,5,-2,BlockType.Stone),
                new BlockOverride(-2,4, 2,BlockType.Stone), new BlockOverride(-2,5, 2,BlockType.Stone),
                new BlockOverride( 2,4, 2,BlockType.Stone), new BlockOverride( 2,5, 2,BlockType.Stone),

                // Antorcha central en techo
                new BlockOverride( 0,4, 0, BlockType.Glowstone),

                // Forja interior (glowstone = fuego)
                new BlockOverride( 1,1, 1, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // ÁRBOL ESPIRITUAL
        // Un árbol muerto gigante con luz interna; lugar sagrado.
        // Aparece en pasto; extremadamente raro.
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef SpiritTree = new StructureDef
        {
            Name = "SpiritTree",
            SpawnChance = 0.007f,
            MinSpacing = 300,
            ValidSurfaces = new[] { BlockType.Grass },
            Blocks = new[]
            {
                // Raíces
                new BlockOverride(-2, 0,  0, BlockType.Wood), new BlockOverride(-1, 0,  1, BlockType.Wood),
                new BlockOverride( 2, 0,  0, BlockType.Wood), new BlockOverride( 1, 0, -1, BlockType.Wood),
                new BlockOverride( 0, 0,  2, BlockType.Wood), new BlockOverride(-1, 0, -2, BlockType.Wood),

                // Tronco grueso (3×3 en base, luego va adelgazando)
                new BlockOverride(-1, 1, -1, BlockType.Wood), new BlockOverride(0, 1, -1, BlockType.Wood), new BlockOverride(1, 1, -1, BlockType.Wood),
                new BlockOverride(-1, 1,  0, BlockType.Wood), new BlockOverride(0, 1,  0, BlockType.Wood), new BlockOverride(1, 1,  0, BlockType.Wood),
                new BlockOverride(-1, 1,  1, BlockType.Wood), new BlockOverride(0, 1,  1, BlockType.Wood), new BlockOverride(1, 1,  1, BlockType.Wood),

                new BlockOverride(-1, 2, -1, BlockType.Wood), new BlockOverride(0, 2, -1, BlockType.Wood), new BlockOverride(1, 2, -1, BlockType.Wood),
                new BlockOverride(-1, 2,  0, BlockType.Wood), new BlockOverride(0, 2,  0, BlockType.Wood), new BlockOverride(1, 2,  0, BlockType.Wood),
                new BlockOverride(-1, 2,  1, BlockType.Wood), new BlockOverride(0, 2,  1, BlockType.Wood), new BlockOverride(1, 2,  1, BlockType.Wood),

                new BlockOverride( 0, 3,  0, BlockType.Wood), new BlockOverride(-1, 3, 0, BlockType.Wood), new BlockOverride(1, 3, 0, BlockType.Wood),
                new BlockOverride( 0, 3, -1, BlockType.Wood), new BlockOverride( 0, 3, 1, BlockType.Wood),

                new BlockOverride( 0, 4,  0, BlockType.Wood),
                new BlockOverride( 0, 5,  0, BlockType.Wood),
                new BlockOverride( 0, 6,  0, BlockType.Wood),
                new BlockOverride( 0, 7,  0, BlockType.Wood),

                // Ramas
                new BlockOverride(-2, 5,  0, BlockType.Wood), new BlockOverride(-3, 5,  0, BlockType.Wood), new BlockOverride(-3, 6, 0, BlockType.Wood),
                new BlockOverride( 2, 5,  0, BlockType.Wood), new BlockOverride( 3, 5,  0, BlockType.Wood), new BlockOverride( 3, 6, 0, BlockType.Wood),
                new BlockOverride( 0, 5,  2, BlockType.Wood), new BlockOverride( 0, 5,  3, BlockType.Wood), new BlockOverride( 0, 6, 3, BlockType.Wood),
                new BlockOverride( 0, 6, -2, BlockType.Wood), new BlockOverride( 0, 7, -3, BlockType.Wood),

                // Luz espiritual (glowstone en las ramas y en el hueco del tronco)
                new BlockOverride( 0, 2,  0, BlockType.Glowstone),
                new BlockOverride(-3, 7,  0, BlockType.Glowstone),
                new BlockOverride( 3, 7,  0, BlockType.Glowstone),
                new BlockOverride( 0, 6,  3, BlockType.Glowstone),
                new BlockOverride( 0, 8, -3, BlockType.Glowstone),
                new BlockOverride( 0, 8,  0, BlockType.Glowstone),
            }
        };

        // ─────────────────────────────────────────────────────────────────────
        // REGISTRO
        // ─────────────────────────────────────────────────────────────────────
        public static readonly StructureDef[] All =
        {
            BanditCamp,
            WatchTower,
            PeasantHut,
            ForgottenGrave,
            VillageWell,
            AncientTemple,
            RunicMonolith,
            ShipwreckHull,
            ArcanePortal,
            DwarvenOutpost,
            SpiritTree,
        };
    }
}